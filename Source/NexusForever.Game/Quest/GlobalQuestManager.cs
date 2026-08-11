using System.Collections.Immutable;
using System.Diagnostics;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Static.Quest;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Shared;
using NLog;

namespace NexusForever.Game.Quest
{
    public sealed class GlobalQuestManager : Singleton<GlobalQuestManager>, IGlobalQuestManager
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// <see cref="DateTime"/> representing the next daily reset.
        /// </summary>
        public DateTime NextDailyReset { get; private set; }

        /// <summary>
        /// <see cref="DateTime"/> representing the next weekly reset.
        /// </summary>
        public DateTime NextWeeklyReset { get; private set; }

        private ImmutableDictionary<ushort, IQuestInfo> questInfoStore =
            ImmutableDictionary<ushort, IQuestInfo>.Empty;
        private ImmutableDictionary<ushort, ImmutableList<uint>> questGiverStore =
            ImmutableDictionary<ushort, ImmutableList<uint>>.Empty;
        private ImmutableDictionary<ushort, ImmutableList<uint>> questReceiverStore =
            ImmutableDictionary<ushort, ImmutableList<uint>>.Empty;

        private ImmutableDictionary<uint, ICommunicatorMessage> communicatorStore =
            ImmutableDictionary<uint, ICommunicatorMessage>.Empty;
        private ImmutableDictionary<ushort, ImmutableList<ICommunicatorMessage>> communicatorQuestStore =
            ImmutableDictionary<ushort, ImmutableList<ICommunicatorMessage>>.Empty;
        private ImmutableDictionary<(ushort /*questId*/, QuestState), ImmutableList<ICommunicatorMessage>> communicatorQuestStateTriggerStore =
            ImmutableDictionary<(ushort, QuestState), ImmutableList<ICommunicatorMessage>>.Empty;

        public void Initialise()
        {
            Stopwatch sw = Stopwatch.StartNew();

            CalculateResetTimes(DateTime.UtcNow);
            InitialiseQuestInfo();
            InitialiseQuestRelations();

            InitialiseCommunicators(
                GameTableManager.Instance.CommunicatorMessages.Entries,
                questInfoStore);

            log.Info($"Cached {questInfoStore.Count} quests in {sw.ElapsedMilliseconds}ms.");
        }

        internal void CalculateResetTimes(DateTime now)
        {
            QuestResetCalculator.TryCalculateNext(QuestRepeatPeriod.Daily, now, out DateTime dailyReset);
            QuestResetCalculator.TryCalculateNext(QuestRepeatPeriod.Weekly, now, out DateTime weeklyReset);
            NextDailyReset = dailyReset;
            NextWeeklyReset = weeklyReset;
        }

        private void InitialiseQuestInfo()
        {
            var builder = ImmutableDictionary.CreateBuilder<ushort, IQuestInfo>();
            foreach (Quest2Entry entry in GameTableManager.Instance.Quest2.Entries)
                builder.Add((ushort)entry.Id, new QuestInfo(entry));

            questInfoStore = builder.ToImmutable();
        }

        private void InitialiseQuestRelations()
        {
            var questGivers = new Dictionary<ushort, List<uint>>();
            var questReceivers = new Dictionary<ushort, List<uint>>();

            foreach (Creature2Entry entry in GameTableManager.Instance.Creature2.Entries)
            {
                // ReSharper disable once PossibleInvalidCastExceptionInForeachLoop
                foreach (ushort questId in entry.QuestIdGiven.Where(q => q != 0u))
                {
                    if (!questGivers.ContainsKey(questId))
                        questGivers.Add(questId, new List<uint>());

                    questGivers[questId].Add(entry.Id);
                }

                // ReSharper disable once PossibleInvalidCastExceptionInForeachLoop
                foreach (ushort questId in entry.QuestIdReceive.Where(q => q != 0u))
                {
                    if (!questReceivers.ContainsKey(questId))
                        questReceivers.Add(questId, new List<uint>());

                    questReceivers[questId].Add(entry.Id);
                }
            }

            questGiverStore = questGivers.ToImmutableDictionary(k => k.Key, v => v.Value.ToImmutableList());
            questReceiverStore = questReceivers.ToImmutableDictionary(k => k.Key, v => v.Value.ToImmutableList());
        }

        internal void InitialiseCommunicators(
            IEnumerable<CommunicatorMessagesEntry> entries,
            IReadOnlyDictionary<ushort, IQuestInfo> questInfos)
        {
            ArgumentNullException.ThrowIfNull(entries);
            ArgumentNullException.ThrowIfNull(questInfos);

            var communicatorBuilder = ImmutableDictionary.CreateBuilder<uint, ICommunicatorMessage>();
            var questBuilder = new Dictionary<ushort, List<ICommunicatorMessage>>();
            var triggerBuilder = new Dictionary<(ushort, QuestState), List<ICommunicatorMessage>>();

            foreach (IGrouping<uint, CommunicatorMessagesEntry> group in entries
                .Where(entry => entry != null)
                .GroupBy(entry => entry.Id)
                .OrderBy(group => group.Key))
            {
                if (group.Count() != 1)
                {
                    log.Warn($"Ignoring duplicate communicator message id {group.Key}.");
                    continue;
                }

                CommunicatorMessagesEntry entry = group.Single();
                if (!TryValidateCommunicatorEntry(entry, questInfos, out string reason))
                {
                    log.Warn($"Ignoring communicator message {entry.Id}: {reason}.");
                    continue;
                }

                var communicator = new CommunicatorMessage(entry);
                communicatorBuilder.Add(communicator.Id, communicator);

                if (communicator.QuestId != 0)
                    AddCommunicator(questBuilder, communicator.QuestId, communicator);

                for (int i = 0; i < entry.Quests.Length; i++)
                {
                    ushort questId = (ushort)entry.Quests[i];
                    if (questId == 0 || questId == communicator.QuestId)
                        continue;

                    AddCommunicator(triggerBuilder, (questId, (QuestState)entry.States[i]), communicator);
                }
            }

            communicatorStore = communicatorBuilder.ToImmutable();
            communicatorQuestStore = questBuilder.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value.OrderBy(message => message.Id).ToImmutableList());
            communicatorQuestStateTriggerStore = triggerBuilder.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value.OrderBy(message => message.Id).ToImmutableList());
        }

        private static bool TryValidateCommunicatorEntry(
            CommunicatorMessagesEntry entry,
            IReadOnlyDictionary<ushort, IQuestInfo> questInfos,
            out string reason)
        {
            if (entry.Id == 0u || entry.Id > CommunicatorMessage.MaximumId)
            {
                reason = "id does not fit the 15-bit packet field";
                return false;
            }

            if (!IsValidQuestReference(entry.QuestIdDelivered, questInfos))
            {
                reason = $"delivered quest {entry.QuestIdDelivered} is invalid";
                return false;
            }

            if (entry.Quests == null || entry.States == null || entry.Quests.Length != entry.States.Length)
            {
                reason = "quest trigger arrays are malformed";
                return false;
            }

            foreach (uint questId in entry.Quests)
            {
                if (!IsValidQuestReference(questId, questInfos))
                {
                    reason = $"trigger quest {questId} is invalid";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        private static bool IsValidQuestReference(
            uint questId,
            IReadOnlyDictionary<ushort, IQuestInfo> questInfos)
        {
            return questId == 0u
                || (questId <= CommunicatorMessage.MaximumId && questInfos.ContainsKey((ushort)questId));
        }

        private static void AddCommunicator<TKey>(
            IDictionary<TKey, List<ICommunicatorMessage>> store,
            TKey key,
            ICommunicatorMessage communicator)
        {
            if (!store.TryGetValue(key, out List<ICommunicatorMessage> messages))
            {
                messages = new List<ICommunicatorMessage>();
                store.Add(key, messages);
            }

            if (messages.All(message => message.Id != communicator.Id))
                messages.Add(communicator);
        }

        public void Update(double lastTick)
        {
            UpdateResetTimes(DateTime.UtcNow);
        }

        internal void UpdateResetTimes(DateTime now)
        {
            DateTime nowUtc = QuestResetCalculator.AsUtc(now);
            if (NextDailyReset <= nowUtc)
            {
                QuestResetCalculator.TryCalculateNext(
                    QuestRepeatPeriod.Daily,
                    nowUtc,
                    out DateTime nextDailyReset);
                NextDailyReset = nextDailyReset;
            }

            if (NextWeeklyReset <= nowUtc)
            {
                QuestResetCalculator.TryCalculateNext(
                    QuestRepeatPeriod.Weekly,
                    nowUtc,
                    out DateTime nextWeeklyReset);
                NextWeeklyReset = nextWeeklyReset;
            }
        }

        /// <summary>
        /// Return <see cref="IQuestInfo"/> for supplied quest.
        /// </summary>
        public IQuestInfo GetQuestInfo(ushort questId)
        {
            return questInfoStore.TryGetValue(questId, out IQuestInfo questInfo) ? questInfo : null;
        }

        /// <summary>
        /// Return a collection of creatures that start the supplied quest.
        /// </summary>
        public IEnumerable<uint> GetQuestGivers(ushort questId)
        {
            return questGiverStore.TryGetValue(questId, out ImmutableList<uint> creatureIds) ? creatureIds : Enumerable.Empty<uint>();
        }

        /// <summary>
        /// Return a collection of creatures that finish the supplied quest.
        /// </summary>
        public IEnumerable<uint> GetQuestReceivers(ushort questId)
        {
            return questReceiverStore.TryGetValue(questId, out ImmutableList<uint> creatureIds) ? creatureIds : Enumerable.Empty<uint>();
        }

        /// <summary>
        /// Return a collection of <see cref="ICommunicatorMessage"/>'s that start the supplied quest.
        /// </summary>
        public IEnumerable<ICommunicatorMessage> GetQuestCommunicatorMessages(ushort questId)
        {
            return communicatorQuestStore.TryGetValue(questId, out ImmutableList<ICommunicatorMessage> creatureIds)
                ? creatureIds : Enumerable.Empty<ICommunicatorMessage>();
        }

        /// <summary>
        /// Return a collection of <see cref="ICommunicatorMessage"/>'s that are triggered when a quest hits a certain state.
        /// </summary>
        public IEnumerable<ICommunicatorMessage> GetQuestCommunicatorQuestStateTriggers(ushort questId, QuestState state)
        {
            return communicatorQuestStateTriggerStore.TryGetValue((questId, state), out ImmutableList<ICommunicatorMessage> triggers)
                ? triggers : Enumerable.Empty<ICommunicatorMessage>();
        }
    }
}
