using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Static.Entity;

namespace NexusForever.Game.Abstract.Entity
{
    public interface IStatValue
    {
        Stat Stat { get; }
        StatType Type { get; }
        float Value { get; set; }
        uint Data { get; set; }

        void SaveCharacter(ulong characterId, CharacterContext context);

        /// <summary>
        /// Stage character stat changes and register acknowledgements for a successful commit.
        /// </summary>
        void SaveCharacter(ulong characterId, CharacterContext context, ISaveCommitScope commitScope)
        {
            SaveCharacter(characterId, context);
        }
    }
}
