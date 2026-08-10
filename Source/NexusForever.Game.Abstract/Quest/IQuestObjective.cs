using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Shared;

namespace NexusForever.Game.Abstract.Quest
{
    public interface IQuestObjective : IUpdate, IDatabaseCharacter
    {
        IQuestInfo QuestInfo { get; }
        IQuestObjectiveInfo ObjectiveInfo { get; }
        byte Index { get; }
        uint Progress { get; set; }
        uint? Timer { get; set; }

        /// <summary>
        /// Stage objective changes and acknowledge them after the character database commits.
        /// </summary>
        /// <param name="context">Character database context.</param>
        /// <param name="commitScope">Scope receiving post-commit acknowledgements.</param>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        /// <summary>
        /// Enqueue the objective to be inserted after its parent quest deletion was cancelled.
        /// </summary>
        void EnqueueCreate();

        /// <summary>
        /// Return if the objective has been completed.
        /// </summary>
        bool IsComplete();

        /// <summary>
        /// Update object progress with supplied update.
        /// </summary>
        void ObjectiveUpdate(uint update);

        /// <summary>
        /// Complete this <see cref="IQuestObjective"/>.
        /// </summary>
        void Complete();
    }
}
