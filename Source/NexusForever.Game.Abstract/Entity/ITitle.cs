using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.GameTable.Model;
using NexusForever.Shared;

namespace NexusForever.Game.Abstract.Entity
{
    public interface ITitle : IDatabaseCharacter, IUpdate
    {
        /// <summary>
        /// Stage title changes and register acknowledgements for a successful commit.
        /// </summary>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        ulong CharacterId { get; }
        CharacterTitleEntry Entry { get; }
        bool Revoked { get; set; }
        double? TimeRemaining { get; set; }
    }
}
