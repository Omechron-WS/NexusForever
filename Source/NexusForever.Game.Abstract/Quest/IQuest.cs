using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Static.Quest;
using NexusForever.Shared;

namespace NexusForever.Game.Abstract.Quest
{
    public interface IQuest : IDisposable, IUpdate, IDatabaseCharacter, IDatabaseState, IEnumerable<IQuestObjective>
    {
        ushort Id { get; }
        IQuestInfo Info { get; }
        QuestState State { get; set; }
        QuestStateFlags Flags { get; set; }
        uint? Timer { get; set; }
        DateTime? Reset { get; set; }

        /// <summary>
        /// Stage quest changes and acknowledge them after the character database commits.
        /// </summary>
        /// <param name="context">Character database context.</param>
        /// <param name="commitScope">Scope receiving post-commit acknowledgements.</param>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        /// <summary>
        /// Stage quest changes and register their successful-commit acknowledgements.
        /// </summary>
        /// <param name="context">Character database context.</param>
        /// <param name="commitScope">Scope receiving post-commit acknowledgements.</param>
        /// <param name="deleteAcknowledged">Action invoked when a requested deletion commits.</param>
        void Save(CharacterContext context, ISaveCommitScope commitScope, Action deleteAcknowledged);

        void InitialiseTimer();

        /// <summary>
        /// Returns if <see cref="IQuest"/> can be deleted.
        /// </summary>
        bool CanDelete();

        /// <summary>
        /// Returns if <see cref="IQuest"/> can be abandoned.
        /// </summary>
        bool CanAbandon();

        /// <summary>
        /// Returns if <see cref="IQuest"/> can be shared with another <see cref="IPlayer"/>.
        /// </summary>
        bool CanShare();

        /// <summary>
        /// Update any <see cref="IQuestObjective"/>'s with supplied <see cref="QuestObjectiveType"/> and data with progress.
        /// </summary>
        void ObjectiveUpdate(QuestObjectiveType type, uint data, uint progress);

        /// <summary>
        /// Update any <see cref="IQuestObjective"/>'s with supplied ID with progress.
        /// </summary>
        void ObjectiveUpdate(uint id, uint progress);

        /// <summary>
        /// Complete the objective with the supplied identifier using its exact completion representation.
        /// </summary>
        void ObjectiveComplete(uint id);

        /// <summary>
        /// Complete every objective using its exact completion representation.
        /// </summary>
        void ObjectivesComplete();
    }
}
