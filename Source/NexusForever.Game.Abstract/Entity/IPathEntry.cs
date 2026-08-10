using NexusForever.Database;
using NexusForever.Database.Character;

namespace NexusForever.Game.Abstract.Entity
{
    public interface IPathEntry : IDatabaseCharacter
    {
        /// <summary>
        /// Stage path changes and register acknowledgements for a successful commit.
        /// </summary>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        Static.PlayerPath.Path Path { get; set; }
        ulong CharacterId { get; set; }
        bool Unlocked { get; set; }
        uint TotalXp { get; set; }
        byte LevelRewarded { get; set; }
    }
}
