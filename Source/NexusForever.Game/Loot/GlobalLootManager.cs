using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NexusForever.Database;
using NexusForever.Database.World;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Static.Account;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Loot;
using NexusForever.Shared;
using NetworkLootItem = NexusForever.Network.World.Message.Model.Loot.LootItem;
using ServerLootGrant = NexusForever.Network.World.Message.Model.Loot.ServerLootGrant;
using NLog;

namespace NexusForever.Game.Loot
{
    public sealed class GlobalLootManager : Singleton<GlobalLootManager>, IGlobalLootManager
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const float LootRange = 35f;
        private const double OmnibitDropChance = 35d;
        private const uint OmnibitMinAmount = 7;
        private const uint OmnibitMaxAmount = 25;
        private const double UpdateInterval = 1d;

        private readonly Dictionary<uint, List<ILootGroup>> creatureLoot = new();
        private readonly Dictionary<uint, List<ILootGroup>> itemLoot = new();
        private readonly List<LootInstance> activeLootInstances = new();

        private double updateTimer;

        /// <summary>
        /// Initialise loot tables from the world database.
        /// </summary>
        public void Initialise()
        {
            WorldDatabase worldDatabase = DatabaseManager.Instance.GetDatabase<WorldDatabase>();

            foreach (EntityLootModel model in worldDatabase.GetEntityLoot())
            {
                if (model.LootGroup == null)
                    continue;

                if (!creatureLoot.TryGetValue(model.Id, out List<ILootGroup> groups))
                {
                    groups = new List<ILootGroup>();
                    creatureLoot.Add(model.Id, groups);
                }

                groups.Add(new LootGroup(model.LootGroup));
            }

            foreach (ItemLootModel model in worldDatabase.GetItemLoot())
            {
                if (model.LootGroup == null)
                    continue;

                if (!itemLoot.TryGetValue(model.Id, out List<ILootGroup> groups))
                {
                    groups = new List<ILootGroup>();
                    itemLoot.Add(model.Id, groups);
                }

                groups.Add(new LootGroup(model.LootGroup));
            }

            log.Info($"Loaded loot tables for {creatureLoot.Count} creatures and {itemLoot.Count} items.");
        }

        /// <summary>
        /// Update active loot instances, removing expired ones.
        /// </summary>
        public void Update(double lastTick)
        {
            updateTimer -= lastTick;
            if (updateTimer > 0d)
                return;

            updateTimer = UpdateInterval;

            for (int i = activeLootInstances.Count - 1; i >= 0; i--)
            {
                activeLootInstances[i].Update(UpdateInterval);
                if (activeLootInstances[i].HasExpired)
                    activeLootInstances.RemoveAt(i);
            }
        }

        /// <summary>
        /// Generate and drop loot from a killed entity for the given player.
        /// </summary>
        public ILootInstance DropLoot(IPlayer looter, IWorldEntity lootedEntity)
        {
            if (!creatureLoot.TryGetValue(lootedEntity.CreatureId, out List<ILootGroup> groups))
                return null;

            LootInstance instance = GenerateLootInstance(
                looter,
                lootedEntity.Guid,
                LootEntityType.Creature,
                lootedEntity.Position,
                groups);

            if (instance == null)
                return null;

            // Omnibit drop chance
            if (Random.Shared.NextDouble() * 100d < OmnibitDropChance)
            {
                uint omnibitAmount = OmnibitMinAmount + (uint)Random.Shared.Next(0, (int)(OmnibitMaxAmount - OmnibitMinAmount + 1)) + looter.Level;
                instance.AddLootItem((uint)AccountCurrencyType.Omnibit, LootItemType.AccountCurrency, omnibitAmount);
            }

            activeLootInstances.Add(instance);
            instance.SendLootNotify(looter);
            return instance;
        }

        /// <summary>
        /// Generate and drop loot from a consumed item (loot bag) for the given player.
        /// </summary>
        public void DropLoot(IPlayer looter, IItem lootedItem)
        {
            if (!itemLoot.TryGetValue(lootedItem.Info.Entry.Id, out List<ILootGroup> groups))
                return;

            LootInstance instance = GenerateLootInstance(
                looter,
                looter.Guid,
                LootEntityType.Item,
                looter.Position,
                groups);

            if (instance == null)
                return;

            instance.Explosion = true;
            activeLootInstances.Add(instance);
            instance.SendLootNotify(looter);
        }

        /// <summary>
        /// Award a specific loot instance item to the player.
        /// </summary>
        public void GiveLoot(IPlayer looter, int lootInstanceItemId)
        {
            LootInstance lootInstance = GetLootInstanceForItem(lootInstanceItemId);
            if (lootInstance == null)
                return;

            if (!lootInstance.HasLooter(looter.CharacterId))
                return;

            if (lootInstance.HasExpired)
                return;

            LootInstanceItem item = lootInstance.GetItem(lootInstanceItemId);
            if (item == null || item.Delivered)
                return;

            float distance = Vector3.Distance(looter.Position, lootInstance.Position);
            if (distance > LootRange)
                return;

            item.DeliverItem(looter);
        }

        /// <summary>
        /// Award all lootable items within range to the player.
        /// </summary>
        public void GiveAllLootInRange(IPlayer looter)
        {
            foreach (LootInstance instance in activeLootInstances)
            {
                if (!instance.HasLooter(looter.CharacterId))
                    continue;

                if (instance.HasExpired)
                    continue;

                float distance = Vector3.Distance(looter.Position, instance.Position);
                if (distance > LootRange)
                    continue;

                foreach (ILootInstanceItem lootItem in instance)
                    if (!lootItem.Delivered)
                        lootItem.DeliverItem(looter);
            }
        }

        /// <summary>
        /// Directly award account currency to a player.
        /// </summary>
        public void GiveLoot(IPlayer player, AccountCurrencyType type, uint count, uint lootUnitId)
        {
            player.Account.CurrencyManager.CurrencyAddAmount(type, count);

            player.Session.EnqueueMessageEncrypted(new ServerLootGrant
            {
                OwnerUnitId  = lootUnitId,
                LooterUnitId = player.Guid,
                LootItem     = new NetworkLootItem
                {
                    LootUnitId = 0,
                    Type       = LootItemType.AccountCurrency,
                    ItemId     = (uint)type,
                    Amount     = count
                }
            });
        }

        /// <summary>
        /// Directly award character currency to a player.
        /// </summary>
        public void GiveLoot(IPlayer player, CurrencyType type, uint count, uint lootUnitId)
        {
            player.CurrencyManager.CurrencyAddAmount(type, count, true);

            player.Session.EnqueueMessageEncrypted(new ServerLootGrant
            {
                OwnerUnitId  = lootUnitId,
                LooterUnitId = player.Guid,
                LootItem     = new NetworkLootItem
                {
                    LootUnitId = 0,
                    Type       = LootItemType.Cash,
                    ItemId     = (uint)type,
                    Amount     = count
                }
            });
        }

        private LootInstance GenerateLootInstance(IPlayer looter, uint entityGuid, LootEntityType entityType, Vector3 position, List<ILootGroup> groups)
        {
            var allDrops = new Dictionary<ILootItem, uint>();
            foreach (ILootGroup group in groups)
            {
                Dictionary<ILootItem, uint> groupDrops = group.GenerateLootDrops(looter);
                foreach ((ILootItem item, uint count) in groupDrops)
                    allDrops[item] = count;
            }

            if (allDrops.Count == 0)
                return null;

            var instance = new LootInstance(entityGuid, entityType, LooterType.Player, position);
            instance.AddLooter(looter.CharacterId, looter.Guid);

            foreach ((ILootItem item, uint count) in allDrops)
                instance.AddLootItem(item.StaticId, item.Type, count);

            return instance;
        }

        private LootInstance GetLootInstanceForItem(int itemId)
        {
            return activeLootInstances.FirstOrDefault(i => i.HasLootInstanceId(itemId));
        }
    }
}
