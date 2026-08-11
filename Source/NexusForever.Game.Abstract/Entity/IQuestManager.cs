using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Static.Quest;
using NexusForever.Shared;

namespace NexusForever.Game.Abstract.Entity
{
    public interface IQuestManager : IDisposable, IDatabaseCharacter, IUpdate
    {
        /// <summary>
        /// Stage quest changes and acknowledge them after the character database commits.
        /// </summary>
        /// <param name="context">Character database context.</param>
        /// <param name="commitScope">Scope receiving post-commit acknowledgements.</param>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        void SendInitialPackets();

        /// <summary>
        /// Return <see cref="QuestState"/> for supplied quest.
        /// </summary>
        QuestState? GetQuestState<T>(T questId) where T : Enum;

        /// <summary>
        /// Return <see cref="QuestState"/> for supplied quest.
        /// </summary>
        QuestState? GetQuestState(ushort questId);

        /// <summary>
        /// Mention a quest from supplied quest id, skipping any prerequisites checks.
        /// </summary>
        void QuestMention(ushort questId);

        /// <summary>
        /// Mention a quest from supplied <see cref="IQuestInfo"/>, skipping any prerequisites checks.
        /// </summary>
        void QuestMention(IQuestInfo info);

        /// <summary>
        /// Add a quest from supplied id, optionally supplying <see cref="IItem"/> which was used to start the quest.
        /// </summary>
        void QuestAdd(ushort questId, IItem item);

        /// <summary>
        /// Add a quest from supplied <see cref="IQuestInfo"/>, skipping any prerequisites checks.
        /// </summary>
        void QuestAdd(IQuestInfo info);

        /// <summary>
        /// Retry an inactive quest id that was previously failed.
        /// </summary>
        void QuestRetry(ushort questId);

        /// <summary>
        /// Abandon an active quest.
        /// </summary>
        void QuestAbandon(ushort questId);

        /// <summary>
        /// Complete all <see cref="IQuestObjective"/>'s for supplied active quest id.
        /// </summary>
        void QuestAchieve(ushort questId);

        /// <summary>
        /// Complete single <see cref="IQuestObjective"/> for supplied active quest id.
        /// </summary>
        void QuestAchieveObjective(ushort questId, byte index);

        /// <summary>
        /// Attempts to apply raw progress to an exact objective slot on an active quest.
        /// </summary>
        /// <param name="questId">Quest identifier.</param>
        /// <param name="objectiveIndex">Zero-based objective slot.</param>
        /// <param name="progress">Raw objective progress value.</param>
        /// <param name="objective">Resolved objective when the quest and slot are valid.</param>
        /// <returns><see langword="true"/> when objective progress changed; otherwise, <see langword="false"/>.</returns>
        bool TryObjectiveUpdate(
            ushort questId,
            byte objectiveIndex,
            uint progress,
            out IQuestObjective objective);

        /// <summary>
        /// Complete an achieved quest supplying an optional reward and whether the quest was completed from the communicator.
        /// </summary>
        void QuestComplete(ushort questId, ushort reward, bool communicator);

        /// <summary>
        /// Ignore or acknowledge an inactive quest.
        /// </summary>
        void QuestIgnore(ushort questId, bool ignored);

        /// <summary>
        /// Track or hide an active quest.
        /// </summary>
        void QuestTrack(ushort questId, bool tracked);

        /// <summary>
        /// Share supplied quest with another <see cref="IPlayer"/>.
        /// </summary>
        void QuestShare(ushort questId);

        /// <summary>
        /// Returns whether the owner currently has the supplied quest in a shareable state.
        /// </summary>
        bool CanShareQuest(ushort questId);

        /// <summary>
        /// Offer a shared quest to the owner after validating the supplied sharer identity and group.
        /// </summary>
        bool OfferQuestShare(ushort questId, uint sharerGuid, ulong sharerCharacterId, ulong groupAssociation);

        /// <summary>
        /// Accept or deny a shared quest from another <see cref="IPlayer"/>.
        /// </summary>
        void QuestShareResult(ushort questId, bool result);

        /// <summary>
        /// Update any active quest <see cref="IQuestObjective"/>'s with supplied <see cref="QuestObjectiveType"/> and data with progress.
        /// </summary>
        void ObjectiveUpdate(QuestObjectiveType type, uint data, uint progress);

        /// <summary>
        /// Update matching active quest objectives except for exact static objective identifiers.
        /// </summary>
        /// <param name="type">Objective type.</param>
        /// <param name="data">Objective target data.</param>
        /// <param name="progress">Raw objective progress value.</param>
        /// <param name="excludedObjectiveIds">Static objective identifiers excluded from this event.</param>
        void ObjectiveUpdate(
            QuestObjectiveType type,
            uint data,
            uint progress,
            IReadOnlySet<uint> excludedObjectiveIds);

        // <summary>
        /// Update any active quest <see cref="IQuestObjective"/>'s with supplied ID with progress.
        /// </summary>
        void ObjectiveUpdate(uint id, uint progress);

        /// <summary>
        /// Returns a collection of all active quests.
        /// </summary>
        IEnumerable<IQuest> GetActiveQuests();
    }
}
