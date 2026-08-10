using NexusForever.Database;
using NexusForever.Database.Character;

namespace NexusForever.Game.Abstract.Entity
{
    public interface ICustomisation : IDatabaseCharacter
    {
        ulong CharacterId { get; }
        uint Label { get; }
        uint Value { get; set; }

        bool PendingDelete { get; }

        /// <summary>
        /// Stage customisation changes and register their successful-commit acknowledgements.
        /// </summary>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        /// <summary>
        /// Stage customisation changes and invoke the supplied action when a requested deletion commits.
        /// </summary>
        void Save(CharacterContext context, ISaveCommitScope commitScope, Action deleteAcknowledged);

        void Delete();

        /// <summary>
        /// Enqueue or cancel deletion of this customisation.
        /// </summary>
        void EnqueueDelete(bool set);
    }
}
