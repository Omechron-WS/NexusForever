using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Account;
using NexusForever.Game.Static.Entity;

namespace NexusForever.Game.Abstract.Loot
{
    public interface IGlobalLootManager
    {
        /// <summary>
        /// Initialise loot tables from the world database.
        /// </summary>
        void Initialise();

        /// <summary>
        /// Generate and drop loot from a killed entity for the given player.
        /// </summary>
        ILootInstance DropLoot(IPlayer looter, IWorldEntity lootedEntity);

        /// <summary>
        /// Generate and drop loot from a consumed item (loot bag) for the given player.
        /// </summary>
        void DropLoot(IPlayer looter, IItem lootedItem);

        /// <summary>
        /// Award a specific loot instance item to the player.
        /// </summary>
        void GiveLoot(IPlayer looter, int lootInstanceItemId);

        /// <summary>
        /// Award all lootable items within range to the player.
        /// </summary>
        void GiveAllLootInRange(IPlayer looter);

        /// <summary>
        /// Directly award account currency to a player.
        /// </summary>
        void GiveLoot(IPlayer player, AccountCurrencyType type, uint count, uint lootUnitId);

        /// <summary>
        /// Directly award character currency to a player.
        /// </summary>
        void GiveLoot(IPlayer player, CurrencyType type, uint count, uint lootUnitId);
    }
}
