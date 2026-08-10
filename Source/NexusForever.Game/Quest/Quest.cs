using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
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

namespace NexusForever.Game.Quest
{
    public class Quest : IQuest
    {
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
        private readonly List<IQuestObjective> objectives = new();

        private UpdateTimer questTimer;

        private IScriptCollection scriptCollection;

        /// <summary>
        /// Create a new <see cref="IQuest"/> from an existing database model.
        /// </summary>
        public Quest(IPlayer owner, IQuestInfo info, CharacterQuestModel model)
        {
            player = owner;
            Info   = info;
            state  = (QuestState)model.State;
            flags  = (QuestStateFlags)model.Flags;
            timer  = model.Timer;
            reset  = model.Reset;
            saveMask = new VersionedSaveMask<QuestSaveMask>();

            if (timer != null)
                questTimer = new UpdateTimer(timer.Value);

            foreach (CharacterQuestObjectiveModel objectiveModel in model.QuestObjective)
                objectives.Add(new QuestObjective(player, info, info.Objectives[objectiveModel.Index], objectiveModel));

            scriptCollection = ScriptManager.Instance.InitialiseOwnedScripts<IQuest>(this, info.Entry.Id);
        }

        /// <summary>
        /// Create a new <see cref="IQuest"/> from supplied <see cref="IQuestInfo"/>.
        /// </summary>
        public Quest(IPlayer owner, IQuestInfo info)
        {
            player = owner;
            Info   = info;
            state  = QuestState.Accepted;

            for (byte i = 0; i < info.Objectives.Count; i++)
                objectives.Add(new QuestObjective(player, info, info.Objectives[i], i));

            if (objectives.Count == 0)
                state = QuestState.Achieved;

            saveMask = new VersionedSaveMask<QuestSaveMask>(QuestSaveMask.Create);

            scriptCollection = ScriptManager.Instance.InitialiseOwnedScripts<IQuest>(this, info.Entry.Id);
        }

        public void Dispose()
        {
            if (scriptCollection != null)
                ScriptManager.Instance.Unload(scriptCollection);

            scriptCollection = null;
        }

        public void InitialiseTimer()
        {
            if (Info.Entry.MaxTimeAllowedMS != 0u)
            {
                questTimer = new UpdateTimer(Info.Entry.MaxTimeAllowedMS / 1000d);
                Timer = (uint)(questTimer.Time * 1000d);
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
            scriptCollection?.Invoke<IUpdate>(s => s.Update(lastTick));

            if (questTimer != null)
            {
                questTimer.Update(lastTick);
                Timer = (uint)(questTimer.Time * 1000d);

                if (questTimer.HasElapsed)
                {
                    // ran out of time to complete quest
                    State = QuestState.Botched;
                    questTimer = null;
                }
            }
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
            if (PendingDelete)
                return;

            if (State == QuestState.Achieved)
                return;

            // Order in reverse Index so that sequential steps don't completed by the same action
            foreach (IQuestObjective objective in objectives
                .Where(o => o.ObjectiveInfo.Entry.Type == (uint)type && o.ObjectiveInfo.Entry.Data == data)
                .OrderByDescending(o => o.Index))
            {
                if (objective.IsComplete())
                    continue;

                if (!CanUpdateObjective(objective))
                    continue;

                uint oldProgress = objective.Progress;
                objective.ObjectiveUpdate(progress);

                if (objective.Progress != oldProgress)
                    SendQuestObjectiveUpdate(objective);

                scriptCollection?.Invoke<IQuestScript>(s => s.OnObjectiveUpdate(objective));
            }

            // TODO: Should you be able to complete optional objectives after required are completed?
            if (RequiredObjectivesComplete())
                CompleteOptionalObjectives();

            if (objectives.All(o => o.IsComplete()))
                State = QuestState.Achieved;
        }

        /// <summary>
        /// Update any <see cref="IQuestObjective"/>'s with supplied ID with progress.
        /// </summary>
        public void ObjectiveUpdate(uint id, uint progress)
        {
            if (PendingDelete)
                return;

            if (State == QuestState.Achieved)
                return;

            IQuestObjective objective = objectives.SingleOrDefault(o => o.ObjectiveInfo.Id == id);
            if (objective == null)
                return;

            if (objective.IsComplete())
                return;

            if (!CanUpdateObjective(objective))
                return;

            uint oldProgress = objective.Progress;
            objective.ObjectiveUpdate(progress);

            if (objective.Progress != oldProgress)
                SendQuestObjectiveUpdate(objective);

            scriptCollection?.Invoke<IQuestScript>(s => s.OnObjectiveUpdate(objective));

            // TODO: Should you be able to complete optional objectives after required are completed?
            if (RequiredObjectivesComplete())
                CompleteOptionalObjectives();

            if (objectives.All(o => o.IsComplete()))
                State = QuestState.Achieved;
        }

        private bool CanUpdateObjective(IQuestObjective objective)
        {
            if (objective.ObjectiveInfo.IsSequential())
            {
                for (int i = 0; i < objective.Index; i++)
                    if (!objectives[i].IsComplete())
                        return false;
            }

            // TODO: client also checks objective flags 1 and 8 in the same function
            return true;
        }

        private bool RequiredObjectivesComplete()
        {
            return objectives
                .Where(o => !o.ObjectiveInfo.IsOptional())
                .All(o => o.IsComplete());
        }

        private void CompleteOptionalObjectives()
        {
            foreach (IQuestObjective objective in objectives
                .Where(o => o.ObjectiveInfo.IsOptional() && !o.IsComplete()))
            {
                objective.Complete();
                SendQuestObjectiveUpdate(objective);
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

            // check if this quest and state is a trigger for a new communicator message
            foreach (ICommunicatorMessage message in GlobalQuestManager.Instance.GetQuestCommunicatorQuestStateTriggers(Id, state))
                if (message.Meets(player))
                    player.QuestManager.QuestMention(message.QuestId);

            scriptCollection?.Invoke<IQuestScript>(s => s.OnQuestStateChange(State, oldState));
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
