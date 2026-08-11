using NexusForever.Game.Abstract.Entity;
using System.Collections.Concurrent;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Abstract.Reputation;
using NexusForever.Game.Prerequisite;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Game.Static.Reputation;
using NexusForever.GameTable.Model;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model.Story;
using NLog;

namespace NexusForever.Game.Quest
{
    public class CommunicatorMessage : ICommunicatorMessage
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly ConcurrentDictionary<uint, byte> loggedConditionFailures = new();

        internal const uint MaximumId = 0x7FFFu;

        public uint Id => entry.Id;
        public ushort QuestId
        {
            get
            {
                if (entry.QuestIdDelivered > MaximumId)
                    throw new InvalidDataException($"Communicator message {entry.Id} has invalid quest {entry.QuestIdDelivered}.");

                return (ushort)entry.QuestIdDelivered;
            }
        }

        private readonly CommunicatorMessagesEntry entry;
        private readonly IPrerequisiteManager prerequisiteManager;

        /// <summary>
        /// Create a new <see cref="ICommunicatorMessage"/> with supplied <see cref="CommunicatorMessagesEntry"/>.
        /// </summary>
        public CommunicatorMessage(CommunicatorMessagesEntry entry)
            : this(entry, null)
        {
        }

        internal CommunicatorMessage(
            CommunicatorMessagesEntry entry,
            IPrerequisiteManager prerequisiteManager)
        {
            this.entry               = entry ?? throw new ArgumentNullException(nameof(entry));
            this.prerequisiteManager = prerequisiteManager;
        }

        /// <summary>
        /// Checks if <see cref="IPlayer"/> meets the required conditions for this quest to be added to their communicator.
        /// </summary>
        public bool Meets(IPlayer player)
        {
            if (player == null || entry.Id == 0u || entry.Id > MaximumId
                || entry.QuestIdDelivered > MaximumId)
                return false;

            try
            {
                return MeetsCore(player);
            }
            catch (Exception exception)
            {
                // Communicator conditions originate in external game-table data. A malformed
                // row or unavailable dependency must not interrupt the remaining messages.
                if (loggedConditionFailures.TryAdd(entry.Id, 0))
                    log.Warn(exception, $"Communicator message {entry.Id} condition evaluation failed.");
                return false;
            }
        }

        private bool MeetsCore(IPlayer player)
        {
            if (entry.WorldId != 0u && entry.WorldId != player.Map?.Entry?.Id)
                return false;

            // TODO: Skip this check until we have better WorldZoneId tracking
            // It also appears as though this is more of a "Trigger when Player gets here".
            // It's plausible this check should be "Has this player been to this zone id?".
            //if (entry.WorldZoneId != 0u && entry.WorldZoneId != player.Zone.Id)
            //    return false;

            if (entry.MinLevel != 0u && entry.MaxLevel != 0u && entry.MinLevel > entry.MaxLevel)
                return false;

            if (entry.MinLevel != 0u && player.Level < entry.MinLevel)
                return false;

            if (entry.MaxLevel != 0u && player.Level > entry.MaxLevel)
                return false;

            if (entry.Quests == null || entry.States == null || entry.Quests.Length != entry.States.Length)
                return false;

            for (int i = 0; i < entry.Quests.Length; i++)
            {
                uint rawQuestId = entry.Quests[i];
                if (rawQuestId == 0u)
                    continue;

                if (rawQuestId > MaximumId || player.QuestManager == null)
                    return false;

                if (player.QuestManager.GetQuestState((ushort)rawQuestId) != (QuestState)entry.States[i])
                    return false;
            }

            if (entry.FactionId != 0u && (Faction)entry.FactionId != player.Faction1)
                return false;

            if (entry.ClassId > byte.MaxValue
                || (entry.ClassId != 0u && (Class)entry.ClassId != player.Class))
                return false;

            if (entry.RaceId > byte.MaxValue
                || (entry.RaceId != 0u && (Race)entry.RaceId != player.Race))
                return false;

            if (!MeetsReputation(player))
                return false;

            if (entry.PrerequisiteId != 0u
                && !(prerequisiteManager ?? PrerequisiteManager.Instance).Meets(player, entry.PrerequisiteId))
                return false;

            return true;
        }

        private bool MeetsReputation(IPlayer player)
        {
            if (entry.ReputationMin == 0u && entry.ReputationMax == 0u)
                return true;

            if (entry.FactionIdReputation == 0u
                || (entry.ReputationMin != 0u && entry.ReputationMax != 0u
                    && entry.ReputationMin > entry.ReputationMax)
                || player.ReputationManager == null)
                return false;

            IReputation reputation = player.ReputationManager.GetReputation((Faction)entry.FactionIdReputation);
            float amount = reputation?.Amount ?? 0f;
            if (!float.IsFinite(amount))
                return false;

            if (entry.ReputationMin != 0u && amount < entry.ReputationMin)
                return false;

            return entry.ReputationMax == 0u || amount <= entry.ReputationMax;
        }

        /// <summary>
        /// Send communicator message to <see cref="IGameSession"/>.
        /// </summary>
        public void Send(IGameSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (entry.Id == 0u || entry.Id > MaximumId)
                throw new InvalidDataException($"Communicator message {entry.Id} does not fit the build 16042 packet field.");

            session.EnqueueMessageEncrypted(new ServerCommunicatorMessage
            {
                CommunicatorMessagesId = checked((ushort)entry.Id)
            });
        }
    }
}
