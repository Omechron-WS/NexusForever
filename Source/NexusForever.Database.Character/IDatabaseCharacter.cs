using NexusForever.Database;

namespace NexusForever.Database.Character
{
    public interface IDatabaseCharacter
    {
        void Save(CharacterContext context);

        /// <summary>
        /// Stage character database changes and register acknowledgements for a successful commit.
        /// </summary>
        /// <param name="context">Character database context.</param>
        /// <param name="commitScope">Scope receiving post-commit acknowledgements.</param>
        void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            Save(context);
        }
    }
}
