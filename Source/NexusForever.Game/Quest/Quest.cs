using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Persistence;
using NexusForever.Game.Static.Quest;
using NexusForever.Network.World.Message.Model;
using NexusForever.Script;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Collection;
using NexusForever.Shared;
using NexusForever.Shared.Game;
using NLog;

namespace NexusForever.Game.Quest
{
    public class Quest : IQuest
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        [Flags]
        private enum QuestSaveMask
        {
            None   = 0x00,
            Create = 0x01,
            State  = 0x02,
            Flags  = 0x04,
            Reset  = 0x08,
            Delete = 0x10,
            Timer  = 0x20
        }

        public ushort Id => (ushort)Info.Entry.Id;
        public IQuestInfo Info { get; }

        public QuestState State
        {
            get => state;
            set
            {
                QuestState oldState = state;

                state = value;
                saveMask.Mark(QuestSaveMask.State);

                OnStateChange(oldState);
            }
        }

        private QuestState state;

        public QuestStateFlags Flags
        {
            get => flags;
            set
            {
                flags = value;
                saveMask.Mark(QuestSaveMask.Flags);
            }
        }

        private QuestStateFlags flags;

        public uint? Timer
        {
            get => timer;
            set
            {
                if (timer == value)
                    return;

                timer = value;
                saveMask.Mark(QuestSaveMask.Timer);
            }
        }

        private uint? timer;

        public DateTime? Reset
        {
            get => reset;
            set
            {
                if (reset == value)
                    return;

                reset = value;
                saveMask.Mark(QuestSaveMask.Reset);
            }
        }

        private DateTime? reset;

        /// <summary>
        /// Returns if <see cref="IQuest"/> is enqueued to be saved to the database.
        /// </summary>
        public bool PendingCreate => (saveMask.Current & QuestSaveMask.Create) != 0;

        /// <summary>
        /// Returns if <see cref="IQuest"/> is enqueued to be deleted from the database.
        /// </summary>
        public bool PendingDelete => (saveMask.Current & QuestSaveMask.Delete) != 0;

        private readonly VersionedSaveMask<QuestSaveMask> saveMask;

        private readonly IPlayer player;
        private readonly IGlobalQuestManager globalQuestManager;
        private readonly IScriptManager scriptManager;
        private readonly IAssetManager assetManager;
        private readonly List<IQuestObjective> objectives = new();

        private UpdateTimer questTimer;

        private IScriptCollection scriptCollection;

        /// <summary>
        /// Create a new <see cref="IQuest"/> from an existing database model.
        /// </summary>
        public Quest(IPlayer owner, IQuestInfo info, CharacterQuestModel model)
            : this(owner, info, model, null, null, null)
        {
        }

        internal Quest(
            IPlayer owner,
            IQuestInfo info,
            CharacterQuestModel model,
            IGlobalQuestManager globalQuestManager,
            IScriptManager scriptManager,
            IAssetManager assetManager)
        {
            player                  = owner;
            Info                    = info;
            this.globalQuestManager = globalQuestManager;
            this.scriptManager      = scriptManager;
            this.assetManager       = assetManager;
            state                   = (QuestState)model.State;
            flags                   = (QuestStateFlags)model.Flags;
            timer                   = model.Timer;
            reset                   = model.Reset;
            saveMask = new VersionedSaveMask<QuestSaveMask>();

            InitialisePersistedTimer();

            ValidateObjectiveCount(info);
            var objectiveModels = new Dictionary<byte, CharacterQuestObjectiveModel>();
            foreach (CharacterQuestObjectiveModel objectiveModel in model.QuestObjective.OrderBy(objective => objective.Index))
            {
                if (objectiveModel.Index >= info.Objectives.Count)
                {
                    log.Error($"Quest {Id} has an invalid persisted objective index {objectiveModel.Index}.");
                    continue;
                }

                if (!objectiveModels.TryAdd(objectiveModel.Index, objectiveModel))
                {
                    log.Error($"Quest {Id} has a duplicate persisted objective index {objectiveModel.Index}.");
                    continue;
                }
            }

            for (int index = 0; index < info.Objectives.Count; index++)
            {
                byte objectiveIndex = (byte)index;
                if (objectiveModels.TryGetValue(objectiveIndex, out CharacterQuestObjectiveModel objectiveModel))
                    objectives.Add(new QuestObjective(player, info, info.Objectives[index], objectiveModel, assetManager));
                else
                {
                    log.Warn($"Quest {Id} is missing persisted objective index {objectiveIndex}; recreating it.");
                    objectives.Add(new QuestObjective(player, info, info.Objectives[index], objectiveIndex, assetManager));
                }
            }

            if (objectives.Count == 0 && state == QuestState.Accepted)
            {
                state = QuestState.Achieved;
                saveMask.Mark(QuestSaveMask.State);
            }

            scriptCollection = GetScriptManager().InitialiseOwnedScripts<IQuest>(this, info.Entry.Id);
        }

        /// <summary>
        /// Create a new <see cref="IQuest"/> from supplied <see cref="IQuestInfo"/>.
        /// </summary>
        public Quest(IPlayer owner, IQuestInfo info)
            : this(owner, info, null, null, null)
        {
        }

        internal Quest(
            IPlayer owner,
            IQuestInfo info,
            IGlobalQuestManager globalQuestManager,
            IScriptManager scriptManager,
            IAssetManager assetManager)
        {
            player                  = owner;
            Info                    = info;
            this.globalQuestManager = globalQuestManager;
            this.scriptManager      = scriptManager;
            this.assetManager       = assetManager;
            state                   = QuestState.Accepted;

            ValidateObjectiveCount(info);
            for (int index = 0; index < info.Objectives.Count; index++)
            {
                byte objectiveIndex = (byte)index;
                objectives.Add(new QuestObjective(player, info, info.Objectives[index], objectiveIndex, assetManager));
            }

            if (objectives.Count == 0)
                state = QuestState.Achieved;

            saveMask = new VersionedSaveMask<QuestSaveMask>(QuestSaveMask.Create);

            scriptCollection = GetScriptManager().InitialiseOwnedScripts<IQuest>(this, info.Entry.Id);
        }

        private static void ValidateObjectiveCount(IQuestInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);
            if (info.Objectives == null || info.Objectives.Count > byte.MaxValue)
                throw new InvalidDataException($"Quest {info.Entry?.Id ?? 0u} has an invalid objective count.");
        }

        public void Dispose()
        {
            if (scriptCollection != null)
                GetScriptManager().Unload(scriptCollection);

            scriptCollection = null;
        }

        public void InitialiseTimer()
        {
            if (Info.Entry.MaxTimeAllowedMS != 0u)
            {
                questTimer = new UpdateTimer(Info.Entry.MaxTimeAllowedMS / 1000d);
                Timer = Info.Entry.MaxTimeAllowedMS;
            }

            // TODO: objective timers
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance, null);
        }

        /// <summary>
        /// Stage quest changes and acknowledge them after the character database commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            Save(context, commitScope, null);
        }

        /// <summary>
        /// Stage quest changes and register their successful-commit acknowledgements.
        /// </summary>
        /// <param name="context">Character database context.</param>
        /// <param name="commitScope">Scope receiving post-commit acknowledgements.</param>
        /// <param name="deleteAcknowledged">Action invoked when a requested deletion commits.</param>
        public void Save(CharacterContext context, ISaveCommitScope commitScope, Action deleteAcknowledged)
        {
            ArgumentNullException.ThrowIfNull(commitScope);

            VersionedSaveMaskSnapshot<QuestSaveMask> snapshot = saveMask.Capture();
            QuestSaveMask stagedMask = snapshot.Mask;
            bool stagesDelete = (stagedMask & QuestSaveMask.Delete) != 0;
            bool stagesTransientDelete = (stagedMask & (QuestSaveMask.Create | QuestSaveMask.Delete)) ==
                (QuestSaveMask.Create | QuestSaveMask.Delete);

            if (stagedMask != QuestSaveMask.None)
            {
                QuestState stagedState = state;
                QuestStateFlags stagedFlags = flags;
                uint? stagedTimer = timer;
                DateTime? stagedReset = reset;

                if (!stagesTransientDelete && (stagedMask & QuestSaveMask.Create) != 0)
                {
                    context.Add(new CharacterQuestModel
                    {
                        Id      = player.CharacterId,
                        QuestId = Id,
                        State   = (byte)stagedState,
                        Flags   = (byte)stagedFlags,
                        Timer   = stagedTimer,
                        Reset   = stagedReset
                    });
                }
                else if (!stagesTransientDelete && stagesDelete)
                {
                    var model = new CharacterQuestModel
                    {
                        Id      = player.CharacterId,
                        QuestId = Id
                    };

                    context.Entry(model).State = EntityState.Deleted;
                }
                else if (!stagesTransientDelete)
                {
                    var model = new CharacterQuestModel
                    {
                        Id      = player.CharacterId,
                        QuestId = Id
                    };

                    EntityEntry<CharacterQuestModel> entity = context.Attach(model);
                    if ((stagedMask & QuestSaveMask.State) != 0)
                    {
                        model.State = (byte)stagedState;
                        entity.Property(p => p.State).IsModified = true;
                    }

                    if ((stagedMask & QuestSaveMask.Flags) != 0)
                    {
                        model.Flags = (byte)stagedFlags;
                        entity.Property(p => p.Flags).IsModified = true;
                    }

                    if ((stagedMask & QuestSaveMask.Reset) != 0)
                    {
                        model.Reset = stagedReset;
                        entity.Property(p => p.Reset).IsModified = true;
                    }

                    if ((stagedMask & QuestSaveMask.Timer) != 0)
                    {
                        model.Timer = stagedTimer;
                        entity.Property(p => p.Timer).IsModified = true;
                    }
                }

                commitScope.Register(() => AcknowledgeSave(snapshot, stagedMask, deleteAcknowledged));
            }

            if (!stagesDelete)
                foreach (IQuestObjective objective in objectives)
                    objective.Save(context, commitScope);
        }

        private void AcknowledgeSave(VersionedSaveMaskSnapshot<QuestSaveMask> snapshot,
            QuestSaveMask stagedMask, Action deleteAcknowledged)
        {
            bool deleteStillRequested = PendingDelete;
            saveMask.Acknowledge(snapshot);

            if ((stagedMask & QuestSaveMask.Delete) == 0)
                return;

            if (deleteStillRequested)
            {
                deleteAcknowledged?.Invoke();
                return;
            }

            saveMask.Mark(QuestSaveMask.Create);
            foreach (IQuestObjective objective in objectives)
                objective.EnqueueCreate();
        }

        public void Update(double lastTick)
        {
            if (!double.IsFinite(lastTick) || lastTick <= 0d)
                return;

            scriptCollection?.Invoke<IUpdate>(s => s.Update(lastTick));

            if (questTimer == null
                || State != QuestState.Accepted)
                return;

            questTimer.Update(lastTick);
            Timer = GetRemainingMilliseconds(questTimer.Time);

            if (!questTimer.HasElapsed)
                return;

            // Ran out of time to complete the quest.
            State = QuestState.Botched;
            questTimer = null;
        }

        private void InitialisePersistedTimer()
        {
            if (timer == null)
            {
                if (state == QuestState.Accepted && Info.Entry.MaxTimeAllowedMS != 0u)
                {
                    log.Error($"Timed quest {Id} has no persisted timer and will be botched.");
                    state = QuestState.Botched;
                    saveMask.Mark(QuestSaveMask.State);
                }

                return;
            }

            uint maximum = Info.Entry.MaxTimeAllowedMS;
            if (maximum == 0u)
            {
                timer = null;
                saveMask.Mark(QuestSaveMask.Timer);
                return;
            }

            if (timer > maximum)
            {
                log.Warn($"Clamping invalid persisted timer {timer}ms for quest {Id} to {maximum}ms.");
                timer = maximum;
                saveMask.Mark(QuestSaveMask.Timer);
            }

            if (state != QuestState.Accepted)
                return;

            if (timer == 0u)
            {
                state = QuestState.Botched;
                saveMask.Mark(QuestSaveMask.State);
                return;
            }

            questTimer = new UpdateTimer(timer.Value / 1000d);
        }

        private static uint GetRemainingMilliseconds(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds <= 0d)
                return 0u;

            double milliseconds = Math.Ceiling(seconds * 1000d);
            return milliseconds >= uint.MaxValue ? uint.MaxValue : (uint)milliseconds;
        }

        /// <summary>
        /// Enqueue <see cref="IQuest"/> to be deleted from the database.
        /// </summary>
        public void EnqueueDelete(bool set)
        {
            if (set)
                saveMask.Mark(QuestSaveMask.Delete);
            else
                saveMask.Clear(QuestSaveMask.Delete);
        }

        /// <summary>
        /// Returns if <see cref="IQuest"/> can be deleted.
        /// </summary>
        public bool CanDelete()
        {
            return Info.IsQuestMentioned != true;
        }

        /// <summary>
        /// Returns if <see cref="IQuest"/> can be abandoned.
        /// </summary>
        public bool CanAbandon()
        {
            if (State != QuestState.Botched && Info.CannotAbandon())
                return false;

            if (State == QuestState.Achieved && Info.CannotAbandonWhenAchieved())
                return false;

            return true;
        }

        /// <summary>
        /// Returns if <see cref="IQuest"/> can be shared with another <see cref="IPlayer"/>.
        /// </summary>
        public bool CanShare()
        {
            if (Info.Entry.QuestShareEnum == 0u)
                return false;

            return State is QuestState.Accepted or QuestState.Achieved or QuestState.Completed;
        }

        /// <summary>
        /// Update any <see cref="IQuestObjective"/>'s with supplied <see cref="QuestObjectiveType"/> and data with progress.
        /// </summary>
        public void ObjectiveUpdate(QuestObjectiveType type, uint data, uint progress)
        {
            ObjectiveUpdate(type, data, progress, null);
        }

        /// <summary>
        /// Update matching objectives except for exact static objective identifiers.
        /// </summary>
        public void ObjectiveUpdate(
            QuestObjectiveType type,
            uint data,
            uint progress,
            IReadOnlySet<uint> excludedObjectiveIds)
        {
            if (PendingDelete)
                return;

            if (State == QuestState.Achieved)
                return;

            Dictionary<IQuestObjective, uint> previousProgress = CaptureObjectiveProgress();

            // Process in descending index order so one event cannot complete consecutive sequential objectives.
            foreach (IQuestObjective objective in objectives
                .Where(o => o.ObjectiveInfo.Type == type
                    && o.IsTarget(data)
                    && (excludedObjectiveIds == null
                        || !excludedObjectiveIds.Contains(o.ObjectiveInfo.Id)))
                .OrderByDescending(o => o.Index))
                UpdateObjective(objective, progress);

            bool requiredObjectivesComplete = RequiredObjectivesComplete();
            SendChangedObjectives(previousProgress, containNotificationFailures: true);

            if (requiredObjectivesComplete)
            {
                try
                {
                    State = QuestState.Achieved;
                }
                catch (Exception exception)
                {
                    // State is assigned before OnStateChange publishes notifications and callbacks.
                    log.Error(exception, $"Quest {Id} achieved-state notification failed after state committed.");
                }
            }
        }

        /// <summary>
        /// Update any <see cref="IQuestObjective"/>'s with supplied ID with progress.
        /// </summary>
        public void ObjectiveUpdate(uint id, uint progress)
        {
            IQuestObjective objective = objectives.SingleOrDefault(o => o.ObjectiveInfo.Id == id);
            if (objective == null)
                return;

            TryObjectiveUpdate(objective.Index, progress, out _);
        }

        /// <summary>
        /// Attempts to apply raw progress to an exact zero-based objective slot.
        /// </summary>
        public bool TryObjectiveUpdate(
            byte objectiveIndex,
            uint progress,
            out IQuestObjective objective)
        {
            objective = null;
            if (PendingDelete || State != QuestState.Accepted || progress == 0u)
                return false;

            objective = objectives.FirstOrDefault(candidate => candidate.Index == objectiveIndex);
            if (objective == null || objective.IsComplete() || !CanUpdateObjective(objective))
                return false;

            IQuestObjective updatedObjective = objective;
            uint previousProgress = updatedObjective.Progress;
            try
            {
                updatedObjective.ObjectiveUpdate(progress);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Quest {Id} objective {objectiveIndex} failed while applying progress.");
            }

            if (updatedObjective.Progress == previousProgress)
                return false;

            try
            {
                scriptCollection?.Invoke<IQuestScript>(script => script.OnObjectiveUpdate(updatedObjective));
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Quest {Id} objective {objectiveIndex} progress callback failed after progress committed.");
            }

            try
            {
                SendQuestObjectiveUpdate(updatedObjective);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Quest {Id} objective {objectiveIndex} update notification failed after progress committed.");
            }

            if (RequiredObjectivesComplete())
            {
                try
                {
                    State = QuestState.Achieved;
                }
                catch (Exception exception)
                {
                    // State is assigned before OnStateChange publishes notifications and callbacks.
                    log.Error(exception, $"Quest {Id} achieved-state notification failed after state committed.");
                }
            }

            return true;
        }

        private Dictionary<IQuestObjective, uint> CaptureObjectiveProgress()
        {
            return objectives.ToDictionary(objective => objective, objective => objective.Progress);
        }

        private void UpdateObjective(IQuestObjective objective, uint progress)
        {
            if (objective.IsComplete() || !CanUpdateObjective(objective))
                return;

            uint previousProgress = objective.Progress;
            try
            {
                objective.ObjectiveUpdate(progress);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Quest {Id} objective {objective.Index} failed while applying progress.");
            }

            if (objective.Progress == previousProgress)
                return;

            try
            {
                scriptCollection?.Invoke<IQuestScript>(script => script.OnObjectiveUpdate(objective));
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Quest {Id} objective {objective.Index} progress callback failed after progress committed.");
            }
        }

        private bool CanUpdateObjective(IQuestObjective objective)
        {
            if (objective.ObjectiveInfo.IsSequential())
            {
                for (int i = 0; i < objective.Index; i++)
                    if (!objectives[i].ObjectiveInfo.IsOptional() && !objectives[i].IsComplete())
                        return false;
            }

            // TODO: client also checks objective flags 1 and 8 in the same function
            return true;
        }

        private bool RequiredObjectivesComplete()
        {
            if (objectives.Count == 0)
                return true;

            IQuestObjective[] requiredObjectives = objectives
                .Where(objective => !objective.ObjectiveInfo.IsOptional())
                .ToArray();
            return requiredObjectives.Length == 0
                ? objectives.All(objective => objective.IsComplete())
                : requiredObjectives.All(objective => objective.IsComplete());
        }

        /// <summary>
        /// Complete the objective with the supplied identifier using its exact completion representation.
        /// </summary>
        public void ObjectiveComplete(uint id)
        {
            IQuestObjective objective = objectives.SingleOrDefault(value => value.ObjectiveInfo.Id == id);
            if (objective == null)
                return;

            CompleteObjectives([objective]);
        }

        /// <summary>
        /// Complete every objective using its exact completion representation.
        /// </summary>
        public void ObjectivesComplete()
        {
            CompleteObjectives(objectives);
        }

        private void CompleteObjectives(IEnumerable<IQuestObjective> selectedObjectives)
        {
            if (PendingDelete || State != QuestState.Accepted)
                return;

            Dictionary<IQuestObjective, uint> previousProgress = CaptureObjectiveProgress();
            foreach (IQuestObjective objective in selectedObjectives.OrderBy(value => value.Index))
            {
                if (objective.IsComplete())
                    continue;

                objective.Complete();
                if (objective.Progress != previousProgress[objective])
                    scriptCollection?.Invoke<IQuestScript>(script => script.OnObjectiveUpdate(objective));
            }

            bool requiredObjectivesComplete = RequiredObjectivesComplete();
            SendChangedObjectives(previousProgress);

            if (requiredObjectivesComplete)
                State = QuestState.Achieved;
        }

        private void SendChangedObjectives(
            IReadOnlyDictionary<IQuestObjective, uint> previousProgress,
            bool containNotificationFailures = false)
        {
            foreach (IQuestObjective objective in objectives.OrderBy(objective => objective.Index))
                if (previousProgress.TryGetValue(objective, out uint progress)
                    && objective.Progress != progress)
                {
                    if (!containNotificationFailures)
                    {
                        SendQuestObjectiveUpdate(objective);
                        continue;
                    }

                    try
                    {
                        SendQuestObjectiveUpdate(objective);
                    }
                    catch (Exception exception)
                    {
                        log.Error(exception, $"Quest {Id} objective {objective.Index} update notification failed after progress committed.");
                    }
                }
        }

        private void SendQuestObjectiveUpdate(IQuestObjective objective)
        {
            // Only update objectives if the state isn't complete. Some scripts will complete quest without objective update.
            if (State == QuestState.Completed)
                return;

            player.Session.EnqueueMessageEncrypted(new ServerQuestObjectiveUpdate
            {
                QuestId   = Id,
                QuestObjectiveIndex     = objective.Index,
                Completed = objective.Progress
            });
        }

        /// <summary>
        /// Invoked when <see cref="QuestState"/> for <see cref="IQuest"/> is updated.
        /// </summary>
        private void OnStateChange(QuestState oldState)
        {
            player.Session.EnqueueMessageEncrypted(new ServerQuestStateChange
            {
                QuestId    = Id,
                QuestState = State
            });

            try
            {
                SendCommunicatorMessages();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to resolve communicator messages for quest {Id} state {state}.");
            }
            finally
            {
                scriptCollection?.Invoke<IQuestScript>(s => s.OnQuestStateChange(State, oldState));
            }
        }

        private void SendCommunicatorMessages()
        {
            IGlobalQuestManager manager = GetGlobalQuestManager();
            IEnumerable<ICommunicatorMessage> messages =
                manager.GetQuestCommunicatorQuestStateTriggers(Id, state)
                ?? Enumerable.Empty<ICommunicatorMessage>();
            var mentionedQuestIds = new HashSet<ushort>();

            foreach (ICommunicatorMessage message in messages)
            {
                try
                {
                    if (message == null || !message.Meets(player))
                        continue;

                    // The communicator row is the primary delivery. Any quest mention is
                    // optional metadata and must not replace the exact build 16042 message.
                    message.Send(player.Session);

                    ushort questId = message.QuestId;
                    if (questId == 0 || !mentionedQuestIds.Add(questId))
                        continue;

                    if (manager.GetQuestInfo(questId) == null)
                    {
                        log.Error($"A communicator message references missing quest {questId}.");
                        continue;
                    }

                    if (player.QuestManager == null)
                    {
                        log.Error($"A communicator message could not mention quest {questId} without a quest manager.");
                        continue;
                    }

                    player.QuestManager.QuestMention(questId);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to deliver a communicator message for quest {Id}.");
                }
            }
        }

        private IGlobalQuestManager GetGlobalQuestManager()
        {
            return globalQuestManager ?? GlobalQuestManager.Instance;
        }

        private IScriptManager GetScriptManager()
        {
            return scriptManager ?? ScriptManager.Instance;
        }

        public IEnumerator<IQuestObjective> GetEnumerator()
        {
            return objectives.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
