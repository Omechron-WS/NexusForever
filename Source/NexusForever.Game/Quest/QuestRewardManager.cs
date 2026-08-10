using System.Collections.Immutable;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Abstract.Reputation;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Game.Static.Reputation;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Static;
using NLog;

namespace NexusForever.Game.Quest
{
    internal interface IQuestRewardManager
    {
        /// <summary>
        /// Prevalidate and aggregate the complete reward set for a quest completion request.
        /// </summary>
        bool TryCreatePlan(IQuestInfo info, ushort selectedRewardId, out QuestRewardPlan plan);

        /// <summary>
        /// Apply a prevalidated reward plan after one inventory exchange admission.
        /// </summary>
        /// <remarks>
        /// A <see langword="false"/> result occurs only when the inventory exchange detects capacity drift before mutation.
        /// Unexpected runtime exceptions are allowed to propagate so a partially applied plan is never made retryable in-session.
        /// </remarks>
        bool TryApply(QuestRewardPlan plan);
    }

    internal sealed class QuestRewardPlan
    {
        public ImmutableDictionary<uint, uint> RemovedItems { get; }
        public ImmutableArray<QuestItemReward> Items { get; }
        public ImmutableArray<QuestCurrencyReward> Currencies { get; }
        public ImmutableArray<QuestReputationReward> Reputations { get; }
        public uint Experience { get; }

        internal QuestRewardPlan(
            IEnumerable<KeyValuePair<uint, uint>> removedItems,
            IEnumerable<QuestItemReward> items,
            IEnumerable<QuestCurrencyReward> currencies,
            IEnumerable<QuestReputationReward> reputations,
            uint experience)
        {
            RemovedItems = removedItems.ToImmutableDictionary();
            Items = items.ToImmutableArray();
            Currencies = currencies.ToImmutableArray();
            Reputations = reputations.ToImmutableArray();
            Experience = experience;
        }
    }

    internal readonly record struct QuestItemReward(IItemInfo Info, uint Amount);
    internal readonly record struct QuestCurrencyReward(CurrencyType CurrencyId, ulong Amount);
    internal readonly record struct QuestReputationReward(Faction FactionId, float Amount);

    internal sealed class QuestRewardManager : IQuestRewardManager
    {
        private const uint ChoiceRewardFlag = 0x01u;
        private const uint KnownRewardFlags = 0x03u;
        private const uint MaximumLevel = 50u;

        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly IPlayer player;
        private readonly IItemManager itemManager;
        private readonly IGameTableManager gameTableManager;
        private readonly IFactionManager factionManager;
        private readonly IPrerequisiteManager prerequisiteManager;

        internal QuestRewardManager(
            IPlayer player,
            IItemManager itemManager,
            IGameTableManager gameTableManager,
            IFactionManager factionManager,
            IPrerequisiteManager prerequisiteManager)
        {
            this.player = player;
            this.itemManager = itemManager;
            this.gameTableManager = gameTableManager;
            this.factionManager = factionManager;
            this.prerequisiteManager = prerequisiteManager;
        }

        /// <inheritdoc />
        public bool TryCreatePlan(IQuestInfo info, ushort selectedRewardId, out QuestRewardPlan plan)
        {
            plan = null;

            try
            {
                return TryCreatePlanInternal(info, selectedRewardId, out plan);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to prepare rewards for quest {info?.Entry?.Id ?? 0u}.");
                return false;
            }
        }

        private bool TryCreatePlanInternal(IQuestInfo info, ushort selectedRewardId, out QuestRewardPlan plan)
        {
            plan = null;
            if (info?.Entry == null || info.Rewards == null)
                return false;

            Quest2RewardEntry[] choiceRewards = info.Rewards.Values
                .Where(entry => (entry.Flags & ChoiceRewardFlag) != 0u)
                .ToArray();

            Quest2RewardEntry selectedReward = null;
            if (choiceRewards.Length == 0)
            {
                if (selectedRewardId != 0u)
                    return false;
            }
            else
            {
                if (selectedRewardId == 0u
                    || !info.Rewards.TryGetValue(selectedRewardId, out selectedReward)
                    || (selectedReward.Flags & ChoiceRewardFlag) == 0u)
                    return false;
            }

            var itemAmounts = new Dictionary<uint, (IItemInfo Info, ulong Amount)>();
            var currencyAmounts = new Dictionary<CurrencyType, ulong>();
            var reputationAmounts = new Dictionary<Faction, double>();
            if (!TryGetPushedItemAmounts(info.Entry, out Dictionary<uint, uint> removedItemAmounts))
                return false;

            IEnumerable<Quest2RewardEntry> rewards = info.Rewards.Values
                .Where(entry => (entry.Flags & ChoiceRewardFlag) == 0u);
            if (selectedReward != null)
                rewards = rewards.Append(selectedReward);

            foreach (Quest2RewardEntry reward in rewards)
            {
                if ((reward.Flags & ~KnownRewardFlags) != 0u
                    || !TryAddReward(reward, itemAmounts, currencyAmounts, reputationAmounts))
                    return false;
            }

            uint money = info.GetRewardMoney();
            if (money != 0u && !TryAddCurrency(currencyAmounts, CurrencyType.Credits, money))
                return false;

            ImmutableDictionary<Faction, float> fixedReputation = info.GetRewardReputation();
            if (fixedReputation == null)
                return false;

            foreach ((Faction factionId, float amount) in fixedReputation)
            {
                if (!TryAddReputation(reputationAmounts, factionId, amount))
                    return false;
            }

            uint experience = info.GetRewardExperience();
            if (!CanGrantExperience(experience)
                || !HasCurrencyCapacity(currencyAmounts)
                || !HasReputationData(reputationAmounts)
                || !HasInventoryCapacity(removedItemAmounts, itemAmounts))
                return false;

            plan = new QuestRewardPlan(
                removedItemAmounts.OrderBy(pair => pair.Key),
                itemAmounts
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new QuestItemReward(pair.Value.Info, (uint)pair.Value.Amount)),
                currencyAmounts
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new QuestCurrencyReward(pair.Key, pair.Value)),
                reputationAmounts
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new QuestReputationReward(pair.Key, (float)pair.Value)),
                experience);
            return true;
        }

        private static bool TryGetPushedItemAmounts(
            Quest2Entry questEntry,
            out Dictionary<uint, uint> removedItemAmounts)
        {
            removedItemAmounts = new Dictionary<uint, uint>();
            uint[] pushedItemIds = questEntry.PushedItemIds ?? Array.Empty<uint>();
            uint[] pushedItemCounts = questEntry.PushedItemCounts ?? Array.Empty<uint>();
            if (pushedItemIds.Length != pushedItemCounts.Length)
                return false;

            for (int i = 0; i < pushedItemIds.Length; i++)
            {
                uint itemId = pushedItemIds[i];
                uint amount = pushedItemCounts[i];
                if (itemId == 0u || amount == 0u)
                    continue;

                removedItemAmounts.TryGetValue(itemId, out uint current);
                ulong total = (ulong)current + amount;
                if (total > uint.MaxValue)
                    return false;

                removedItemAmounts[itemId] = (uint)total;
            }

            return true;
        }

        private bool TryAddReward(
            Quest2RewardEntry reward,
            Dictionary<uint, (IItemInfo Info, ulong Amount)> itemAmounts,
            Dictionary<CurrencyType, ulong> currencyAmounts,
            Dictionary<Faction, double> reputationAmounts)
        {
            if (reward.ObjectAmount == 0u)
                return false;

            switch ((QuestRewardType)reward.Quest2RewardTypeId)
            {
                case QuestRewardType.Item:
                {
                    IItemInfo itemInfo = itemManager.GetItemInfo(reward.ObjectId);
                    if (itemInfo == null || !MeetsItemRequirements(itemInfo))
                        return false;

                    ulong amount = reward.ObjectAmount;
                    if (itemAmounts.TryGetValue(itemInfo.Id, out (IItemInfo Info, ulong Amount) current))
                    {
                        if (ulong.MaxValue - current.Amount < amount)
                            return false;

                        amount += current.Amount;
                    }

                    if (amount > uint.MaxValue)
                        return false;

                    itemAmounts[itemInfo.Id] = (itemInfo, amount);
                    return true;
                }
                case QuestRewardType.Reputation:
                    return TryAddReputation(reputationAmounts, (Faction)reward.ObjectId, reward.ObjectAmount);
                case QuestRewardType.Money:
                    return TryAddCurrency(currencyAmounts, (CurrencyType)reward.ObjectId, reward.ObjectAmount);
                case QuestRewardType.TradeSkillXp:
                case QuestRewardType.TradeSkill:
                case QuestRewardType.AccountItem:
                case QuestRewardType.AccountCurrency:
                case QuestRewardType.AccountGenericUnlock:
                case QuestRewardType.GenericUnlock:
                case QuestRewardType.RotationEssence:
                default:
                    log.Warn($"Quest {reward.Quest2Id} has unsupported reward type {reward.Quest2RewardTypeId}.");
                    return false;
            }
        }

        private bool MeetsItemRequirements(IItemInfo itemInfo)
        {
            Item2Entry entry = itemInfo.Entry;
            if (entry == null)
                return false;

            if (entry.ClassRequired != 0u && entry.ClassRequired != (uint)player.Class)
                return false;
            if (entry.RaceRequired != 0u && entry.RaceRequired != (uint)player.Race)
                return false;
            if (entry.Faction2IdRequired != 0u && entry.Faction2IdRequired != (uint)player.Faction1)
                return false;
            if (entry.PrerequisiteId != 0u && !prerequisiteManager.Meets(player, entry.PrerequisiteId))
                return false;

            uint proficiency = itemInfo.CategoryEntry?.ItemProficiencyId ?? 0u;
            return proficiency == 0u || ((uint)player.GetItemProficiencies() & proficiency) != 0u;
        }

        private static bool TryAddCurrency(
            Dictionary<CurrencyType, ulong> currencyAmounts,
            CurrencyType currencyId,
            ulong amount)
        {
            if (amount == 0ul)
                return true;

            currencyAmounts.TryGetValue(currencyId, out ulong current);
            if (ulong.MaxValue - current < amount)
                return false;

            currencyAmounts[currencyId] = current + amount;
            return true;
        }

        private static bool TryAddReputation(
            Dictionary<Faction, double> reputationAmounts,
            Faction factionId,
            double amount)
        {
            if (!double.IsFinite(amount) || amount <= 0d)
                return false;

            reputationAmounts.TryGetValue(factionId, out double current);
            double total = current + amount;
            if (!double.IsFinite(total) || total > float.MaxValue)
                return false;

            reputationAmounts[factionId] = total;
            return true;
        }

        private bool CanGrantExperience(uint experience)
        {
            if (experience == 0u || player.Level >= MaximumLevel)
                return true;

            uint signatureExperience = player.SignatureEnabled ? (uint)(experience * 0.25f) : 0u;
            ulong totalExperience = (ulong)player.XpManager.TotalXp + experience + signatureExperience;
            if (totalExperience > uint.MaxValue)
                return false;

            uint level = player.Level;
            XpPerLevelEntry nextLevel = gameTableManager.XpPerLevel.GetEntry(level + 1u);
            if (nextLevel == null)
                return false;

            while (totalExperience >= nextLevel.MinXpForLevel && level < MaximumLevel)
            {
                level++;
                if (level >= MaximumLevel)
                    break;

                nextLevel = gameTableManager.XpPerLevel.GetEntry(level + 1u);
                if (nextLevel == null)
                    return false;
            }

            return true;
        }

        private bool HasCurrencyCapacity(Dictionary<CurrencyType, ulong> currencyAmounts)
        {
            Dictionary<CurrencyType, ulong> currentAmounts = player.CurrencyManager
                .ToDictionary(currency => currency.Id, currency => currency.Amount);

            foreach ((CurrencyType currencyId, ulong amount) in currencyAmounts)
            {
                CurrencyTypeEntry entry = gameTableManager.CurrencyType.GetEntry((ulong)currencyId);
                if (entry == null)
                    return false;

                currentAmounts.TryGetValue(currencyId, out ulong current);
                if (ulong.MaxValue - current < amount)
                    return false;

                ulong total = current + amount;
                if (entry.CapAmount > 0ul && total > entry.CapAmount)
                    return false;
            }

            return true;
        }

        private bool HasReputationData(Dictionary<Faction, double> reputationAmounts)
        {
            foreach ((Faction factionId, double amount) in reputationAmounts)
            {
                if (factionManager.GetFaction(factionId) == null
                    || !double.IsFinite(amount)
                    || amount <= 0d
                    || amount > float.MaxValue)
                    return false;

                IReputation current = player.ReputationManager.GetReputation(factionId);
                if (current != null)
                {
                    double total = current.Amount + amount;
                    if (!float.IsFinite(current.Amount)
                        || !double.IsFinite(total)
                        || total > float.MaxValue)
                        return false;
                }
            }

            return true;
        }

        private bool HasInventoryCapacity(
            IReadOnlyDictionary<uint, uint> removedItemAmounts,
            Dictionary<uint, (IItemInfo Info, ulong Amount)> itemAmounts)
        {
            if (removedItemAmounts.Count == 0 && itemAmounts.Count == 0)
                return true;

            IBag bag = player.Inventory.SingleOrDefault(candidate => candidate.Location == InventoryLocation.Inventory);
            if (bag == null || bag.SlotsRemaining > bag.Slots)
                return false;

            var stacks = bag
                .Select(item => new SimulatedItemStack(item.Id, item.StackCount))
                .ToList();
            if ((ulong)stacks.Count > bag.Slots
                || bag.SlotsRemaining != bag.Slots - (uint)stacks.Count)
                return false;

            ulong slotsRemaining = bag.SlotsRemaining;

            foreach ((uint itemId, uint removalAmount) in removedItemAmounts)
            {
                uint count = removalAmount;

                foreach (SimulatedItemStack stack in stacks.Where(stack => stack.ItemId == itemId && stack.Amount != 0u))
                {
                    uint removed = Math.Min(count, stack.Amount);
                    stack.Amount -= removed;
                    count -= removed;

                    if (stack.Amount == 0u)
                        slotsRemaining++;
                    if (count == 0u)
                        break;
                }
            }

            if (slotsRemaining > bag.Slots)
                return false;

            foreach ((uint itemId, (IItemInfo itemInfo, ulong amount)) in itemAmounts)
            {
                uint maximumStack = itemInfo.IsStackable() ? itemInfo.Entry.MaxStackCount : 1u;
                if (maximumStack == 0u)
                    return false;

                ulong remaining = amount;
                if (itemInfo.IsStackable())
                {
                    foreach (SimulatedItemStack stack in stacks.Where(stack => stack.ItemId == itemId && stack.Amount != 0u))
                    {
                        if (stack.Amount > maximumStack)
                            return false;

                        uint available = maximumStack - stack.Amount;
                        uint added = (uint)Math.Min(remaining, available);
                        stack.Amount += added;
                        remaining -= added;

                        if (remaining == 0ul)
                            break;
                    }
                }

                ulong requiredSlots = (remaining + maximumStack - 1u) / maximumStack;
                if (requiredSlots > slotsRemaining)
                    return false;

                slotsRemaining -= requiredSlots;
            }

            return true;
        }

        /// <inheritdoc />
        public bool TryApply(QuestRewardPlan plan)
        {
            if (plan == null)
                return false;

            if (!player.Inventory.TryItemExchange(
                plan.RemovedItems,
                plan.Items.Select(reward => new KeyValuePair<IItemInfo, uint>(reward.Info, reward.Amount)),
                ItemUpdateReason.Quest))
                return false;

            foreach (QuestCurrencyReward reward in plan.Currencies)
                player.CurrencyManager.CurrencyAddAmount(reward.CurrencyId, reward.Amount);

            foreach (QuestReputationReward reward in plan.Reputations)
                player.ReputationManager.UpdateReputation(reward.FactionId, reward.Amount);

            if (plan.Experience != 0u)
                player.XpManager.GrantXp(plan.Experience, ExpReason.Quest);

            return true;
        }

        private sealed class SimulatedItemStack
        {
            public uint ItemId { get; }
            public uint Amount { get; set; }

            public SimulatedItemStack(uint itemId, uint amount)
            {
                ItemId = itemId;
                Amount = amount;
            }
        }
    }
}
