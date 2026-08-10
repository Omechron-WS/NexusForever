using System;
using System.Collections.Generic;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Static.AccountInventory;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Loot;
using NexusForever.Game.Static.Quest;
using NexusForever.Network.World.Message.Static;
using NetworkLootItem = NexusForever.Network.World.Message.Model.Loot.LootItem;
using ServerLootGrant = NexusForever.Network.World.Message.Model.Loot.ServerLootGrant;

namespace NexusForever.Game.Loot
{
    public class LootInstanceItem : ILootInstanceItem
    {
        private static uint nextLootId;

        public int Id { get; }
        public uint StaticId { get; }
        public LootItemType Type { get; }
        public uint Amount { get; private set; }
        public bool Delivered { get; private set; }
        public uint WinnerGuid { get; private set; }
        public ulong WinnerCharacterId { get; private set; }

        public uint LootUnitGuid { get; set; }

        public LootInstanceItem(uint staticId, LootItemType type, uint amount)
        {
            Id       = (int)System.Threading.Interlocked.Increment(ref nextLootId);
            StaticId = staticId;
            Type     = type;
            Amount   = amount;
        }

        /// <summary>
        /// Assign a winner for this loot item.
        /// </summary>
        public void SetWinner(ulong characterId, uint guid)
        {
            WinnerCharacterId = characterId;
            WinnerGuid        = guid;
        }

        /// <summary>
        /// Add to the amount of this loot item.
        /// </summary>
        public void AddToAmount(uint amount)
        {
            Amount += amount;
        }

        /// <summary>
        /// Deliver the item to the player's inventory, currency, or quest progress.
        /// </summary>
        public void DeliverItem(IPlayer player, bool sendAsGrant = true)
        {
            if (Delivered)
                return;

            switch (Type)
            {
                case LootItemType.AccountCurrency:
                    player.Account.CurrencyManager.CurrencyAddAmount((AccountCurrencyType)StaticId, Amount);
                    break;
                case LootItemType.Cash:
                    player.CurrencyManager.CurrencyAddAmount((CurrencyType)StaticId, Amount, true);
                    break;
                case LootItemType.StaticItem:
                    player.Inventory.ItemCreate(InventoryLocation.Inventory, StaticId, Amount, ItemUpdateReason.Loot);
                    break;
                case LootItemType.VirtualItem:
                    player.QuestManager.ObjectiveUpdate(QuestObjectiveType.VirtualCollect, StaticId, Amount);
                    break;
            }

            Delivered = true;

            if (sendAsGrant)
            {
                player.Session.EnqueueMessageEncrypted(new ServerLootGrant
                {
                    OwnerUnitId  = LootUnitGuid,
                    LooterUnitId = player.Guid,
                    LootItem     = Build()
                });
            }
        }

        /// <summary>
        /// Build a <see cref="LootItem"/> for packet serialisation.
        /// </summary>
        public NetworkLootItem Build()
        {
            return new NetworkLootItem
            {
                LootUnitId        = (uint)Id,
                Type              = Type,
                ItemId            = StaticId,
                Amount            = Amount,
                CanLoot           = !Delivered,
                RequiresRoll      = false,
                OnlyMasterLootable = false,
                Explosion         = false,
                RollTime          = 0,
                RandomCircuitData = 0,
                RandomGlyphData   = 0,
                ItemQuality2Id    = 0
            };
        }

        /// <summary>
        /// Build a list of <see cref="LootItem"/> for account currency with visual splitting.
        /// </summary>
        public List<NetworkLootItem> BuildForAccountCurrency()
        {
            var items = new List<NetworkLootItem>();
            uint remaining = Amount;
            uint maxItems = 50u;
            uint perItem = Math.Max(1u, remaining / maxItems);

            while (remaining > 0 && items.Count < maxItems)
            {
                uint chunk = Math.Min(perItem, remaining);
                remaining -= chunk;

                items.Add(new NetworkLootItem
                {
                    LootUnitId        = (uint)Id,
                    Type              = Type,
                    ItemId            = StaticId,
                    Amount            = chunk,
                    CanLoot           = false,
                    RequiresRoll      = false,
                    OnlyMasterLootable = false,
                    Explosion         = true,
                    RollTime          = 0,
                    RandomCircuitData = 0,
                    RandomGlyphData   = 0,
                    ItemQuality2Id    = 0
                });
            }

            if (remaining > 0 && items.Count > 0)
                items[^1].Amount += remaining;

            return items;
        }
    }
}
