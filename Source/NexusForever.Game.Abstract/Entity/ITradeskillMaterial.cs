using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Abstract.Entity
{
    public interface ITradeskillMaterial : IDatabaseCharacter
    {
        /// <summary>
        /// Stage material changes and register acknowledgements for a successful commit.
        /// </summary>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        TradeskillMaterialEntry Entry { get; }
        ulong Owner { get; }
        ushort MaterialId { get; }
        ushort Amount { get; set; }
    }
}
