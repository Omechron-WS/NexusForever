using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Loot;
using NexusForever.Network.World.Message.Model.Loot;

namespace NexusForever.Game.Abstract.Loot
{
    public interface ILootInstanceItem
    {
        uint Id { get; }
        uint StaticId { get; }
        LootItemType Type { get; }
        uint Amount { get; }
        bool Delivered { get; }
        uint WinnerGuid { get; }

        /// <summary>
        /// Assign a winner for this loot item.
        /// </summary>
        void SetWinner(ulong characterId, uint guid);

        /// <summary>
        /// Attempt to deliver the item to the player's inventory, currency, or quest progress.
        /// </summary>
        bool DeliverItem(IPlayer player, bool sendAsGrant = true);

        /// <summary>
        /// Build a <see cref="LootItem"/> for packet serialisation.
        /// </summary>
        LootItem Build();
    }
}
