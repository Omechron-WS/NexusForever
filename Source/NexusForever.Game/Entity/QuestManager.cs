using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Persistence;
using NexusForever.Game.Prerequisite;
using NexusForever.Game.Quest;
using NexusForever.Game.Reputation;
using NexusForever.Game.Static;
using NexusForever.Game.Static.Achievement;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Game.Static.Reputation;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Static;
using NexusForever.Shared;
using NLog;

namespace NexusForever.Game.Entity
{
    public class QuestManager : IQuestManager
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private const double QuestShareDuration = 10d;

        [Flags]
        private enum GetQuestFlags
        {
            Completed = 0x01,
            Inactive  = 0x02,
            Active    = 0x04,
            All       = Completed | Inactive | Active
        }

        private readonly IPlayer player;
        private readonly IGlobalQuestManager globalQuestManager;
        private IQuestRewardManager questRewardManager;
        private readonly IDisableManager disableManager;
        private readonly Func<DateTime> utcNow;
        private readonly IItemManager itemManager;
        private readonly IPrerequisiteManager prerequisiteManager;
        private readonly IGameTableManager gameTableManager;
        private readonly Func<IQuestInfo, IQuest> questFactory;

        private double elapsedTime;
        private PendingQuestShare pendingQuestShare;
        private int disposed;

        private readonly Dictionary<ushort, IQuest> completedQuests = new();
        private readonly Dictionary<ushort, IQuest> inactiveQuests = new();
        private readonly Dictionary<ushort, IQuest> activeQuests = new();
        private readonly HashSet<ushort> completingQuests = new();

        /// <summary>
        /// Create a new <see cref="IQuestManager"/> from existing <see cref="CharacterModel"/> database model.
        /// </summary>
        public QuestManager(IPlayer owner, CharacterModel model)
            : this(owner, model, null, null, null, null)
        {
        }

        internal QuestManager(
            IPlayer owner,
            CharacterModel model,
            IGlobalQuestManager globalQuestManager,
            IQuestRewardManager questRewardManager,
            IDisableManager disableManager,
            Func<DateTime> utcNow = null,
            IItemManager itemManager = null,
            IPrerequisiteManager prerequisiteManager = null,
            IGameTableManager gameTableManager = null,
            Func<IQuestInfo, IQuest> questFactory = null)
        {
            player                   = owner;
            this.globalQuestManager  = globalQuestManager;
            this.questRewardManager  = questRewardManager;
            this.disableManager      = disableManager;
            this.utcNow              = utcNow ?? (() => DateTime.UtcNow);
            this.itemManager         = itemManager;
            this.prerequisiteManager = prerequisiteManager;
            this.gameTableManager    = gameTableManager;
            this.questFactory        = questFactory;

            foreach (CharacterQuestModel questModel in model.Quest)
            {
                IQuestInfo info = GetGlobalQuestManager().GetQuestInfo(questModel.QuestId);
                if (info == null)
                {
                    log.Error($"Player {player.CharacterId} has an invalid quest {questModel.QuestId}!");
                    continue;
                }

                var quest = new Quest.Quest(player, info, questModel, GetGlobalQuestManager(), null, null);
                switch (quest.State)
                {
                    case QuestState.Completed:
                        completedQuests.Add(quest.Id, quest);
                        break;
                    case QuestState.Botched:
                    case QuestState.Ignored:
                    case QuestState.Mentioned:
                        inactiveQuests.Add(quest.Id, quest);
                        break;
                    case QuestState.Accepted:
                    case QuestState.Achieved:
                        activeQuests.Add(quest.Id, quest);
                        break;
                }
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref disposed, 1);
            Interlocked.Exchange(ref pendingQuestShare, null);

            foreach (IQuest quest in completedQuests.Values
                .Concat(inactiveQuests.Values)
                .Concat(activeQuests.Values))
                quest.Dispose();
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage quest changes and acknowledge them after the character database commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            ArgumentNullException.ThrowIfNull(commitScope);

            foreach (IQuest quest in completedQuests.Values
                .Concat(inactiveQuests.Values)
                .Concat(activeQuests.Values)
                .Distinct()
                .ToList())
                quest.Save(context, commitScope, () => RemoveDeletedQuest(quest));
        }

        private void RemoveDeletedQuest(IQuest quest)
        {
            bool removed = RemoveQuest(completedQuests, quest);
            removed |= RemoveQuest(inactiveQuests, quest);
            removed |= RemoveQuest(activeQuests, quest);

            if (removed)
                quest.Dispose();
        }

        private static bool RemoveQuest(Dictionary<ushort, IQuest> quests, IQuest quest)
        {
            if (!quests.TryGetValue(quest.Id, out IQuest current) || !ReferenceEquals(current, quest))
                return false;

            return quests.Remove(quest.Id);
        }

        public void Update(double lastTick)
        {
            if (double.IsFinite(lastTick) && lastTick > 0d)
            {
                double updatedElapsedTime = elapsedTime + lastTick;
                elapsedTime = double.IsFinite(updatedElapsedTime) ? updatedElapsedTime : double.MaxValue;

                PendingQuestShare pending = Volatile.Read(ref pendingQuestShare);
                if (pending != null && elapsedTime >= pending.ExpiresAt)
                    Interlocked.CompareExchange(ref pendingQuestShare, null, pending);
            }

            KeyValuePair<ushort, IQuest>[] activeQuestSnapshot;
            try
            {
                activeQuestSnapshot = activeQuests.ToArray();
            }
            catch (Exception exception)
            {
                log.Error(exception, "Failed to snapshot active quests while advancing quest timers.");
                return;
            }

            var botchedQuests = new List<KeyValuePair<ushort, IQuest>>();
            foreach (KeyValuePair<ushort, IQuest> entry in activeQuestSnapshot)
            {
                try
                {
                    if (!activeQuests.TryGetValue(entry.Key, out IQuest activeQuest)
                        || !ReferenceEquals(activeQuest, entry.Value))
                        continue;

                    activeQuest.Update(lastTick);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to update active quest {entry.Key}.");
                }

                try
                {
                    if (activeQuests.TryGetValue(entry.Key, out IQuest activeQuest)
                        && ReferenceEquals(activeQuest, entry.Value)
                        && activeQuest.State == QuestState.Botched)
                        botchedQuests.Add(entry);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to inspect active quest {entry.Key} after its update.");
                }
            }

            foreach (KeyValuePair<ushort, IQuest> entry in botchedQuests)
            {
                try
                {
                    if (!activeQuests.TryGetValue(entry.Key, out IQuest activeQuest)
                        || !ReferenceEquals(activeQuest, entry.Value)
                        || activeQuest.State != QuestState.Botched)
                        continue;
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to revalidate botched quest {entry.Key}.");
                    continue;
                }

                if (!inactiveQuests.TryAdd(entry.Key, entry.Value))
                {
                    log.Error(
                        $"Failed to migrate botched quest {entry.Key} because an inactive quest with the same id is already tracked.");
                    continue;
                }

                if (!activeQuests.TryGetValue(entry.Key, out IQuest currentQuest)
                    || !ReferenceEquals(currentQuest, entry.Value))
                {
                    if (inactiveQuests.TryGetValue(entry.Key, out IQuest inactiveQuest)
                        && ReferenceEquals(inactiveQuest, entry.Value))
                        inactiveQuests.Remove(entry.Key);

                    continue;
                }

                activeQuests.Remove(entry.Key);

                log.Trace($"Failed to complete quest {entry.Key} before the timer expired!");
            }
        }

        public void SendInitialPackets()
        {
            DateTime now = GetUtcNow();
            player.Session.EnqueueMessageEncrypted(new ServerQuestInit
            {
                Completed = completedQuests.Values
                    .Select(q => new ServerQuestInit.QuestComplete
                    {
                        QuestId        = q.Id,
                        CompletedToday = q.Reset is DateTime reset
                            && now < QuestResetCalculator.AsUtc(reset)
                    }).ToList(),
                Inactive = inactiveQuests.Values
                    .Select(q => new ServerQuestInit.QuestInactive
                    {
                        QuestId = q.Id,
                        State   = q.State
                    }).ToList(),
                Active = activeQuests.Values
                    .Select(q => new ServerQuestInit.QuestActive
                    {
                        QuestId    = q.Id,
                        State      = q.State,
                        Flags      = q.Flags,
                        QuestTimeRemaining = q.Timer ?? 0u,
                        Objectives = q
                            .OrderBy(objective => objective.Index)
                            .Select(objective => new ServerQuestInit.QuestActive.Objective
                            {
                                Progress      = objective.Progress,
                                TimeRemaining = objective.Timer ?? 0u
                            }).ToList()
                    }).ToList()
            });
        }

        /// <summary>
        /// Return <see cref="QuestState"/> for supplied quest.
        /// </summary>
        public QuestState? GetQuestState<T>(T questId) where T : Enum
        {
            return GetQuestState(questId.As<T, ushort>());
        }

        /// <summary>
        /// Return <see cref="QuestState"/> for supplied quest.
        /// </summary>
        public QuestState? GetQuestState(ushort questId)
        {
            return GetQuest(questId)?.State;
        }

        private IQuest GetQuest(ushort questId, GetQuestFlags flags = GetQuestFlags.All)
        {
            if ((flags & GetQuestFlags.Active) != 0
                && activeQuests.TryGetValue(questId, out IQuest quest))
                return quest;

            if ((flags & GetQuestFlags.Inactive) != 0
                && inactiveQuests.TryGetValue(questId, out quest))
                return quest;

            if ((flags & GetQuestFlags.Completed) != 0
                && completedQuests.TryGetValue(questId, out quest))
                return quest;

            return null;
        }

        /// <summary>
        /// Mention a quest from supplied quest id, skipping any prerequisites checks.
        /// </summary>
        public void QuestMention(ushort questId)
        {
            IQuestInfo info = GetGlobalQuestManager().GetQuestInfo(questId);
            if (info == null)
                throw new ArgumentException($"Invalid quest {questId}!");

            if (GetDisableManager().IsDisabled(DisableType.Quest, questId))
            {
                player.SendSystemMessage($"Unable to add quest {questId} because it is disabled.");
                return;
            }

            if (GetQuest(questId) != null)
                return;

            QuestMention(info);
        }

        /// <summary>
        /// Mention a quest from supplied <see cref="IQuestInfo"/>, skipping any prerequisites checks.
        /// </summary>
        public void QuestMention(IQuestInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);
            if (info.Entry == null || info.Entry.Id == 0u || info.Entry.Id > CommunicatorMessage.MaximumId)
                throw new ArgumentException("Quest information has an invalid id.", nameof(info));

            ushort questId = (ushort)info.Entry.Id;
            if (GetQuest(questId) != null)
                return;

            var quest = new Quest.Quest(player, info, GetGlobalQuestManager(), null, null);

            quest.State = QuestState.Mentioned;
            inactiveQuests.Add(questId, quest);

            log.Trace($"Mentioned new quest {info.Entry.Id}.");
        }

        /// <summary>
        /// Add a quest from supplied id, optionally supplying <see cref="IItem"/> which was used to start the quest.
        /// </summary>
        public void QuestAdd(ushort questId, IItem item)
        {
            IQuestInfo info = GetGlobalQuestManager().GetQuestInfo(questId);
            if (info == null)
                throw new ArgumentException($"Invalid quest {questId}!");

            if (GetDisableManager().IsDisabled(DisableType.Quest, questId))
            {
                player.SendSystemMessage($"Unable to add quest {questId} because it is disabled.");
                return;
            }

            IQuest quest = GetQuest(questId);
            QuestAdd(info, quest, item);
        }

        private void QuestAdd(IQuestInfo info, IQuest quest, IItem item)
        {
            if (quest?.State is QuestState.Accepted or QuestState.Achieved)
                throw new QuestException($"Player {player.CharacterId} tried to start quest {info.Entry.Id} which is already in progress!");

            // if quest has already been completed make sure it's repeatable and the reset period has elapsed
            if (quest?.State == QuestState.Completed)
            {
                QuestRepeatPeriod repeatPeriod = (QuestRepeatPeriod)info.Entry.QuestRepeatPeriodEnum;
                if (repeatPeriod == QuestRepeatPeriod.None)
                    throw new QuestException($"Player {player.CharacterId} tried to start quest {info.Entry.Id} which they have already completed!");

                if (!QuestResetCalculator.IsSupported(repeatPeriod))
                {
                    log.Error($"Quest {info.Entry.Id} has unsupported repeat period {info.Entry.QuestRepeatPeriodEnum}.");
                    throw new QuestException($"Player {player.CharacterId} tried to start quest {info.Entry.Id} with invalid repeat metadata!");
                }

                DateTime? persistedReset = GetQuest((ushort)info.Entry.Id, GetQuestFlags.Completed).Reset;
                if (persistedReset == null)
                {
                    log.Error($"Completed repeatable quest {info.Entry.Id} has no persisted reset time.");
                    throw new QuestException($"Player {player.CharacterId} tried to start quest {info.Entry.Id} with missing reset metadata!");
                }

                DateTime resetTime = QuestResetCalculator.AsUtc(persistedReset.Value);
                if (GetUtcNow() < resetTime)
                    throw new QuestException($"Player {player.CharacterId} tried to start quest {info.Entry.Id} which hasn't reset yet!");
            }

            if (item != null)
            {
                if (info.Entry.Id != item.Info.Entry.Quest2IdActivation)
                    throw new QuestException($"Player {player.CharacterId} tried to start quest {info.Entry.Id} from invalid item {item.Info.Entry.Id}!");

                // TODO: consume charge
            }
            else
            {
                // make sure the player is in range of a quest giver or they are eligible for a communicator message that starts the quest
                IGlobalQuestManager manager = GetGlobalQuestManager();
                ushort questId = (ushort)info.Entry.Id;
                bool hasVisibleQuestGiver = (manager.GetQuestGivers(questId) ?? Enumerable.Empty<uint>())
                    .Any(creatureId => player.GetVisibleCreature<WorldEntity>(creatureId).Any());
                bool canStartRemotely = info.CanBeCalledBack()
                    || (info.IsCommunicatorReceived() && HasEligibleCommunicatorMessage(manager, questId));
                if (!hasVisibleQuestGiver && !canStartRemotely)
                    throw new QuestException($"Player {player.CharacterId} tried to start quest {info.Entry.Id} without quest giver!");
            }

            // server doesn't send an error message for prerequisites since the client does the exact same checks
            // it's assumed that a player could never get here without cheating in some way
            if (!MeetsPrerequisites(info))
                throw new QuestException($"Player {player.CharacterId} tried to start quest {info.Entry.Id} without meeting the prerequisites!");

            QuestAdd(info);
        }

        private bool MeetsPrerequisites(IQuestInfo info)
        {
            if (info.Entry.QuestPlayerFactionEnum == 0u && player.Faction1 != Faction.Exile)
                return false;
            if (info.Entry.QuestPlayerFactionEnum == 1u && player.Faction1 != Faction.Dominion)
                return false;
            if (info.Entry.PrerequisiteRace != 0u && player.Race != (Race)info.Entry.PrerequisiteRace)
                return false;
            if (info.Entry.PrerequisiteClass != 0u && player.Class != (Class)info.Entry.PrerequisiteClass)
                return false;
            if (player.Level < info.Entry.PrerequisiteLevel)
                return false;

            if (!info.PrerequisiteQuests.IsEmpty)
            {
                bool preReqQuestsCompleted;
                if ((info.Entry.PrerequisiteFlags & 1) != 0u)
                    preReqQuestsCompleted = info.PrerequisiteQuests.Any(q => GetQuestState((ushort)q.Id) == QuestState.Completed);
                else
                    preReqQuestsCompleted = info.PrerequisiteQuests.All(q => GetQuestState((ushort)q.Id) == QuestState.Completed);

                if (!preReqQuestsCompleted)
                    return false;
            }

            if (info.Entry.PrerequisiteId != 0u
                && !GetPrerequisiteManager().Meets(player, info.Entry.PrerequisiteId))
                return false;

            if (!info.IsContract())
            {
                GameFormulaEntry entry = GetGameTableManager().GameFormula?.GetEntry(655);
                // client also hard codes 40 if entry doesn't exist
                if (!HasActiveQuestCapacity(entry?.Dataint0 ?? 40u))
                    return false;
            }
            else
            {
                // TODO: contracts use reward property for max slots, RewardProperty.ActiveContractSlots
            }

            return true;
        }

        /// <summary>
        /// Add a quest from supplied <see cref="IQuestInfo"/>, skipping any prerequisites checks.
        /// </summary>
        public void QuestAdd(IQuestInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);
            PrepareAndActivateQuest(info, false);
        }

        private void PrepareAndActivateQuest(IQuestInfo info, bool shared)
        {
            IQuest quest;
            bool created;
            ushort questId;
            try
            {
                questId = (ushort)info.Entry.Id;
                quest = GetQuest(questId);
                created = quest == null;
                quest ??= CreateQuest(info);
            }
            catch (Exception exception)
            {
                string qualifier = shared ? "shared " : string.Empty;
                log.Error(exception, $"Failed to prepare {qualifier}quest {info.Entry?.Id ?? 0u} for activation.");
                return;
            }

            try
            {
                QuestState initialState;
                try
                {
                    initialState = quest.Any() ? QuestState.Accepted : QuestState.Achieved;
                }
                catch (Exception exception)
                {
                    string qualifier = shared ? "shared " : string.Empty;
                    log.Error(exception, $"Failed to inspect {qualifier}quest {info.Entry.Id} before activation.");
                    return;
                }

                if (!TryGrantPushedItems(info))
                    return;

                ActivateQuest(info, quest, initialState);
            }
            finally
            {
                if (created
                    && (!activeQuests.TryGetValue(questId, out IQuest activeQuest)
                        || !ReferenceEquals(activeQuest, quest)))
                    DisposeUntrackedQuest(quest, questId);
            }
        }

        private void DisposeUntrackedQuest(IQuest quest, ushort questId)
        {
            try
            {
                quest.Dispose();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to dispose untracked quest {questId}.");
            }
        }

        private void ActivateQuest(IQuestInfo info, IQuest quest, QuestState initialState)
        {
            ushort questId = (ushort)info.Entry.Id;

            if (GetQuest(questId) != null)
                QuestRemove(quest);

            quest.Flags |= QuestStateFlags.Tracked;
            quest.InitialiseTimer();
            activeQuests.Add(questId, quest);

            try
            {
                // Commit dictionary membership before the client notification. A failed notification must not leave
                // admitted pushed items without their quest; the next quest-log synchronisation repairs client state.
                quest.State = initialState;
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to notify player {player.CharacterId} about accepted quest {questId}.");
            }

            log.Trace($"Accepted new quest {info.Entry.Id}.");
        }

        private bool TryGrantPushedItems(IQuestInfo info)
        {
            if (!TryBuildPushedItemAdditions(info, out IReadOnlyCollection<KeyValuePair<IItemInfo, uint>> additions))
            {
                log.Error($"Quest {info.Entry?.Id ?? 0u} has invalid pushed item metadata.");
                return false;
            }

            if (additions.Count == 0)
                return true;

            try
            {
                if (player.Inventory?.TryAdmitItemExchange(
                    [],
                    additions,
                    ItemUpdateReason.Quest) == true)
                    return true;
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to grant pushed items for quest {info.Entry.Id}.");
                return false;
            }

            try
            {
                player.SendGenericError(GenericError.ItemInventoryFull);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to report insufficient inventory space for quest {info.Entry.Id}.");
            }

            return false;
        }

        private bool TryBuildPushedItemAdditions(
            IQuestInfo info,
            out IReadOnlyCollection<KeyValuePair<IItemInfo, uint>> additions)
        {
            additions = Array.Empty<KeyValuePair<IItemInfo, uint>>();
            uint[] itemIds = info?.Entry?.PushedItemIds;
            uint[] itemCounts = info?.Entry?.PushedItemCounts;
            if (itemIds == null || itemCounts == null || itemIds.Length != itemCounts.Length)
                return false;

            var itemAmounts = new Dictionary<uint, uint>();
            for (int i = 0; i < itemIds.Length; i++)
            {
                uint itemId = itemIds[i];
                uint itemCount = itemCounts[i];
                if ((itemId == 0u) != (itemCount == 0u))
                    return false;
                if (itemId == 0u)
                    continue;

                itemAmounts.TryGetValue(itemId, out uint current);
                ulong updated = (ulong)current + itemCount;
                if (updated > uint.MaxValue)
                    return false;

                itemAmounts[itemId] = (uint)updated;
            }

            var resolved = new List<KeyValuePair<IItemInfo, uint>>(itemAmounts.Count);
            try
            {
                foreach ((uint itemId, uint amount) in itemAmounts.OrderBy(pair => pair.Key))
                {
                    IItemInfo itemInfo = GetItemManager().GetItemInfo(itemId);
                    if (itemInfo == null)
                        return false;

                    resolved.Add(new KeyValuePair<IItemInfo, uint>(itemInfo, amount));
                }
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to resolve pushed items for quest {info.Entry.Id}.");
                return false;
            }

            additions = resolved;
            return true;
        }

        internal bool HasActiveQuestCapacity(uint maximum)
        {
            return activeQuests.Count < maximum;
        }

        private void QuestRemove(IQuest quest)
        {
            // remove existing quest from its current home before
            switch (quest.State)
            {
                case QuestState.Abandoned:
                    activeQuests.Remove(quest.Id);
                    break;
                case QuestState.Completed:
                    completedQuests.Remove(quest.Id);
                    break;
                case QuestState.Botched:
                case QuestState.Ignored:
                case QuestState.Mentioned:
                    inactiveQuests.Remove(quest.Id);
                    break;
            }

            if (quest.PendingDelete)
                quest.EnqueueDelete(false);

            // reset previous objective progress
            foreach (IQuestObjective objective in quest)
                objective.Progress = 0u;
        }

        /// <summary>
        /// Retry an inactive quest id that was previously failed.
        /// </summary>
        public void QuestRetry(ushort questId)
        {
            IQuestInfo info = GlobalQuestManager.Instance.GetQuestInfo(questId);
            if (info == null)
                throw new ArgumentException($"Invalid quest {questId}!");

            IQuest quest = GetQuest(questId, GetQuestFlags.Inactive);
            if (quest == null)
                throw new QuestException($"Player {player.CharacterId} tried to restart quest {questId} which they don't have!");

            if (quest.State != QuestState.Botched)
                throw new QuestException($"Player {player.CharacterId} tried to restart quest {questId} which hasn't been failed!");

            QuestAdd(info, quest, null);
        }

        /// <summary>
        /// Abandon an active quest.
        /// </summary>
        public void QuestAbandon(ushort questId)
        {
            if (GlobalQuestManager.Instance.GetQuestInfo(questId) == null)
                throw new ArgumentException($"Invalid quest {questId}!");

            IQuest quest = GetQuest(questId, GetQuestFlags.Active | GetQuestFlags.Inactive);
            if (quest == null || quest.PendingDelete)
                throw new QuestException($"Player {player.CharacterId} tried to abandon quest {questId} which they don't have!");

            if (!quest.CanAbandon())
                throw new QuestException($"Player {player.CharacterId} tried to abandon quest {questId} which can't be abandoned!");

            // don't delete quests that have been mentioned, they may not be able to be re-collected.
            if (quest.CanDelete())
                quest.EnqueueDelete(true);
            else
            {
                switch (quest.State)
                {
                    case QuestState.Accepted:
                    case QuestState.Achieved:
                        activeQuests.Remove(questId);
                        break;
                    case QuestState.Botched:
                        inactiveQuests.Remove(quest.Id);
                        break;
                }
            }

            foreach (IQuestObjective objective in quest)
                objective.Progress = 0u;

            if (quest.Info.IsQuestMentioned)
            {
                quest.State = QuestState.Mentioned;
                inactiveQuests.Add(quest.Id, quest);
            }
            else
                quest.State = QuestState.Abandoned;

            log.Trace($"Abandoned quest {questId}.");
        }

        /// <summary>
        /// Complete all <see cref="IQuestObjective"/>'s for supplied active quest id.
        /// </summary>
        public void QuestAchieve(ushort questId)
        {
            if (GlobalQuestManager.Instance.GetQuestInfo(questId) == null)
                throw new ArgumentException($"Invalid quest {questId}!");

            IQuest quest = GetQuest(questId);
            if (quest == null || quest.PendingDelete)
                throw new QuestException($"Player {player.CharacterId} tried to achieve quest {questId} which they don't have!");

            if (quest.State != QuestState.Accepted)
                throw new QuestException($"Player {player.CharacterId} tried to achieve quest {questId} with invalid state!");

            quest.ObjectivesComplete();
        }

        /// <summary>
        /// Complete single <see cref="IQuestObjective"/> for supplied active quest id.
        /// </summary>
        public void QuestAchieveObjective(ushort questId, byte index)
        {
            if (GlobalQuestManager.Instance.GetQuestInfo(questId) == null)
                throw new ArgumentException($"Invalid quest {questId}!");

            IQuest quest = GetQuest(questId);
            if (quest == null || quest.PendingDelete)
                throw new QuestException();

            if (quest.State != QuestState.Accepted)
                throw new QuestException();

            IQuestObjective objective = quest.SingleOrDefault(o => o.Index == index);
            if (objective == null)
                throw new QuestException();

            quest.ObjectiveComplete(objective.ObjectiveInfo.Id);
        }

        /// <summary>
        /// Attempts to apply raw progress to an exact objective slot on an active quest.
        /// </summary>
        public bool TryObjectiveUpdate(
            ushort questId,
            byte objectiveIndex,
            uint progress,
            out IQuestObjective objective)
        {
            objective = null;
            if (progress == 0u)
                return false;

            IQuest quest = GetQuest(questId, GetQuestFlags.Active);
            if (quest == null || quest.PendingDelete || quest.State != QuestState.Accepted)
                return false;

            return quest.TryObjectiveUpdate(objectiveIndex, progress, out objective);
        }

        /// <summary>
        /// Complete an achieved quest supplying an optional reward and whether the quest was completed from the communicator.
        /// </summary>
        public void QuestComplete(ushort questId, ushort reward, bool communicator)
        {
            IGlobalQuestManager globalQuestManager = GetGlobalQuestManager();
            IQuestInfo questInfo = globalQuestManager.GetQuestInfo(questId);
            if (questInfo == null)
                throw new ArgumentException($"Invalid quest {questId}!");

            if (GetDisableManager().IsDisabled(DisableType.Quest, questId))
            {
                player.SendSystemMessage($"Unable to complete quest {questId} because it is disabled.");
                return;
            }

            DateTime? completionReset = CalculateCompletionReset(questInfo);

            IQuest quest = GetQuest(questId, GetQuestFlags.Active);
            if (quest == null)
            {
                if (!questInfo.IsAutoComplete())
                    throw new QuestException($"Player {player.CharacterId} tried to complete quest {questId} which they don't have!");

                QuestAdd(questId, null);
                quest = GetQuest(questId);
                quest.State = QuestState.Achieved;
            }

            if (quest.State != QuestState.Achieved)
                throw new QuestException($"Player {player.CharacterId} tried to complete quest {questId} which wasn't complete!");

            if (completingQuests.Contains(questId))
                throw new QuestException($"Player {player.CharacterId} tried to complete quest {questId} while completion was already in progress!");

            if (communicator)
            {
                // TODO: check if this is complete, client seems to also refer to contact info
                // for more see QuestTracker:HelperShowQuestCallbackBtn in LUA which contains the logic to show the complete button in the quest tracker
                if (!quest.Info.IsCommunicatorReceived() && !quest.Info.CanBeCalledBack())
                    throw new QuestException($"Player {player.CharacterId} tried to complete quest {questId} without communicator message!");
            }
            else
            {
                if (!globalQuestManager.GetQuestReceivers(questId).Any(c => player.GetVisibleCreature<WorldEntity>(c).Any()))
                    throw new QuestException($"Player {player.CharacterId} tried to complete quest {questId} without any quest receiver!");
            }

            if (!completingQuests.Add(questId))
                throw new QuestException($"Player {player.CharacterId} tried to complete quest {questId} while completion was already in progress!");
            bool releaseCompletionGuard = false;
            try
            {
                IQuestRewardManager questRewardManager = GetQuestRewardManager();
                if (!questRewardManager.TryCreatePlan(quest.Info, reward, out QuestRewardPlan rewardPlan))
                {
                    releaseCompletionGuard = true;
                    return;
                }

                if (!questRewardManager.TryApply(rewardPlan))
                {
                    releaseCompletionGuard = true;
                    return;
                }

                quest.State = QuestState.Completed;

                if (completionReset != null)
                    quest.Reset = completionReset;

                activeQuests.Remove(questId);
                completedQuests.Add(questId, quest);

                // Final quest completion is the only event mapped here. Objective type 24 metadata is ambiguous
                // about objective-level completion, so do not emit it from intermediate objective transitions.
                releaseCompletionGuard = true;
                NotifyFinalQuestCompletion(questId);
                player.AchievementManager.CheckAchievements(player, AchievementType.QuestComplete, questId);
            }
            finally
            {
                if (releaseCompletionGuard)
                    completingQuests.Remove(questId);
            }
        }

        /// <summary>
        /// Best-effort notification that a quest reached final completion.
        /// </summary>
        /// <remarks>
        /// Snapshot identities are revalidated before dispatch because an earlier observer can remove or replace a
        /// later active quest. The completed source has already left <see cref="activeQuests"/> before this runs.
        /// </remarks>
        private void NotifyFinalQuestCompletion(ushort sourceQuestId)
        {
            KeyValuePair<ushort, IQuest>[] activeQuestSnapshot;
            try
            {
                activeQuestSnapshot = activeQuests.ToArray();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to snapshot active quests after completing quest {sourceQuestId}.");
                return;
            }

            foreach (KeyValuePair<ushort, IQuest> observerEntry in activeQuestSnapshot)
            {
                try
                {
                    if (!activeQuests.TryGetValue(observerEntry.Key, out IQuest activeQuest)
                        || !ReferenceEquals(activeQuest, observerEntry.Value))
                        continue;

                    activeQuest.ObjectiveUpdate(QuestObjectiveType.CompleteQuest, sourceQuestId, 1u);
                }
                catch (Exception exception)
                {
                    log.Error(
                        exception,
                        $"Failed to update quest {observerEntry.Key} after completing quest {sourceQuestId}.");
                }
            }
        }

        private IGlobalQuestManager GetGlobalQuestManager()
        {
            return globalQuestManager ?? GlobalQuestManager.Instance;
        }

        private IQuestRewardManager GetQuestRewardManager()
        {
            return questRewardManager ??= new QuestRewardManager(
                player,
                ItemManager.Instance,
                GameTableManager.Instance,
                FactionManager.Instance,
                PrerequisiteManager.Instance);
        }

        private IDisableManager GetDisableManager()
        {
            return disableManager ?? DisableManager.Instance;
        }

        private IItemManager GetItemManager()
        {
            return itemManager ?? ItemManager.Instance;
        }

        private IQuest CreateQuest(IQuestInfo info)
        {
            return questFactory?.Invoke(info)
                ?? new Quest.Quest(player, info, GetGlobalQuestManager(), null, null);
        }

        private IPrerequisiteManager GetPrerequisiteManager()
        {
            return prerequisiteManager ?? PrerequisiteManager.Instance;
        }

        private IGameTableManager GetGameTableManager()
        {
            return gameTableManager ?? GameTableManager.Instance;
        }

        private DateTime GetUtcNow()
        {
            return QuestResetCalculator.AsUtc(utcNow());
        }

        private DateTime? CalculateCompletionReset(IQuestInfo questInfo)
        {
            QuestRepeatPeriod repeatPeriod = (QuestRepeatPeriod)questInfo.Entry.QuestRepeatPeriodEnum;
            if (repeatPeriod == QuestRepeatPeriod.None)
                return null;

            if (!QuestResetCalculator.TryCalculateNext(repeatPeriod, GetUtcNow(), out DateTime resetTime))
            {
                log.Error($"Quest {questInfo.Entry.Id} has unsupported repeat period {questInfo.Entry.QuestRepeatPeriodEnum}.");
                throw new QuestException($"Quest {questInfo.Entry.Id} has invalid repeat metadata!");
            }

            return resetTime;
        }

        private bool HasEligibleCommunicatorMessage(IGlobalQuestManager manager, ushort questId)
        {
            IEnumerable<ICommunicatorMessage> messages = manager.GetQuestCommunicatorMessages(questId)
                ?? Enumerable.Empty<ICommunicatorMessage>();
            foreach (ICommunicatorMessage message in messages)
            {
                try
                {
                    if (message?.Meets(player) == true)
                        return true;
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to evaluate a communicator message for quest {questId}.");
                }
            }

            return false;
        }

        /// <summary>
        /// Ignore or acknowledge an inactive quest.
        /// </summary>
        public void QuestIgnore(ushort questId, bool ignored)
        {
            IQuestInfo questInfo = GlobalQuestManager.Instance.GetQuestInfo(questId);
            if (questInfo == null)
                throw new ArgumentException($"Invalid quest {questId}!");

            IQuest quest = GetQuest((ushort)questInfo.Entry.Id);
            if (quest == null)
                quest = new Quest.Quest(player, questInfo, GetGlobalQuestManager(), null, null); // Add quest so we can set it to ignored.
            else
                QuestRemove(quest); // Removes from quest log. Might not be the cleanest way to do this?

            quest.State = ignored ? QuestState.Ignored : QuestState.Mentioned;

            inactiveQuests.Add(questId, quest);
        }

        /// <summary>
        /// Track or hide an active quest.
        /// </summary>
        public void QuestTrack(ushort questId, bool tracked)
        {
            if (GlobalQuestManager.Instance.GetQuestInfo(questId) == null)
                throw new ArgumentException($"Invalid quest {questId}!");

            IQuest quest = GetQuest(questId, GetQuestFlags.Active);
            if (quest == null)
                throw new QuestException($"Player {player.CharacterId} tried to track quest {questId} which they don't have!");

            if (quest.State != QuestState.Accepted && quest.State != QuestState.Achieved)
                throw new QuestException($"Player {player.CharacterId} tried to track quest {questId} with invalid state!");

            if (tracked)
                quest.Flags |= QuestStateFlags.Tracked;
            else
                quest.Flags &= ~QuestStateFlags.Tracked;

            log.Trace($"Updated tracked state of quest {questId} to {tracked}.");
        }

        /// <summary>
        /// Share supplied quest with another <see cref="IPlayer"/>.
        /// </summary>
        public void QuestShare(ushort questId)
        {
            IQuestInfo info = GetGlobalQuestManager().GetQuestInfo(questId);
            if (info == null)
                throw new ArgumentException($"Invalid quest {questId}!");

            if (!CanShareQuest(questId))
                throw new QuestException($"Player {player.CharacterId} tried to share quest {questId} which can't be shared!");

            if (player.TargetGuid is not uint targetGuid || targetGuid == 0u || targetGuid == player.Guid)
                throw new QuestException($"Player {player.CharacterId} tried to share quest {questId} without a target!");

            IPlayer recipient;
            try
            {
                recipient = player.GetVisible<IPlayer>(targetGuid);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to resolve the recipient for quest share {questId}.");
                return;
            }

            if (!IsValidSharePair(player, recipient))
                throw new QuestException($"Player {player.CharacterId} tried to share quest {questId} to an invalid player!");

            try
            {
                IQuestManager recipientQuestManager = recipient.QuestManager;
                if (recipientQuestManager == null
                    || !recipientQuestManager.OfferQuestShare(
                        questId,
                        player.Guid,
                        player.CharacterId,
                        player.GroupAssociation))
                    return;
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to offer quest share {questId} to player {recipient.CharacterId}.");
                return;
            }

            log.Trace($"Shared quest {questId} with player {recipient.Name}.");
        }

        /// <summary>
        /// Returns whether the owner currently has the supplied quest in a shareable state.
        /// </summary>
        public bool CanShareQuest(ushort questId)
        {
            if (Volatile.Read(ref disposed) != 0
                || questId == 0u
                || questId > CommunicatorMessage.MaximumId)
                return false;

            try
            {
                IQuestInfo info = GetGlobalQuestManager().GetQuestInfo(questId);
                if (info?.Entry == null
                    || info.Entry.Id != questId
                    || info.Entry.QuestShareEnum == 0u
                    || GetDisableManager().IsDisabled(DisableType.Quest, questId))
                    return false;

                IQuest quest = GetQuest(questId);
                return quest != null
                    && !quest.PendingDelete
                    && quest.CanShare();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to validate quest share {questId} for player {player.CharacterId}.");
                return false;
            }
        }

        /// <summary>
        /// Offer a shared quest to the owner after validating the supplied sharer identity and group.
        /// </summary>
        public bool OfferQuestShare(
            ushort questId,
            uint sharerGuid,
            ulong sharerCharacterId,
            ulong groupAssociation)
        {
            if (Volatile.Read(ref disposed) != 0
                || questId == 0u
                || questId > CommunicatorMessage.MaximumId
                || sharerGuid == 0u
                || sharerCharacterId == 0ul
                || sharerCharacterId == player.CharacterId
                || groupAssociation == 0ul
                || player.Guid == 0u
                || player.CharacterId == 0ul
                || !player.InWorld
                || player.Map == null
                || player.GroupAssociation != groupAssociation)
                return false;

            if (!TryResolveSharer(
                sharerGuid,
                sharerCharacterId,
                groupAssociation,
                out IPlayer sharer))
                return false;

            try
            {
                if (sharer.QuestManager?.CanShareQuest(questId) != true)
                    return false;
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to validate sharer {sharerCharacterId} for quest {questId}.");
                return false;
            }

            double expiresAt = elapsedTime + QuestShareDuration;
            if (!double.IsFinite(expiresAt))
                expiresAt = double.MaxValue;

            var pending = new PendingQuestShare(
                questId,
                sharerGuid,
                sharerCharacterId,
                groupAssociation,
                expiresAt);

            try
            {
                IGameSession session = player.Session;
                if (session == null)
                    return false;

                if (!session.TryEnqueueMessageEncrypted(new ServerQuestShared
                {
                    SharerUnitId = sharerGuid,
                    QuestId     = questId
                }))
                    return false;
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to send quest share {questId} to player {player.CharacterId}.");
                return false;
            }

            Interlocked.Exchange(ref pendingQuestShare, pending);
            if (Volatile.Read(ref disposed) != 0)
            {
                Interlocked.CompareExchange(ref pendingQuestShare, null, pending);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Accept or deny a shared quest from another <see cref="IPlayer"/>.
        /// </summary>
        public void QuestShareResult(ushort questId, bool result)
        {
            PendingQuestShare pending = Interlocked.Exchange(ref pendingQuestShare, null);
            if (pending == null
                || Volatile.Read(ref disposed) != 0
                || !result
                || questId == 0u
                || questId != pending.QuestId
                || elapsedTime >= pending.ExpiresAt)
                return;

            if (player.Guid == 0u
                || player.CharacterId == 0ul
                || !player.InWorld
                || player.Map == null
                || player.GroupAssociation == 0ul
                || player.GroupAssociation != pending.GroupAssociation)
                return;

            if (!TryResolveSharer(
                pending.SharerGuid,
                pending.SharerCharacterId,
                pending.GroupAssociation,
                out IPlayer sharer))
                return;

            try
            {
                if (sharer.QuestManager?.CanShareQuest(questId) != true)
                    return;
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to revalidate sharer {pending.SharerCharacterId} for quest {questId}.");
                return;
            }

            IQuestInfo info;
            try
            {
                info = GetGlobalQuestManager().GetQuestInfo(questId);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to resolve shared quest {questId}.");
                return;
            }

            if (!CanAcceptSharedQuest(info, questId))
                return;

            PrepareAndActivateQuest(info, true);
        }

        private bool CanAcceptSharedQuest(IQuestInfo info, ushort questId)
        {
            try
            {
                Quest2Entry entry = info?.Entry;
                if (entry == null
                    || entry.Id != questId
                    || entry.QuestShareEnum == 0u
                    || info.IsContract()
                    || entry.PrerequisiteItem != 0u
                    || entry.QuestIdExclusionPreq0 != 0u
                    || entry.QuestIdExclusionPreq1 != 0u
                    || entry.QuestIdExclusionPreq2 != 0u
                    || HasUnsupportedVirtualPushedItems(entry)
                    || GetDisableManager().IsDisabled(DisableType.Quest, questId))
                    return false;

                IQuest quest = GetQuest(questId);
                if (quest != null)
                {
                    if (quest.PendingDelete || quest.State != QuestState.Completed)
                        return false;

                    QuestRepeatPeriod repeatPeriod = (QuestRepeatPeriod)entry.QuestRepeatPeriodEnum;
                    if (repeatPeriod == QuestRepeatPeriod.None
                        || !QuestResetCalculator.IsSupported(repeatPeriod)
                        || quest.Reset == null
                        || GetUtcNow() < QuestResetCalculator.AsUtc(quest.Reset.Value))
                        return false;
                }

                return MeetsPrerequisites(info);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to validate shared quest {questId} for player {player.CharacterId}.");
                return false;
            }
        }

        private bool TryResolveSharer(
            uint sharerGuid,
            ulong sharerCharacterId,
            ulong groupAssociation,
            out IPlayer sharer)
        {
            sharer = null;
            if (sharerGuid == 0u
                || sharerCharacterId == 0ul
                || sharerCharacterId == player.CharacterId
                || groupAssociation == 0ul
                || player.Map == null)
                return false;

            try
            {
                sharer = player.GetVisible<IPlayer>(sharerGuid);
                return sharer != null
                    && sharer.Guid == sharerGuid
                    && sharer.CharacterId == sharerCharacterId
                    && sharer.Guid != player.Guid
                    && sharer.CharacterId != player.CharacterId
                    && sharer.InWorld
                    && ReferenceEquals(sharer.Map, player.Map)
                    && sharer.GroupAssociation == groupAssociation
                    && IsVisiblePlayer(sharer, player);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to resolve quest sharer {sharerCharacterId}.");
                sharer = null;
                return false;
            }
        }

        private static bool IsValidSharePair(IPlayer sharer, IPlayer recipient)
        {
            if (sharer == null
                || recipient == null
                || sharer.Guid == 0u
                || recipient.Guid == 0u
                || sharer.CharacterId == 0ul
                || recipient.CharacterId == 0ul
                || sharer.Guid == recipient.Guid
                || sharer.CharacterId == recipient.CharacterId
                || !sharer.InWorld
                || !recipient.InWorld
                || sharer.Map == null
                || !ReferenceEquals(sharer.Map, recipient.Map)
                || sharer.GroupAssociation == 0ul
                || sharer.GroupAssociation != recipient.GroupAssociation)
                return false;

            return IsVisiblePlayer(recipient, sharer);
        }

        private static bool IsVisiblePlayer(IPlayer observer, IPlayer expected)
        {
            IPlayer visible = observer.GetVisible<IPlayer>(expected.Guid);
            return visible != null
                && visible.Guid == expected.Guid
                && visible.CharacterId == expected.CharacterId;
        }

        private static bool HasUnsupportedVirtualPushedItems(Quest2Entry entry)
        {
            return entry.VirtualItemIdPushed00 != 0u
                || entry.VirtualItemIdPushed01 != 0u
                || entry.VirtualItemIdPushed02 != 0u
                || entry.VirtualItemIdPushed03 != 0u
                || entry.VirtualItemPushedCount00 != 0u
                || entry.VirtualItemPushedCount01 != 0u
                || entry.VirtualItemPushedCount02 != 0u
                || entry.VirtualItemPushedCount03 != 0u;
        }

        /// <summary>
        /// Update any active quest <see cref="IQuestObjective"/>'s with supplied <see cref="QuestObjectiveType"/> and data with progress.
        /// </summary>
        public void ObjectiveUpdate(QuestObjectiveType type, uint data, uint progress)
        {
            UpdateActiveQuests(
                quest => quest.ObjectiveUpdate(type, data, progress),
                $"objective type {type}, data {data}, progress {progress}");
        }

        /// <summary>
        /// Update matching active quest objectives except for exact static objective identifiers.
        /// </summary>
        public void ObjectiveUpdate(
            QuestObjectiveType type,
            uint data,
            uint progress,
            IReadOnlySet<uint> excludedObjectiveIds)
        {
            UpdateActiveQuests(
                quest => quest.ObjectiveUpdate(type, data, progress, excludedObjectiveIds),
                $"objective type {type}, data {data}, progress {progress} with exclusions");
        }

        /// <summary>
        /// Update any active quest <see cref="IQuestObjective"/>'s with supplied ID with progress.
        /// </summary>
        public void ObjectiveUpdate(uint id, uint progress)
        {
            UpdateActiveQuests(
                quest => quest.ObjectiveUpdate(id, progress),
                $"objective {id}, progress {progress}");
        }

        private void UpdateActiveQuests(Action<IQuest> update, string context)
        {
            KeyValuePair<ushort, IQuest>[] activeQuestSnapshot;
            try
            {
                activeQuestSnapshot = activeQuests.ToArray();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to snapshot active quests while updating {context}.");
                return;
            }

            foreach (KeyValuePair<ushort, IQuest> entry in activeQuestSnapshot)
            {
                try
                {
                    if (!activeQuests.TryGetValue(entry.Key, out IQuest activeQuest)
                        || !ReferenceEquals(activeQuest, entry.Value))
                        continue;

                    update(activeQuest);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to update active quest {entry.Key} for {context}.");
                }
            }
        }

        /// <summary>
        /// Returns a collection of all active quests.
        /// </summary>
        public IEnumerable<IQuest> GetActiveQuests()
        {
            return activeQuests.Values;
        }
    }
}
