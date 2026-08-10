using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Static.Entity;

namespace NexusForever.Game.Abstract.Entity
{
    public interface IAppearance : IDatabaseCharacter
    {
        ulong Owner { get; }
        ItemSlot ItemSlot { get; }
        ushort DisplayId { get; set; }

        bool PendingDelete { get; }

        /// <summary>
        /// Stage appearance changes and register their successful-commit acknowledgements.
        /// </summary>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        /// <summary>
        /// Stage appearance changes and invoke the supplied action when a requested deletion commits.
        /// </summary>
        void Save(CharacterContext context, ISaveCommitScope commitScope, Action deleteAcknowledged);

        void Delete();

        /// <summary>
        /// Enqueue or cancel deletion of this appearance.
        /// </summary>
        void EnqueueDelete(bool set);
    }
}
