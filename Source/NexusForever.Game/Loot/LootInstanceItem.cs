using System;
using System.Collections.Generic;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Static.AccountInventory;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Loot;
using NexusForever.Game.Static.Quest;
using NexusForever.Network.World.Message.Static;
using NLog;
using NetworkLootItem = NexusForever.Network.World.Message.Model.Loot.LootItem;
using ServerLootGrant = NexusForever.Network.World.Message.Model.Loot.ServerLootGrant;

namespace NexusForever.Game.Loot
{
    public class LootInstanceItem : ILootInstanceItem
    {
        private const int DeliveryPending = 0;
        private const int DeliveryInProgress = 1;
        private const int DeliveryComplete = 2;

        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static uint nextLootId;

        public uint Id { get; }
        public uint StaticId { get; }
        public LootItemType Type { get; }
        public uint Amount { get; private set; }
        public bool Delivered => Volatile.Read(ref deliveryState) == DeliveryComplete;
        public uint WinnerGuid { get; private set; }
        public ulong WinnerCharacterId { get; private set; }

        public uint LootUnitGuid { get; set; }

        private readonly object winnerLock = new();
        private int deliveryState;

        public LootInstanceItem(uint staticId, LootItemType type, uint amount)
        {
            if (!CanDeliver(type))
                throw new ArgumentOutOfRangeException(nameof(type), type, "The loot item type cannot be delivered.");

            do
            {
                Id = System.Threading.Interlocked.Increment(ref nextLootId);
            }
            while (Id == 0u);
            StaticId = staticId;
            Type     = type;
            Amount   = amount;
        }

        /// <summary>
        /// Assign a winner for this loot item.
        /// </summary>
        public void SetWinner(ulong characterId, uint guid)
        {
            lock (winnerLock)
            {
                if (WinnerCharacterId != 0ul && WinnerCharacterId != characterId)
                    throw new InvalidOperationException("The loot item already has a different winner.");

                WinnerCharacterId = characterId;
                WinnerGuid        = guid;
            }
        }

        /// <summary>
        /// Add to the amount of this loot item.
        /// </summary>
        public void AddToAmount(uint amount)
        {
            Amount += amount;
        }

        /// <summary>
        /// Attempt to deliver the item to the player's inventory, currency, or quest progress.
        /// </summary>
        public bool DeliverItem(IPlayer player, bool sendAsGrant = true)
        {
            ArgumentNullException.ThrowIfNull(player);

            lock (winnerLock)
            {
                if (WinnerCharacterId == 0ul
                    || WinnerCharacterId != player.CharacterId
                    || WinnerGuid != player.Guid)
                    return false;
            }

            if (Interlocked.CompareExchange(ref deliveryState, DeliveryInProgress, DeliveryPending) != DeliveryPending)
                return false;

            bool deliveryCommitted = false;
            try
            {
                switch (Type)
                {
                    case LootItemType.AccountCurrency:
                        player.Account.CurrencyManager.CurrencyAddAmount((AccountCurrencyType)StaticId, Amount);
                        break;
                    case LootItemType.Cash:
                        player.CurrencyManager.CurrencyAddAmount((CurrencyType)StaticId, Amount, true);
                        break;
                    case LootItemType.StaticItem:
                        if (!player.Inventory.TryItemCreate(
                            InventoryLocation.Inventory,
                            StaticId,
                            Amount,
                            out _,
                            ItemUpdateReason.Loot))
                            return false;
                        break;
                    case LootItemType.VirtualItem:
                        player.QuestManager.ObjectiveUpdate(QuestObjectiveType.VirtualCollect, StaticId, Amount);
                        break;
                    default:
                        log.Warn("Loot item type {0} is not supported for delivery.", Type);
                        return false;
                }

                Volatile.Write(ref deliveryState, DeliveryComplete);
                deliveryCommitted = true;

                if (sendAsGrant)
                {
                    player.Session.EnqueueMessageEncrypted(new ServerLootGrant
                    {
                        OwnerUnitId  = LootUnitGuid,
                        LooterUnitId = player.Guid,
                        LootItem     = Build()
                    });
                }

                return true;
            }
            catch (Exception exception)
            {
                // Reward operations are not transactional. Once one has thrown, retain an at-most-once
                // claim boundary so a retry cannot duplicate a mutation that completed before the failure.
                Volatile.Write(ref deliveryState, DeliveryComplete);
                deliveryCommitted = true;
                log.Error(exception, "Failed while delivering loot item {0}; the claim will not be retried.", Id);
                throw;
            }
            finally
            {
                if (!deliveryCommitted)
                    Volatile.Write(ref deliveryState, DeliveryPending);
            }
        }

        /// <summary>
        /// Return whether this server can deliver the supplied loot item type.
        /// </summary>
        internal static bool CanDeliver(LootItemType type)
        {
            return type is LootItemType.AccountCurrency
                or LootItemType.Cash
                or LootItemType.StaticItem
                or LootItemType.VirtualItem;
        }

        /// <summary>
        /// Build a <see cref="LootItem"/> for packet serialisation.
        /// </summary>
        public NetworkLootItem Build()
        {
            return new NetworkLootItem
            {
                LootUnitId        = Id,
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
                    LootUnitId        = Id,
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
