using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NexusForever.Database;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Static.AccountInventory;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Loot;
using NexusForever.Shared;
using NetworkLootItem = NexusForever.Network.World.Message.Model.Loot.LootItem;
using ServerLootGrant = NexusForever.Network.World.Message.Model.Loot.ServerLootGrant;
using ServerLootRemove = NexusForever.Network.World.Message.Model.Loot.ServerLootRemove;
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
        private readonly ConcurrentDictionary<uint, LootInstance> lootInstancesByItemId = new();
        private readonly ConcurrentDictionary<LootInstance, IBaseMap> lootMaps = new();
        private readonly ConcurrentDictionary<LootInstance, IWorldEntity> lootOwners = new();

        private readonly ILootTableProvider lootTableProvider;
        private readonly Func<double> omnibitChanceRoll;
        private readonly Func<int, int, int> omnibitAmountRoll;

        private double updateTimer = UpdateInterval;
        private bool isInitialised;

        /// <summary>
        /// Create the global loot manager with its world database record provider.
        /// </summary>
        public GlobalLootManager(ILootTableProvider lootTableProvider)
            : this(
                lootTableProvider,
                static () => Random.Shared.NextDouble(),
                static (minimum, maximum) => Random.Shared.Next(minimum, maximum))
        {
        }

        internal GlobalLootManager(
            ILootTableProvider lootTableProvider,
            Func<double> omnibitChanceRoll,
            Func<int, int, int> omnibitAmountRoll)
        {
            this.lootTableProvider  = lootTableProvider ?? throw new ArgumentNullException(nameof(lootTableProvider));
            this.omnibitChanceRoll  = omnibitChanceRoll ?? throw new ArgumentNullException(nameof(omnibitChanceRoll));
            this.omnibitAmountRoll  = omnibitAmountRoll ?? throw new ArgumentNullException(nameof(omnibitAmountRoll));
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
            {
                if (TryGetRemovalReason(pendingLootInstance, out LootRemovalReason pendingRemovalReason))
                {
                    RemoveLootInstance(pendingLootInstance, pendingRemovalReason);
                    continue;
                }

                activeLootInstances.Add(pendingLootInstance);
            }

            if (!double.IsFinite(lastTick) || lastTick <= 0d)
                return;

            foreach (LootInstance lootInstance in activeLootInstances)
                lootInstance.Update(lastTick);

            updateTimer -= lastTick;
            if (updateTimer > 0d)
                return;

            updateTimer = UpdateInterval;

            for (int i = activeLootInstances.Count - 1; i >= 0; i--)
            {
                LootInstance lootInstance = activeLootInstances[i];
                if (!TryGetRemovalReason(lootInstance, out LootRemovalReason removalReason))
                    continue;

                RemoveLootInstance(lootInstance, removalReason);
                activeLootInstances.RemoveAt(i);
            }
        }

        /// <summary>
        /// Generate and drop loot from a killed entity for the given player.
        /// </summary>
        public ILootInstance DropLoot(IPlayer looter, IWorldEntity lootedEntity)
        {
            ArgumentNullException.ThrowIfNull(looter);
            ArgumentNullException.ThrowIfNull(lootedEntity);

            if (lootedEntity is IPetEntity)
                return null;

            if (looter.Map == null || !ReferenceEquals(looter.Map, lootedEntity.Map))
                return null;

            LootInstance instance = null;
            if (creatureLoot.TryGetValue(lootedEntity.CreatureId, out List<ILootGroup> groups))
            {
                instance = GenerateLootInstance(
                    looter,
                    lootedEntity.Guid,
                    LootEntityType.Creature,
                    lootedEntity.Position,
                    groups);
            }

            if (TryRollOmnibitAmount(looter, out uint omnibitAmount))
            {
                instance ??= CreateLootInstance(
                    looter,
                    lootedEntity.Guid,
                    LootEntityType.Creature,
                    lootedEntity.Position);
                instance.AddLootItem((uint)AccountCurrencyType.Omnibits, LootItemType.AccountCurrency, omnibitAmount);
            }

            if (instance == null)
                return null;

            RegisterLootInstance(instance, looter.Map, lootedEntity);
            try
            {
                instance.SendLootNotify(looter);
            }
            finally
            {
                if (instance.HasExpired)
                    RemoveLootInstance(instance, GetExpirationRemovalReason(instance));
            }

            return instance;
        }

        /// <summary>
        /// Generate and drop loot from a consumed item (loot bag) for the given player.
        /// </summary>
        public void DropLoot(IPlayer looter, IItem lootedItem)
        {
            ArgumentNullException.ThrowIfNull(looter);
            ArgumentNullException.ThrowIfNull(lootedItem);

            if (!itemLoot.TryGetValue(lootedItem.Info.Entry.Id, out List<ILootGroup> groups))
                return;

            if (looter.Map == null)
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
            RegisterLootInstance(instance, looter.Map);
            try
            {
                instance.SendLootNotify(looter);
            }
            finally
            {
                if (instance.HasExpired)
                    RemoveLootInstance(instance, GetExpirationRemovalReason(instance));
            }
        }

        /// <summary>
        /// Returns whether the supplied item has a configured loot table.
        /// </summary>
        public bool HasLootTable(IItem lootedItem)
        {
            return lootedItem?.Info?.Entry != null
                && itemLoot.ContainsKey(lootedItem.Info.Entry.Id);
        }

        /// <summary>
        /// Attempt to award a specific loot instance item to the player.
        /// </summary>
        public bool GiveLoot(IPlayer looter, uint ownerUnitId, uint lootInstanceItemId)
        {
            if (looter == null)
                return false;

            LootInstance lootInstance = GetLootInstanceForItem(lootInstanceItemId);
            if (lootInstance == null)
                return false;

            if (lootInstance.Guid != ownerUnitId)
                return false;

            if (!lootInstance.TryRefreshLooter(looter.CharacterId, looter.Guid))
                return false;

            if (lootInstance.HasExpired)
            {
                RemoveLootInstance(lootInstance, GetExpirationRemovalReason(lootInstance));
                return false;
            }

            if (!IsLootSourceActive(lootInstance))
            {
                RemoveLootInstance(lootInstance, LootRemovalReason.StaleSource);
                return false;
            }

            if (!IsLootInRange(looter, lootInstance))
                return false;

            LootInstanceItem item = lootInstance.GetItem(lootInstanceItemId);
            if (item == null || item.Delivered)
                return false;

            try
            {
                item.SetWinner(looter.CharacterId, looter.Guid);
                return item.DeliverItem(looter);
            }
            finally
            {
                if (lootInstance.HasExpired)
                    RemoveLootInstance(lootInstance, GetExpirationRemovalReason(lootInstance));
            }
        }

        /// <summary>
        /// Award all lootable items within range to the player.
        /// </summary>
        public void GiveAllLootInRange(IPlayer looter)
        {
            if (looter == null)
                return;

            foreach (LootInstance instance in lootInstancesByItemId.Values.Distinct().ToList())
            {
                if (!instance.TryRefreshLooter(looter.CharacterId, looter.Guid))
                    continue;

                if (instance.HasExpired)
                {
                    RemoveLootInstance(instance, GetExpirationRemovalReason(instance));
                    continue;
                }

                if (!IsLootSourceActive(instance))
                {
                    RemoveLootInstance(instance, LootRemovalReason.StaleSource);
                    continue;
                }

                if (!IsLootInRange(looter, instance))
                    continue;

                foreach (ILootInstanceItem lootItem in instance.ToList())
                    if (!lootItem.Delivered)
                        GiveLoot(looter, instance.Guid, lootItem.Id);
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

            LootInstance instance = CreateLootInstance(looter, entityGuid, entityType, position);

            foreach ((ILootItem item, uint count) in allDrops)
                instance.AddLootItem(item.StaticId, item.Type, count);

            return instance;
        }

        private static LootInstance CreateLootInstance(IPlayer looter, uint entityGuid, LootEntityType entityType, Vector3 position)
        {
            var instance = new LootInstance(entityGuid, entityType, LooterType.Player, position);
            instance.AddLooter(looter.CharacterId, looter.Guid);
            return instance;
        }

        private bool TryRollOmnibitAmount(IPlayer looter, out uint amount)
        {
            amount = 0u;

            double chance = omnibitChanceRoll();
            if (!double.IsFinite(chance) || chance < 0d || chance * 100d >= OmnibitDropChance)
                return false;

            int exclusiveMaximum = (int)(OmnibitMaxAmount - OmnibitMinAmount + 1u);
            int amountOffset = omnibitAmountRoll(0, exclusiveMaximum);
            if (amountOffset < 0 || amountOffset >= exclusiveMaximum)
                return false;

            amount = OmnibitMinAmount + (uint)amountOffset + looter.Level;
            return true;
        }

        private LootInstance GetLootInstanceForItem(uint itemId)
        {
            return lootInstancesByItemId.GetValueOrDefault(itemId);
        }

        private void RegisterLootInstance(LootInstance instance, IBaseMap map, IWorldEntity owner = null)
        {
            ArgumentNullException.ThrowIfNull(map);

            var registeredItemIds = new List<uint>();
            foreach (ILootInstanceItem item in instance)
            {
                if (!lootInstancesByItemId.TryAdd(item.Id, instance))
                {
                    foreach (uint registeredItemId in registeredItemIds)
                        lootInstancesByItemId.TryRemove(registeredItemId, out _);

                    throw new InvalidOperationException($"Loot item identifier {item.Id} is already active.");
                }

                registeredItemIds.Add(item.Id);
            }

            if (!lootMaps.TryAdd(instance, map))
            {
                foreach (uint registeredItemId in registeredItemIds)
                    lootInstancesByItemId.TryRemove(registeredItemId, out _);

                throw new InvalidOperationException("The loot instance is already registered.");
            }

            if (owner != null)
            {
                if (!lootOwners.TryAdd(instance, owner))
                {
                    lootMaps.TryRemove(instance, out _);
                    foreach (uint registeredItemId in registeredItemIds)
                        lootInstancesByItemId.TryRemove(registeredItemId, out _);

                    throw new InvalidOperationException("The loot instance owner is already registered.");
                }

                try
                {
                    owner.AddLoot(instance);
                }
                catch
                {
                    lootOwners.TryRemove(instance, out _);
                    lootMaps.TryRemove(instance, out _);
                    foreach (uint registeredItemId in registeredItemIds)
                        lootInstancesByItemId.TryRemove(registeredItemId, out _);

                    try
                    {
                        owner.RemoveLoot(instance);
                    }
                    catch (Exception exception)
                    {
                        log.Error(exception, $"Failed to roll back loot instance {instance.Guid} from its source entity.");
                    }

                    throw;
                }
            }

            pendingLootInstances.Enqueue(instance);
        }

        private void RemoveLootInstance(LootInstance instance, LootRemovalReason reason)
        {
            bool wasRegistered = lootMaps.TryRemove(instance, out IBaseMap map);

            foreach (ILootInstanceItem item in instance)
            {
                if (lootInstancesByItemId.TryGetValue(item.Id, out LootInstance registeredInstance)
                    && ReferenceEquals(registeredInstance, instance))
                    lootInstancesByItemId.TryRemove(item.Id, out _);
            }

            if (lootOwners.TryRemove(instance, out IWorldEntity owner))
            {
                try
                {
                    owner.RemoveLoot(instance);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to detach loot instance {instance.Guid} from its source entity.");
                }
            }

            if (!wasRegistered || reason == LootRemovalReason.Completed)
                return;

            SendLootRemove(instance, map);
        }

        private static LootRemovalReason GetExpirationRemovalReason(LootInstance instance)
        {
            return instance.IsComplete
                ? LootRemovalReason.Completed
                : LootRemovalReason.TimedOut;
        }

        private bool TryGetRemovalReason(LootInstance instance, out LootRemovalReason reason)
        {
            if (instance.IsComplete)
            {
                reason = LootRemovalReason.Completed;
                return true;
            }

            if (instance.HasTimedOut)
            {
                reason = LootRemovalReason.TimedOut;
                return true;
            }

            if (!IsLootSourceActive(instance))
            {
                reason = LootRemovalReason.StaleSource;
                return true;
            }

            reason = default;
            return false;
        }

        private void SendLootRemove(LootInstance instance, IBaseMap map)
        {
            foreach ((ulong characterId, uint playerGuid) in instance.GetLooterIdentities())
            {
                try
                {
                    if (HasVisibleSiblingLoot(instance, map, characterId))
                        continue;

                    IGridEntity gridEntity = map.GetEntity<IGridEntity>(playerGuid);
                    if (gridEntity is not IPlayer player
                        || player.CharacterId != characterId
                        || player.Guid != playerGuid
                        || !player.InWorld
                        || !ReferenceEquals(player.Map, map))
                        continue;

                    player.Session.EnqueueMessageEncrypted(new ServerLootRemove
                    {
                        OwnerUnitId = instance.Guid
                    });
                }
                catch (Exception exception)
                {
                    log.Error(
                        exception,
                        $"Failed to notify authorised character {characterId} that loot source {instance.Guid} was removed.");
                }
            }
        }

        private bool HasVisibleSiblingLoot(LootInstance removedInstance, IBaseMap map, ulong characterId)
        {
            foreach (LootInstance candidate in lootInstancesByItemId.Values.Distinct())
            {
                if (ReferenceEquals(candidate, removedInstance)
                    || candidate.Guid != removedInstance.Guid
                    || !candidate.HasLooter(characterId)
                    || candidate.IsComplete)
                    continue;

                if (lootMaps.TryGetValue(candidate, out IBaseMap candidateMap)
                    && ReferenceEquals(candidateMap, map))
                    return true;
            }

            return false;
        }

        private bool IsLootSourceActive(LootInstance instance)
        {
            if (!lootMaps.TryGetValue(instance, out IBaseMap map))
                return false;

            if (!lootOwners.TryGetValue(instance, out IWorldEntity owner))
                return true;

            return owner.Guid == instance.Guid && ReferenceEquals(owner.Map, map);
        }

        private bool IsLootInRange(IPlayer looter, LootInstance instance)
        {
            if (!lootMaps.TryGetValue(instance, out IBaseMap map)
                || !ReferenceEquals(looter.Map, map))
                return false;

            Vector3 lootPosition = instance.Position;
            if (lootOwners.TryGetValue(instance, out IWorldEntity owner))
                lootPosition = owner.Position;

            return Vector3.Distance(looter.Position, lootPosition) <= LootRange;
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

                if (!LootGroup.TryValidateModel(model, out string groupValidationError))
                    throw new DatabaseDataException($"Loot group {model.Id} {groupValidationError}");

                model.Parent     = null;
                model.ChildGroup = new HashSet<LootGroupModel>();
                model.Item     ??= new HashSet<LootItemModel>();

                foreach (LootItemModel item in model.Item)
                {
                    if (item == null)
                        throw new DatabaseDataException($"Loot group {model.Id} contains a null item record.");

                    if (!LootItem.TryValidateModel(item, out string itemValidationError))
                    {
                        throw new DatabaseDataException(
                            $"Loot item {item.Id} in group {model.Id} {itemValidationError}");
                    }

                    var itemType = (LootItemType)item.Type;
                    if (!LootInstanceItem.CanDeliver(itemType))
                        throw new DatabaseDataException(
                            $"Loot item {item.Id} in group {model.Id} uses unsupported type {item.Type}.");
                }
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

        private enum LootRemovalReason
        {
            Completed,
            TimedOut,
            StaleSource
        }
    }
}
