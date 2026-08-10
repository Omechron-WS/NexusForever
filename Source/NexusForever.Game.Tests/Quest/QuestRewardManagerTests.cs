using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Achievement;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Abstract.Reputation;
using NexusForever.Game.Entity;
using NexusForever.Game.Quest;
using NexusForever.Game.Static;
using NexusForever.Game.Static.Achievement;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Game.Static.Reputation;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Static;

namespace NexusForever.Game.Tests.Quest
{
    public sealed class QuestRewardManagerTests
    {
        private const ushort QuestId = 100;

        [Fact]
        public void TryCreatePlan_InvalidSelectedRewardGrantsNothing()
        {
            RewardFixture fixture = CreateFixture();
            IQuestInfo info = CreateQuestInfo(
                CreateReward(10u, QuestRewardType.Item, 1u, 1u, 1u));

            bool result = fixture.Manager.TryCreatePlan(info, 99, out QuestRewardPlan plan);

            Assert.False(result);
            Assert.Null(plan);
            VerifyNoRewardMutation(fixture);
        }

        [Fact]
        public void TryCreatePlan_AutomaticRewardCannotAlsoBeSelected()
        {
            RewardFixture fixture = CreateFixture();
            IQuestInfo info = CreateQuestInfo(
                CreateReward(10u, QuestRewardType.Item, 1u, 1u, 0u));

            bool result = fixture.Manager.TryCreatePlan(info, 10, out QuestRewardPlan plan);

            Assert.False(result);
            Assert.Null(plan);
            VerifyNoRewardMutation(fixture);
        }

        [Fact]
        public void TryCreatePlan_ChoiceQuestRequiresExactlyOneChoice()
        {
            RewardFixture fixture = CreateFixture();
            IQuestInfo info = CreateQuestInfo(
                CreateReward(10u, QuestRewardType.Item, 1u, 1u, 1u));

            Assert.False(fixture.Manager.TryCreatePlan(info, 0, out QuestRewardPlan plan));
            Assert.Null(plan);
            VerifyNoRewardMutation(fixture);
        }

        [Fact]
        public void TryCreatePlan_DistinctItemRewardsUseAggregateSlotCapacity()
        {
            Mock<IItemInfo> first = CreateItemInfo(1u, 1u, false);
            Mock<IItemInfo> second = CreateItemInfo(2u, 1u, false);
            Mock<IBag> bag = CreateBag(1u, 1u);
            RewardFixture fixture = CreateFixture(bag.Object, first.Object, second.Object);
            IQuestInfo info = CreateQuestInfo(
                CreateReward(10u, QuestRewardType.Item, 1u, 1u, 0u),
                CreateReward(11u, QuestRewardType.Item, 2u, 1u, 0u));

            bool result = fixture.Manager.TryCreatePlan(info, 0, out QuestRewardPlan plan);

            Assert.False(result);
            Assert.Null(plan);
            VerifyNoRewardMutation(fixture);
        }

        [Fact]
        public void TryCreatePlan_IdenticalItemRewardsUseAggregateStackCapacity()
        {
            Mock<IItemInfo> itemInfo = CreateItemInfo(1u, 10u, true);
            IItem existing = CreateItem(1u, 8u);
            Mock<IBag> bag = CreateBag(1u, 0u, existing);
            RewardFixture fixture = CreateFixture(bag.Object, itemInfo.Object);
            IQuestInfo info = CreateQuestInfo(
                CreateReward(10u, QuestRewardType.Item, 1u, 2u, 0u),
                CreateReward(11u, QuestRewardType.Item, 1u, 2u, 0u));

            bool result = fixture.Manager.TryCreatePlan(info, 0, out QuestRewardPlan plan);

            Assert.False(result);
            Assert.Null(plan);
            VerifyNoRewardMutation(fixture);
        }

        [Fact]
        public void TryCreatePlan_FullBagUsesSlotFreedByPushedItemReclamation()
        {
            const uint pushedItemId = 50u;
            Mock<IItemInfo> rewardItem = CreateItemInfo(1u, 1u, false);
            IItem pushedItem = CreateItem(pushedItemId, 1u);
            Mock<IBag> bag = CreateBag(1u, 0u, pushedItem);
            RewardFixture fixture = CreateFixture(bag.Object, rewardItem.Object);
            IQuestInfo info = CreateQuestInfo(
                new Quest2Entry
                {
                    Id = QuestId,
                    PushedItemIds = [pushedItemId],
                    PushedItemCounts = [1u]
                },
                CreateReward(10u, QuestRewardType.Item, 1u, 1u, 0u));

            bool result = fixture.Manager.TryCreatePlan(info, 0, out QuestRewardPlan plan);

            Assert.True(result);
            Assert.NotNull(plan);
            Assert.Equal(1u, plan.RemovedItems[pushedItemId]);
            Assert.Equal(1u, Assert.Single(plan.Items).Amount);
            VerifyNoRewardMutation(fixture);
        }

        [Fact]
        public void TryCreatePlan_UnsupportedAutomaticRewardRejectsWholeSet()
        {
            RewardFixture fixture = CreateFixture();
            IQuestInfo info = CreateQuestInfo(
                CreateReward(10u, QuestRewardType.AccountCurrency, 1u, 1u, 0u));

            bool result = fixture.Manager.TryCreatePlan(info, 0, out QuestRewardPlan plan);

            Assert.False(result);
            Assert.Null(plan);
            VerifyNoRewardMutation(fixture);
        }

        [Fact]
        public void TryCreatePlan_ItemEligibilityUsesVerifiedItemRequirements()
        {
            Mock<IItemInfo> itemInfo = CreateItemInfo(1u, 1u, false, requiredClass: Class.Esper);
            RewardFixture fixture = CreateFixture(CreateBag(1u, 1u).Object, itemInfo.Object);
            IQuestInfo info = CreateQuestInfo(
                CreateReward(10u, QuestRewardType.Item, 1u, 1u, 1u));

            bool result = fixture.Manager.TryCreatePlan(info, 10, out QuestRewardPlan plan);

            Assert.False(result);
            Assert.Null(plan);
            VerifyNoRewardMutation(fixture);
        }

        [Fact]
        public void TryCreatePlan_ReputationOverflowIsRejectedBeforeMutation()
        {
            Faction factionId = (Faction)500u;
            RewardFixture fixture = CreateFixture();
            fixture.FactionManager.Setup(manager => manager.GetFaction(factionId)).Returns(Mock.Of<IFactionNode>());
            fixture.ReputationManager.Setup(manager => manager.GetReputation(factionId)).Returns(Mock.Of<IReputation>(
                reputation => reputation.Amount == float.MaxValue));
            IQuestInfo info = CreateQuestInfo(
                ImmutableDictionary<Faction, float>.Empty.Add(factionId, float.MaxValue));

            bool result = fixture.Manager.TryCreatePlan(info, 0, out QuestRewardPlan plan);

            Assert.False(result);
            Assert.Null(plan);
            VerifyNoRewardMutation(fixture);
        }

        [Fact]
        public void TryCreatePlan_AggregatesAllSupportedCharacterLocalRewards()
        {
            Faction factionId = (Faction)500u;
            Mock<IItemInfo> itemInfo = CreateItemInfo(1u, 1u, false);
            GameTable<CurrencyTypeEntry> currencyTable = CreateGameTable(new CurrencyTypeEntry
            {
                Id = (uint)CurrencyType.Credits,
                CapAmount = 100ul
            });
            var gameTableManager = new Mock<IGameTableManager>();
            gameTableManager.SetupGet(manager => manager.CurrencyType).Returns(currencyTable);
            RewardFixture fixture = CreateFixture(
                CreateBag(1u, 1u).Object,
                itemInfo.Object,
                gameTableManager: gameTableManager.Object);
            fixture.FactionManager.Setup(manager => manager.GetFaction(factionId)).Returns(Mock.Of<IFactionNode>());
            fixture.ReputationManager.Setup(manager => manager.GetReputation(factionId)).Returns((IReputation)null);
            IQuestInfo info = CreateQuestInfo(
                new Quest2Entry
                {
                    Id = QuestId
                },
                [
                    CreateReward(10u, QuestRewardType.Item, 1u, 1u, 0u),
                    CreateReward(11u, QuestRewardType.Money, (uint)CurrencyType.Credits, 2u, 0u),
                    CreateReward(12u, QuestRewardType.Reputation, (uint)factionId, 4u, 0u)
                ],
                ImmutableDictionary<Faction, float>.Empty.Add(factionId, 3f),
                0u,
                3u);

            bool result = fixture.Manager.TryCreatePlan(info, 0, out QuestRewardPlan plan);

            Assert.True(result);
            Assert.Equal(1u, Assert.Single(plan.Items).Amount);
            Assert.Equal(5ul, Assert.Single(plan.Currencies).Amount);
            Assert.Equal(7f, Assert.Single(plan.Reputations).Amount);
            VerifyNoRewardMutation(fixture);
        }

        [Fact]
        public void TryCreatePlan_ExperienceCanReachLevelCapWithoutLevel51Data()
        {
            GameTable<XpPerLevelEntry> xpTable = CreateGameTable(new XpPerLevelEntry
            {
                Id = 50u,
                MinXpForLevel = 100u
            });
            var gameTableManager = new Mock<IGameTableManager>();
            gameTableManager.SetupGet(manager => manager.XpPerLevel).Returns(xpTable);
            RewardFixture fixture = CreateFixture(gameTableManager: gameTableManager.Object, level: 49u);
            fixture.XpManager.SetupGet(manager => manager.TotalXp).Returns(90u);
            IQuestInfo info = CreateQuestInfo(experience: 10u);

            bool result = fixture.Manager.TryCreatePlan(info, 0, out QuestRewardPlan plan);

            Assert.True(result);
            Assert.NotNull(plan);
            Assert.Equal(10u, plan.Experience);
            VerifyNoRewardMutation(fixture);
        }

        [Fact]
        public void TryApply_CapacityDriftDoesNotReachOtherRewardDomains()
        {
            Mock<IItemInfo> itemInfo = CreateItemInfo(1u, 1u, false);
            RewardFixture fixture = CreateFixture(CreateBag(1u, 1u).Object, itemInfo.Object);
            fixture.Inventory
                .Setup(inventory => inventory.TryItemExchange(
                    It.IsAny<IEnumerable<KeyValuePair<uint, uint>>>(),
                    It.IsAny<IEnumerable<KeyValuePair<IItemInfo, uint>>>(),
                    ItemUpdateReason.Quest))
                .Returns(false);
            var plan = new QuestRewardPlan(
                [],
                [new QuestItemReward(itemInfo.Object, 1u)],
                [new QuestCurrencyReward(CurrencyType.Credits, 10ul)],
                [new QuestReputationReward((Faction)500u, 2f)],
                20u);

            bool result = fixture.Manager.TryApply(plan);

            Assert.False(result);
            fixture.CurrencyManager.Verify(
                manager => manager.CurrencyAddAmount(It.IsAny<CurrencyType>(), It.IsAny<ulong>(), It.IsAny<bool>()),
                Times.Never);
            fixture.ReputationManager.Verify(
                manager => manager.UpdateReputation(It.IsAny<Faction>(), It.IsAny<float>()),
                Times.Never);
            fixture.XpManager.Verify(
                manager => manager.GrantXp(It.IsAny<uint>(), It.IsAny<ExpReason>()),
                Times.Never);
        }

        [Fact]
        public void TryApply_AppliesPlanAfterOneAdmittedInventoryExchange()
        {
            Faction factionId = (Faction)500u;
            Mock<IItemInfo> itemInfo = CreateItemInfo(1u, 1u, false);
            RewardFixture fixture = CreateFixture(CreateBag(1u, 1u).Object, itemInfo.Object);
            fixture.Inventory
                .Setup(inventory => inventory.TryItemExchange(
                    It.IsAny<IEnumerable<KeyValuePair<uint, uint>>>(),
                    It.IsAny<IEnumerable<KeyValuePair<IItemInfo, uint>>>(),
                    ItemUpdateReason.Quest))
                .Returns(true);
            var plan = new QuestRewardPlan(
                [new KeyValuePair<uint, uint>(50u, 2u)],
                [new QuestItemReward(itemInfo.Object, 1u)],
                [new QuestCurrencyReward(CurrencyType.Credits, 10ul)],
                [new QuestReputationReward(factionId, 2f)],
                20u);

            bool result = fixture.Manager.TryApply(plan);

            Assert.True(result);
            fixture.Inventory.Verify(inventory => inventory.TryItemExchange(
                It.Is<IEnumerable<KeyValuePair<uint, uint>>>(removals => removals.Single().Value == 2u),
                It.Is<IEnumerable<KeyValuePair<IItemInfo, uint>>>(additions => additions.Single().Value == 1u),
                ItemUpdateReason.Quest), Times.Once);
            fixture.CurrencyManager.Verify(
                manager => manager.CurrencyAddAmount(CurrencyType.Credits, 10ul, false), Times.Once);
            fixture.ReputationManager.Verify(
                manager => manager.UpdateReputation(factionId, 2f), Times.Once);
            fixture.XpManager.Verify(
                manager => manager.GrantXp(20u, ExpReason.Quest), Times.Once);
        }

        private static RewardFixture CreateFixture(
            IBag inventoryBag = null,
            IItemInfo firstItem = null,
            IItemInfo secondItem = null,
            IGameTableManager gameTableManager = null,
            uint level = 50u)
        {
            var player = new Mock<IPlayer>();
            var inventory = new Mock<IInventory>();
            var currencyManager = new Mock<ICurrencyManager>();
            var reputationManager = new Mock<IReputationManager>();
            var xpManager = new Mock<IXpManager>();
            var itemManager = new Mock<IItemManager>();
            var factionManager = new Mock<IFactionManager>();
            var prerequisiteManager = new Mock<IPrerequisiteManager>();

            IBag[] bags = inventoryBag == null ? [] : [inventoryBag];
            inventory.Setup(inventory => inventory.GetEnumerator())
                .Returns(() => ((IEnumerable<IBag>)bags).GetEnumerator());
            currencyManager.Setup(manager => manager.GetEnumerator())
                .Returns(() => Enumerable.Empty<ICurrency>().GetEnumerator());

            if (firstItem != null)
                itemManager.Setup(manager => manager.GetItemInfo(firstItem.Id)).Returns(firstItem);
            if (secondItem != null)
                itemManager.Setup(manager => manager.GetItemInfo(secondItem.Id)).Returns(secondItem);

            player.SetupGet(value => value.CharacterId).Returns(42ul);
            player.SetupGet(value => value.Level).Returns(level);
            player.SetupGet(value => value.Class).Returns(Class.Warrior);
            player.SetupGet(value => value.Race).Returns(Race.Human);
            player.SetupGet(value => value.Faction1).Returns(Faction.Exile);
            player.SetupGet(value => value.Inventory).Returns(inventory.Object);
            player.SetupGet(value => value.CurrencyManager).Returns(currencyManager.Object);
            player.SetupGet(value => value.ReputationManager).Returns(reputationManager.Object);
            player.SetupGet(value => value.XpManager).Returns(xpManager.Object);
            player.Setup(value => value.GetItemProficiencies()).Returns(unchecked((ItemProficiency)uint.MaxValue));

            gameTableManager ??= Mock.Of<IGameTableManager>();
            var manager = new QuestRewardManager(
                player.Object,
                itemManager.Object,
                gameTableManager,
                factionManager.Object,
                prerequisiteManager.Object);

            return new RewardFixture(
                manager,
                player,
                inventory,
                currencyManager,
                reputationManager,
                xpManager,
                factionManager);
        }

        private static IQuestInfo CreateQuestInfo(params Quest2RewardEntry[] rewards)
        {
            return CreateQuestInfo(new Quest2Entry
            {
                Id = QuestId
            }, rewards);
        }

        private static IQuestInfo CreateQuestInfo(
            Quest2Entry entry,
            params Quest2RewardEntry[] rewards)
        {
            return CreateQuestInfo(entry, rewards, ImmutableDictionary<Faction, float>.Empty, 0u);
        }

        private static IQuestInfo CreateQuestInfo(
            ImmutableDictionary<Faction, float> reputation,
            uint experience = 0u)
        {
            return CreateQuestInfo(new Quest2Entry
            {
                Id = QuestId
            }, [], reputation, experience);
        }

        private static IQuestInfo CreateQuestInfo(uint experience)
        {
            return CreateQuestInfo(ImmutableDictionary<Faction, float>.Empty, experience);
        }

        private static IQuestInfo CreateQuestInfo(
            Quest2Entry entry,
            IEnumerable<Quest2RewardEntry> rewards,
            ImmutableDictionary<Faction, float> reputation,
            uint experience,
            uint money = 0u)
        {
            var info = new Mock<IQuestInfo>();
            info.SetupGet(value => value.Entry).Returns(entry);
            info.SetupGet(value => value.Rewards).Returns(rewards.ToImmutableDictionary(reward => reward.Id));
            info.Setup(value => value.GetRewardMoney()).Returns(money);
            info.Setup(value => value.GetRewardExperience()).Returns(experience);
            info.Setup(value => value.GetRewardReputation()).Returns(reputation);
            return info.Object;
        }

        private static Quest2RewardEntry CreateReward(
            uint id,
            QuestRewardType type,
            uint objectId,
            uint amount,
            uint flags)
        {
            return new Quest2RewardEntry
            {
                Id = id,
                Quest2Id = QuestId,
                Quest2RewardTypeId = (uint)type,
                ObjectId = objectId,
                ObjectAmount = amount,
                Flags = flags
            };
        }

        private static Mock<IItemInfo> CreateItemInfo(
            uint itemId,
            uint maximumStack,
            bool stackable,
            Class requiredClass = Class.None)
        {
            var info = new Mock<IItemInfo>();
            info.SetupGet(value => value.Id).Returns(itemId);
            info.SetupGet(value => value.Entry).Returns(new Item2Entry
            {
                Id = itemId,
                MaxStackCount = maximumStack,
                ClassRequired = (uint)requiredClass
            });
            info.SetupGet(value => value.CategoryEntry).Returns(new Item2CategoryEntry());
            info.Setup(value => value.IsStackable()).Returns(stackable);
            return info;
        }

        private static IItem CreateItem(uint itemId, uint amount)
        {
            return Mock.Of<IItem>(item => item.Id == itemId && item.StackCount == amount);
        }

        private static Mock<IBag> CreateBag(uint slots, uint slotsRemaining, params IItem[] items)
        {
            var bag = new Mock<IBag>();
            bag.SetupGet(value => value.Location).Returns(InventoryLocation.Inventory);
            bag.SetupGet(value => value.Slots).Returns(slots);
            bag.SetupGet(value => value.SlotsRemaining).Returns(slotsRemaining);
            bag.Setup(value => value.GetEnumerator())
                .Returns(() => ((IEnumerable<IItem>)items).GetEnumerator());
            return bag;
        }

        private static GameTable<T> CreateGameTable<T>(params T[] entries) where T : class, new()
        {
            var table = (GameTable<T>)RuntimeHelpers.GetUninitializedObject(typeof(GameTable<T>));
            typeof(GameTable<T>).GetProperty(nameof(GameTable<T>.Entries))?.SetValue(table, entries);

            FieldInfo idField = typeof(T).GetFields().First();
            uint maximumId = entries.Select(entry => (uint)idField.GetValue(entry)).DefaultIfEmpty().Max();
            int[] lookup = Enumerable.Repeat(-1, checked((int)maximumId + 1)).ToArray();
            for (int i = 0; i < entries.Length; i++)
                lookup[(uint)idField.GetValue(entries[i])] = i;

            typeof(GameTable<T>).GetField("lookup", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(table, lookup);
            typeof(GameTable<T>).GetField("header", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(table,
                new GameTableHeader
                {
                    MaxId = maximumId + 1ul
                });
            return table;
        }

        private static void VerifyNoRewardMutation(RewardFixture fixture)
        {
            fixture.Inventory.Verify(
                inventory => inventory.TryItemExchange(
                    It.IsAny<IEnumerable<KeyValuePair<uint, uint>>>(),
                    It.IsAny<IEnumerable<KeyValuePair<IItemInfo, uint>>>(),
                    It.IsAny<ItemUpdateReason>()),
                Times.Never);
            fixture.CurrencyManager.Verify(
                manager => manager.CurrencyAddAmount(It.IsAny<CurrencyType>(), It.IsAny<ulong>(), It.IsAny<bool>()),
                Times.Never);
            fixture.ReputationManager.Verify(
                manager => manager.UpdateReputation(It.IsAny<Faction>(), It.IsAny<float>()),
                Times.Never);
            fixture.XpManager.Verify(
                manager => manager.GrantXp(It.IsAny<uint>(), It.IsAny<ExpReason>()),
                Times.Never);
        }

        private sealed record RewardFixture(
            QuestRewardManager Manager,
            Mock<IPlayer> Player,
            Mock<IInventory> Inventory,
            Mock<ICurrencyManager> CurrencyManager,
            Mock<IReputationManager> ReputationManager,
            Mock<IXpManager> XpManager,
            Mock<IFactionManager> FactionManager);
    }

    public sealed class QuestCompletionRewardTests
    {
        private const ushort QuestId = 100;

        [Fact]
        public void QuestComplete_RejectedPlanKeepsAchievedQuestAndPushedItems()
        {
            CompletionFixture fixture = CreateFixture();
            fixture.RewardManager.PrepareResult = false;

            fixture.Manager.QuestComplete(QuestId, 99, true);

            Assert.Equal(QuestState.Achieved, fixture.Quest.Object.State);
            Assert.Same(fixture.Quest.Object, Assert.Single(fixture.Manager.GetActiveQuests()));
            fixture.Inventory.Verify(
                inventory => inventory.ItemDelete(It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<ItemUpdateReason>()),
                Times.Never);
            Assert.Equal(0, fixture.RewardManager.ApplyCount);
        }

        [Fact]
        public void QuestComplete_InventoryDriftKeepsAchievedQuestWithoutCompletion()
        {
            CompletionFixture fixture = CreateFixture();
            fixture.RewardManager.ApplyResult = false;

            fixture.Manager.QuestComplete(QuestId, 0, true);

            Assert.Equal(QuestState.Achieved, fixture.Quest.Object.State);
            Assert.Same(fixture.Quest.Object, Assert.Single(fixture.Manager.GetActiveQuests()));
            Assert.Equal(1, fixture.RewardManager.ApplyCount);
            fixture.AchievementManager.Verify(
                manager => manager.CheckAchievements(
                    It.IsAny<IPlayer>(),
                    It.IsAny<AchievementType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Never);
        }

        [Fact]
        public void QuestComplete_SuccessfulRepeatPacketDoesNotReapplyRewards()
        {
            CompletionFixture fixture = CreateFixture();

            fixture.Manager.QuestComplete(QuestId, 0, true);

            Assert.Throws<QuestException>(() => fixture.Manager.QuestComplete(QuestId, 0, true));
            Assert.Equal(QuestState.Completed, fixture.Quest.Object.State);
            Assert.Empty(fixture.Manager.GetActiveQuests());
            Assert.Equal(1, fixture.RewardManager.PrepareCount);
            Assert.Equal(1, fixture.RewardManager.ApplyCount);
            fixture.Quest.VerifySet(quest => quest.State = QuestState.Completed, Times.Once);
            fixture.AchievementManager.Verify(
                manager => manager.CheckAchievements(
                    fixture.Player.Object,
                    AchievementType.QuestComplete,
                    QuestId,
                    0u,
                    1u),
                Times.Once);
        }

        [Fact]
        public void QuestComplete_ReentrantPacketCannotEnterRewardApplicationTwice()
        {
            CompletionFixture fixture = CreateFixture();
            fixture.RewardManager.ApplyAction = () => Assert.Throws<QuestException>(
                () => fixture.Manager.QuestComplete(QuestId, 0, true));

            fixture.Manager.QuestComplete(QuestId, 0, true);

            Assert.Equal(QuestState.Completed, fixture.Quest.Object.State);
            Assert.Equal(1, fixture.RewardManager.PrepareCount);
            Assert.Equal(1, fixture.RewardManager.ApplyCount);
            fixture.Quest.VerifySet(quest => quest.State = QuestState.Completed, Times.Once);
        }

        [Fact]
        public void QuestComplete_ReentrantPacketCannotPrepareRewardsTwice()
        {
            CompletionFixture fixture = CreateFixture();
            fixture.RewardManager.PrepareAction = () => Assert.Throws<QuestException>(
                () => fixture.Manager.QuestComplete(QuestId, 0, true));

            fixture.Manager.QuestComplete(QuestId, 0, true);

            Assert.Equal(QuestState.Completed, fixture.Quest.Object.State);
            Assert.Equal(1, fixture.RewardManager.PrepareCount);
            Assert.Equal(1, fixture.RewardManager.ApplyCount);
            fixture.Quest.VerifySet(quest => quest.State = QuestState.Completed, Times.Once);
        }

        [Fact]
        public void QuestComplete_UnexpectedApplyExceptionCannotBeRetriedInSession()
        {
            CompletionFixture fixture = CreateFixture();
            fixture.RewardManager.ApplyAction = () => throw new InvalidOperationException();

            Assert.Throws<InvalidOperationException>(() => fixture.Manager.QuestComplete(QuestId, 0, true));
            Assert.Throws<QuestException>(() => fixture.Manager.QuestComplete(QuestId, 0, true));

            Assert.Equal(QuestState.Achieved, fixture.Quest.Object.State);
            Assert.Equal(1, fixture.RewardManager.PrepareCount);
            Assert.Equal(1, fixture.RewardManager.ApplyCount);
        }

        private static CompletionFixture CreateFixture()
        {
            var inventory = new Mock<IInventory>();
            var achievementManager = new Mock<ICharacterAchievementManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(42ul);
            player.SetupGet(value => value.Inventory).Returns(inventory.Object);
            player.SetupGet(value => value.AchievementManager).Returns(achievementManager.Object);

            Quest2Entry entry = new()
            {
                Id = QuestId,
                PushedItemIds = [],
                PushedItemCounts = []
            };
            var info = new Mock<IQuestInfo>();
            info.SetupGet(value => value.Entry).Returns(entry);
            info.Setup(value => value.IsCommunicatorReceived()).Returns(true);

            var quest = new Mock<IQuest>();
            quest.SetupGet(value => value.Id).Returns(QuestId);
            quest.SetupGet(value => value.Info).Returns(info.Object);
            quest.SetupProperty(value => value.State, QuestState.Achieved);

            var globalQuestManager = new Mock<IGlobalQuestManager>();
            globalQuestManager.Setup(manager => manager.GetQuestInfo(QuestId)).Returns(info.Object);
            var disableManager = new Mock<IDisableManager>();
            disableManager.Setup(manager => manager.IsDisabled(DisableType.Quest, QuestId)).Returns(false);
            var rewardManager = new TestQuestRewardManager();

            var manager = new QuestManager(
                player.Object,
                new CharacterModel
                {
                    Id = 42ul
                },
                globalQuestManager.Object,
                rewardManager,
                disableManager.Object);
            FieldInfo field = typeof(QuestManager).GetField("activeQuests", BindingFlags.Instance | BindingFlags.NonPublic);
            var activeQuests = (Dictionary<ushort, IQuest>)field.GetValue(manager);
            activeQuests.Add(QuestId, quest.Object);

            return new CompletionFixture(
                manager,
                player,
                inventory,
                achievementManager,
                quest,
                rewardManager);
        }

        private sealed class TestQuestRewardManager : IQuestRewardManager
        {
            private readonly QuestRewardPlan plan = new([], [], [], [], 0u);

            public bool PrepareResult { get; set; } = true;
            public bool ApplyResult { get; set; } = true;
            public Action PrepareAction { get; set; }
            public Action ApplyAction { get; set; }
            public int PrepareCount { get; private set; }
            public int ApplyCount { get; private set; }

            public bool TryCreatePlan(IQuestInfo info, ushort selectedRewardId, out QuestRewardPlan rewardPlan)
            {
                PrepareCount++;
                PrepareAction?.Invoke();
                rewardPlan = PrepareResult ? plan : null;
                return PrepareResult;
            }

            public bool TryApply(QuestRewardPlan rewardPlan)
            {
                ApplyCount++;
                ApplyAction?.Invoke();
                return ApplyResult;
            }
        }

        private sealed record CompletionFixture(
            QuestManager Manager,
            Mock<IPlayer> Player,
            Mock<IInventory> Inventory,
            Mock<ICharacterAchievementManager> AchievementManager,
            Mock<IQuest> Quest,
            TestQuestRewardManager RewardManager);
    }
}
