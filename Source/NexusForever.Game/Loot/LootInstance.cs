using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Static.Loot;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Shared;

namespace NexusForever.Game.Loot
{
    public class LootInstance : ILootInstance
    {
        private const double ExpiryDuration = 1800d;

        public uint Guid { get; }
        public LootEntityType LootEntityType { get; }
        public LooterType LooterType { get; }
        public bool Explosion { get; set; }

        public bool HasExpired => expiryTimer <= 0d
            || lootItems.Values.All(i => i.Delivered);

        private readonly Dictionary<ulong, uint> looterGuids = new();
        private readonly Dictionary<int, LootInstanceItem> lootItems = new();
        private double expiryTimer = ExpiryDuration;

        public LootInstance(uint guid, LootEntityType entityType, LooterType looterType)
        {
            Guid           = guid;
            LootEntityType = entityType;
            LooterType     = looterType;
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
            var lootItemList = new List<NetworkLootItem>();

            foreach (LootInstanceItem item in lootItems.Values)
            {
                if (item.Delivered)
                    continue;

                if (item.Type == LootItemType.AccountCurrency)
                {
                    lootItemList.AddRange(item.BuildForAccountCurrency());
                    item.DeliverItem(player, false);
                }
                else
                {
                    lootItemList.Add(item.Build());
                }
            }

            if (lootItemList.Count == 0)
                return;

            player.Session.EnqueueMessageEncrypted(new ServerLootNotify
            {
                UnitId    = Guid,
                Unknown0  = 0,
                Explosion = Explosion,
                LootItems = lootItemList
            });
        }

        /// <summary>
        /// Returns whether a loot instance item with the given id exists.
        /// </summary>
        public bool HasLootInstanceId(int id)
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
        public LootInstanceItem GetItem(int id)
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
