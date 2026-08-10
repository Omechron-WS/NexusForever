using NexusForever.Database;
using NexusForever.Database.Character;

namespace NexusForever.Game.Abstract.Entity
{
    public interface IBone : IDatabaseCharacter
    {
        ulong Owner { get; }
        byte BoneIndex { get; }
        float BoneValue { get; set; }
        
        bool PendingDelete { get; }

        /// <summary>
        /// Stage bone changes and register their successful-commit acknowledgements.
        /// </summary>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        /// <summary>
        /// Stage bone changes and invoke the supplied action when a requested deletion commits.
        /// </summary>
        void Save(CharacterContext context, ISaveCommitScope commitScope, Action deleteAcknowledged);

        void Delete();

        /// <summary>
        /// Enqueue or cancel deletion of this bone.
        /// </summary>
        void EnqueueDelete(bool set);
    }
}
