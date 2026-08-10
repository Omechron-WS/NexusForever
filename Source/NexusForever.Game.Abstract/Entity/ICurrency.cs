using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Abstract.Entity
{
    public interface ICurrency : IDatabaseCharacter
    {
        /// <summary>
        /// Stage currency changes and register acknowledgements for a successful commit.
        /// </summary>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        CurrencyTypeEntry Entry { get; set; }
        CurrencyType Id { get; }
        ulong CharacterId { get; set; }

        ulong Amount { get; set; }
    }
}
