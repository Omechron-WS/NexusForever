using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NexusForever.Database;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Static.AccountInventory;
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

        private Dictionary<uint, List<ILootGroup>> creatureLoot = new();
        private Dictionary<uint, List<ILootGroup>> itemLoot = new();
        private readonly List<LootInstance> activeLootInstances = new();
        private readonly ConcurrentQueue<LootInstance> pendingLootInstances = new();

        private readonly ILootTableProvider lootTableProvider;

        private double updateTimer = UpdateInterval;
        private bool isInitialised;

        /// <summary>
        /// Create the global loot manager with its world database record provider.
        /// </summary>
        public GlobalLootManager(ILootTableProvider lootTableProvider)
        {
            this.lootTableProvider = lootTableProvider ?? throw new ArgumentNullException(nameof(lootTableProvider));
        }

        /// <summary>
        /// Initialise loot tables from the world database.
        /// </summary>
        public void Initialise()
        {
            if (isInitialised)
                throw new InvalidOperationException("The global loot manager is already initialised.");

            LootTableData data = lootTableProvider.LoadLootTables()
                ?? throw new DatabaseDataException("The loot table provider returned no data.");
            Dictionary<ulong, LootGroupModel> lootGroupModels = BuildLootHierarchy(data.LootGroups);
            var builtLootGroups = new Dictionary<ulong, ILootGroup>();

            ILootGroup ResolveLootGroup(ulong groupId, string source)
            {
                if (builtLootGroups.TryGetValue(groupId, out ILootGroup builtLootGroup))
                    return builtLootGroup;

                if (!lootGroupModels.TryGetValue(groupId, out LootGroupModel lootGroupModel))
                    throw new DatabaseDataException($"{source} references missing loot group {groupId}.");

                builtLootGroup = new LootGroup(lootGroupModel);
                builtLootGroups.Add(groupId, builtLootGroup);
                return builtLootGroup;
            }

            var loadedCreatureLoot = new Dictionary<uint, List<ILootGroup>>();
            foreach (EntityLootModel model in data.EntityLoot)
            {
                if (model == null)
                    throw new DatabaseDataException("Creature loot mappings contain a null record.");

                if (!loadedCreatureLoot.TryGetValue(model.Id, out List<ILootGroup> groups))
                {
                    groups = new List<ILootGroup>();
                    loadedCreatureLoot.Add(model.Id, groups);
                }

                groups.Add(ResolveLootGroup(model.LootGroupId, $"Creature loot mapping {model.Id}"));
            }

            var loadedItemLoot = new Dictionary<uint, List<ILootGroup>>();
            foreach (ItemLootModel model in data.ItemLoot)
            {
                if (model == null)
                    throw new DatabaseDataException("Item loot mappings contain a null record.");

                if (!loadedItemLoot.TryGetValue(model.Id, out List<ILootGroup> groups))
                {
                    groups = new List<ILootGroup>();
                    loadedItemLoot.Add(model.Id, groups);
                }

                groups.Add(ResolveLootGroup(model.LootGroupId, $"Item loot mapping {model.Id}"));
            }

            creatureLoot  = loadedCreatureLoot;
            itemLoot      = loadedItemLoot;
            isInitialised = true;

            log.Info($"Loaded loot tables for {creatureLoot.Count} creatures and {itemLoot.Count} items.");
        }

        /// <summary>
        /// Update active loot instances, removing expired ones.
        /// </summary>
        public void Update(double lastTick)
        {
            if (!isInitialised)
                throw new InvalidOperationException("The global loot manager must be initialised before it is updated.");

            while (pendingLootInstances.TryDequeue(out LootInstance pendingLootInstance))
                activeLootInstances.Add(pendingLootInstance);

            foreach (LootInstance lootInstance in activeLootInstances)
                lootInstance.Update(lastTick);

            updateTimer -= lastTick;
            if (updateTimer > 0d)
                return;

            updateTimer = UpdateInterval;

            for (int i = activeLootInstances.Count - 1; i >= 0; i--)
                if (activeLootInstances[i].HasExpired)
                    activeLootInstances.RemoveAt(i);
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
                instance.AddLootItem((uint)AccountCurrencyType.Omnibits, LootItemType.AccountCurrency, omnibitAmount);
            }

            pendingLootInstances.Enqueue(instance);
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
            pendingLootInstances.Enqueue(instance);
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

        private static Dictionary<ulong, LootGroupModel> BuildLootHierarchy(IEnumerable<LootGroupModel> models)
        {
            var lootGroups = new Dictionary<ulong, LootGroupModel>();
            foreach (LootGroupModel model in models)
            {
                if (model == null)
                    throw new DatabaseDataException("Loot groups contain a null record.");

                if (!lootGroups.TryAdd(model.Id, model))
                    throw new DatabaseDataException($"Loot group {model.Id} is defined more than once.");

                model.Parent     = null;
                model.ChildGroup = new HashSet<LootGroupModel>();
                model.Item     ??= new HashSet<LootItemModel>();
            }

            foreach (LootGroupModel model in lootGroups.Values)
            {
                if (!model.ParentId.HasValue)
                    continue;

                if (!lootGroups.TryGetValue(model.ParentId.Value, out LootGroupModel parent))
                    throw new DatabaseDataException($"Loot group {model.Id} references missing parent {model.ParentId.Value}.");

                model.Parent = parent;
                parent.ChildGroup.Add(model);
            }

            var visiting = new HashSet<ulong>();
            var visited = new HashSet<ulong>();
            foreach (LootGroupModel model in lootGroups.Values)
                ValidateLootHierarchy(model, visiting, visited);

            return lootGroups;
        }

        private static void ValidateLootHierarchy(LootGroupModel model, HashSet<ulong> visiting, HashSet<ulong> visited)
        {
            if (visited.Contains(model.Id))
                return;

            if (!visiting.Add(model.Id))
                throw new DatabaseDataException($"Loot group hierarchy contains a cycle at group {model.Id}.");

            foreach (LootGroupModel child in model.ChildGroup)
                ValidateLootHierarchy(child, visiting, visited);

            visiting.Remove(model.Id);
            visited.Add(model.Id);
        }
    }
}
