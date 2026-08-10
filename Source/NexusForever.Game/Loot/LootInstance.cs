using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Static.Loot;
using NetworkLootItem = NexusForever.Network.World.Message.Model.Loot.LootItem;
using ServerLootNotify = NexusForever.Network.World.Message.Model.Loot.ServerLootNotify;

namespace NexusForever.Game.Loot
{
    public class LootInstance : ILootInstance
    {
        private const double ExpiryDuration = 1800d;

        public uint Guid { get; }
        public LootEntityType LootEntityType { get; }
        public LooterType LooterType { get; }
        public bool Explosion { get; set; }

        /// <summary>
        /// World position where the loot was dropped (entity death position).
        /// Used for range checks when players attempt to loot.
        /// </summary>
        public Vector3 Position { get; }

        public bool HasExpired => expiryTimer <= 0d
            || lootItems.Values.All(i => i.Delivered);

        private readonly Dictionary<ulong, uint> looterGuids = new();
        private readonly Dictionary<uint, LootInstanceItem> lootItems = new();
        private double expiryTimer = ExpiryDuration;

        public LootInstance(uint guid, LootEntityType entityType, LooterType looterType, Vector3 position)
        {
            Guid           = guid;
            LootEntityType = entityType;
            LooterType     = looterType;
            Position       = position;
        }

        /// <summary>
        /// Update the expiry timer.
        /// </summary>
        public void Update(double lastTick)
        {
            expiryTimer -= lastTick;
        }

        /// <summary>
        /// Register a player as an authorised looter.
        /// </summary>
        public void AddLooter(ulong characterId, uint playerGuid)
        {
            looterGuids.TryAdd(characterId, playerGuid);
        }

        /// <summary>
        /// Add a loot item to this instance.
        /// </summary>
        public void AddLootItem(uint staticId, LootItemType type, uint count)
        {
            var item = new LootInstanceItem(staticId, type, count);
            item.LootUnitGuid = Guid;
            lootItems.Add(item.Id, item);
        }

        /// <summary>
        /// Send the loot notification packet to the given player.
        /// </summary>
        public void SendLootNotify(IPlayer player)
        {
            ArgumentNullException.ThrowIfNull(player);

            if (!HasLooter(player.CharacterId))
                return;

            var lootItemList = new List<NetworkLootItem>();
            bool deliverImmediately = LootEntityType == LootEntityType.Item
                && LooterType == LooterType.Player;

            foreach (LootInstanceItem item in lootItems.Values)
            {
                item.SetWinner(player.CharacterId, player.Guid);

                if (deliverImmediately)
                    item.DeliverItem(player, false);

                if (item.Type == LootItemType.AccountCurrency)
                {
                    if (!item.Delivered)
                        item.DeliverItem(player, false);

                    if (item.Delivered)
                        lootItemList.AddRange(item.BuildForAccountCurrency());
                    else
                        lootItemList.Add(item.Build());
                }
                else
                {
                    NetworkLootItem networkItem = item.Build();
                    networkItem.Explosion = deliverImmediately;
                    lootItemList.Add(networkItem);
                }
            }

            if (lootItemList.Count == 0)
                return;

            player.Session.EnqueueMessageEncrypted(new ServerLootNotify
            {
                OwnerUnitId  = Guid,
                ParentUnitId = 0,
                Explosion    = Explosion,
                LootItems    = lootItemList
            });
        }

        /// <summary>
        /// Returns whether a loot instance item with the given id exists.
        /// </summary>
        public bool HasLootInstanceId(uint id)
        {
            return lootItems.ContainsKey(id);
        }

        /// <summary>
        /// Returns whether the character is an authorised looter.
        /// </summary>
        public bool HasLooter(ulong characterId)
        {
            return looterGuids.ContainsKey(characterId);
        }

        /// <summary>
        /// Get a loot instance item by its unique id.
        /// </summary>
        public LootInstanceItem GetItem(uint id)
        {
            return lootItems.GetValueOrDefault(id);
        }

        public IEnumerator<ILootInstanceItem> GetEnumerator()
        {
            return lootItems.Values.Cast<ILootInstanceItem>().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
