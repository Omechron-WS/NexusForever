using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Abstract.Entity
{
    public interface IPetFlair : IDatabaseCharacter
    {
        PetFlairEntry Entry { get; }
        ulong Owner { get; }

        /// <summary>
        /// Stage pet flair changes and register their successful-commit acknowledgements.
        /// </summary>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);
    }
}
