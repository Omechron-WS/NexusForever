using System.Collections.Immutable;
using System.Reflection;
using Moq;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Entity;
using NexusForever.Game.Quest;
using NexusForever.Game.Static;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Game.Static.Reputation;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Static;

namespace NexusForever.Game.Tests.Quest
{
    public sealed class QuestSharingTests
    {
        private const ushort QuestId = 100;
        private const uint OwnerGuid = 20u;
        private const ulong OwnerCharacterId = 200ul;
        private const uint SharerGuid = 10u;
        private const ulong SharerCharacterId = 100ul;
        private const ulong GroupAssociation = 300ul;

        [Fact]
        public void QuestShare_ValidOwnedQuestOffersToVisibleGroupMember()
        {
            QuestSharingFixture fixture = CreateFixture();
            Mock<IQuest> ownedQuest = CreateQuest(QuestState.Accepted, canShare: true);
            GetQuestDictionary(fixture.Manager, "activeQuests").Add(QuestId, ownedQuest.Object);
            fixture.Owner.SetupGet(player => player.TargetGuid).Returns(SharerGuid);
            fixture.SharerQuestManager
                .Setup(manager => manager.OfferQuestShare(
                    QuestId,
                    OwnerGuid,
                    OwnerCharacterId,
                    GroupAssociation))
                .Returns(true);

            fixture.Manager.QuestShare(QuestId);

            fixture.SharerQuestManager.Verify(manager => manager.OfferQuestShare(
                QuestId,
                OwnerGuid,
                OwnerCharacterId,
                GroupAssociation), Times.Once);
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("not-shareable")]
        [InlineData("zero-enum")]
        [InlineData("disabled")]
        [InlineData("zero-group")]
        [InlineData("different-group")]
        [InlineData("one-way-visibility")]
        [InlineData("same-character")]
        public void QuestShare_InvalidOwnershipStateOrRelationshipDoesNotOffer(string condition)
        {
            QuestSharingFixture fixture = CreateFixture();
            Mock<IQuest> quest = CreateQuest(QuestState.Accepted, canShare: condition != "not-shareable");
            if (condition != "missing")
                GetQuestDictionary(fixture.Manager, "activeQuests").Add(QuestId, quest.Object);
            fixture.Owner.SetupGet(player => player.TargetGuid).Returns(SharerGuid);

            switch (condition)
            {
                case "disabled":
                    fixture.DisableManager
                        .Setup(manager => manager.IsDisabled(DisableType.Quest, QuestId))
                        .Returns(true);
                    break;
                case "zero-enum":
                    fixture.Entry.QuestShareEnum = 0u;
                    break;
                case "zero-group":
                    fixture.Owner.SetupGet(player => player.GroupAssociation).Returns(0ul);
                    break;
                case "different-group":
                    fixture.Sharer.SetupGet(player => player.GroupAssociation).Returns(GroupAssociation + 1ul);
                    break;
                case "one-way-visibility":
                    fixture.Sharer
                        .Setup(player => player.GetVisible<IPlayer>(OwnerGuid))
                        .Returns((IPlayer)null);
                    break;
                case "same-character":
                    fixture.Sharer.SetupGet(player => player.CharacterId).Returns(OwnerCharacterId);
                    break;
            }

            Assert.Throws<QuestException>(() => fixture.Manager.QuestShare(QuestId));
            fixture.SharerQuestManager.Verify(manager => manager.OfferQuestShare(
                It.IsAny<ushort>(),
                It.IsAny<uint>(),
                It.IsAny<ulong>(),
                It.IsAny<ulong>()), Times.Never);
        }

        [Fact]
        public void OfferQuestShare_EmitsExactBuild16042Packet()
        {
            QuestSharingFixture fixture = CreateFixture();

            bool offered = Offer(fixture);

            Assert.True(offered);
            ServerQuestShared packet = Assert.Single(GetMessages<ServerQuestShared>(fixture.Session));
            Assert.Equal(SharerGuid, packet.SharerUnitId);
            Assert.Equal(QuestId, packet.QuestId);
        }

        [Theory]
        [InlineData("zero-group")]
        [InlineData("different-group")]
        [InlineData("not-visible")]
        [InlineData("one-way-visibility")]
        [InlineData("different-map")]
        [InlineData("wrong-character")]
        [InlineData("same-character")]
        [InlineData("not-shareable")]
        public void OfferQuestShare_InvalidIdentityRelationshipOrSourceStateFailsClosed(string condition)
        {
            QuestSharingFixture fixture = CreateFixture();
            ulong offeredGroup = GroupAssociation;
            ulong offeredCharacterId = SharerCharacterId;

            switch (condition)
            {
                case "zero-group":
                    offeredGroup = 0ul;
                    break;
                case "different-group":
                    offeredGroup++;
                    break;
                case "not-visible":
                    fixture.Owner
                        .Setup(player => player.GetVisible<IPlayer>(SharerGuid))
                        .Returns((IPlayer)null);
                    break;
                case "one-way-visibility":
                    fixture.Sharer
                        .Setup(player => player.GetVisible<IPlayer>(OwnerGuid))
                        .Returns((IPlayer)null);
                    break;
                case "different-map":
                    fixture.Sharer.SetupGet(player => player.Map).Returns(Mock.Of<IBaseMap>());
                    break;
                case "wrong-character":
                    offeredCharacterId++;
                    break;
                case "same-character":
                    offeredCharacterId = OwnerCharacterId;
                    fixture.Sharer.SetupGet(player => player.CharacterId).Returns(OwnerCharacterId);
                    break;
                case "not-shareable":
                    fixture.SharerQuestManager
                        .Setup(manager => manager.CanShareQuest(QuestId))
                        .Returns(false);
                    break;
            }

            bool offered = fixture.Manager.OfferQuestShare(
                QuestId,
                SharerGuid,
                offeredCharacterId,
                offeredGroup);

            Assert.False(offered);
            Assert.Empty(GetMessages<ServerQuestShared>(fixture.Session));
        }

        [Theory]
        [InlineData(false, QuestId)]
        [InlineData(true, QuestId + 1)]
        public void QuestShareResult_FalseOrWrongResultConsumesOffer(bool result, int resultQuestId)
        {
            QuestSharingFixture fixture = CreateFixture();
            Assert.True(Offer(fixture));

            fixture.Manager.QuestShareResult((ushort)resultQuestId, result);
            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.Empty(fixture.Manager.GetActiveQuests());
            fixture.FactoryQuest.Verify(quest => quest.InitialiseTimer(), Times.Never);
        }

        [Fact]
        public void QuestShareResult_ReplayAddsQuestExactlyOnce()
        {
            QuestSharingFixture fixture = CreateFixture();
            Assert.True(Offer(fixture));

            fixture.Manager.QuestShareResult(QuestId, true);
            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.Same(fixture.FactoryQuest.Object, Assert.Single(fixture.Manager.GetActiveQuests()));
            fixture.FactoryQuest.Verify(quest => quest.InitialiseTimer(), Times.Once);
        }

        [Theory]
        [InlineData(9.999d, true)]
        [InlineData(10d, false)]
        [InlineData(10.001d, false)]
        public void QuestShareResult_UsesExclusiveTenSecondMonotonicWindow(double elapsed, bool accepted)
        {
            QuestSharingFixture fixture = CreateFixture();
            Assert.True(Offer(fixture));

            fixture.Manager.Update(elapsed);
            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.Equal(accepted, fixture.Manager.GetActiveQuests().Any());
        }

        [Fact]
        public void QuestShareResult_NonFiniteTicksDoNotExpireOffer()
        {
            QuestSharingFixture fixture = CreateFixture();
            Assert.True(Offer(fixture));

            fixture.Manager.Update(double.NaN);
            fixture.Manager.Update(double.PositiveInfinity);
            fixture.Manager.Update(double.NegativeInfinity);
            fixture.Manager.Update(9d);
            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.Single(fixture.Manager.GetActiveQuests());
        }

        [Theory]
        [InlineData("recipient-group")]
        [InlineData("source-group")]
        [InlineData("source-character")]
        [InlineData("recipient-character")]
        [InlineData("source-map")]
        [InlineData("recipient-visibility")]
        [InlineData("source-visibility")]
        [InlineData("source-state")]
        public void QuestShareResult_RelationshipOrSourceDriftRejectsAcceptance(string drift)
        {
            QuestSharingFixture fixture = CreateFixture();
            Assert.True(Offer(fixture));

            switch (drift)
            {
                case "recipient-group":
                    fixture.Owner.SetupGet(player => player.GroupAssociation).Returns(GroupAssociation + 1ul);
                    break;
                case "source-group":
                    fixture.Sharer.SetupGet(player => player.GroupAssociation).Returns(GroupAssociation + 1ul);
                    break;
                case "source-character":
                    fixture.Sharer.SetupGet(player => player.CharacterId).Returns(SharerCharacterId + 1ul);
                    break;
                case "recipient-character":
                    fixture.Owner.SetupGet(player => player.CharacterId).Returns(SharerCharacterId);
                    break;
                case "source-map":
                    fixture.Sharer.SetupGet(player => player.Map).Returns(Mock.Of<IBaseMap>());
                    break;
                case "recipient-visibility":
                    fixture.Owner
                        .Setup(player => player.GetVisible<IPlayer>(SharerGuid))
                        .Returns((IPlayer)null);
                    break;
                case "source-visibility":
                    fixture.Sharer
                        .Setup(player => player.GetVisible<IPlayer>(OwnerGuid))
                        .Returns((IPlayer)null);
                    break;
                case "source-state":
                    fixture.SharerQuestManager
                        .Setup(manager => manager.CanShareQuest(QuestId))
                        .Returns(false);
                    break;
            }

            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.Empty(fixture.Manager.GetActiveQuests());
        }

        [Theory]
        [InlineData(QuestState.Accepted)]
        [InlineData(QuestState.Achieved)]
        [InlineData(QuestState.Mentioned)]
        [InlineData(QuestState.Ignored)]
        [InlineData(QuestState.Botched)]
        [InlineData(QuestState.Abandoned)]
        [InlineData(QuestState.Unknown)]
        [InlineData(QuestState.Completed)]
        public void QuestShareResult_ExistingQuestStatesRejectWithoutGrant(QuestState state)
        {
            QuestSharingFixture fixture = CreateFixture();
            fixture.Entry.PushedItemIds = [500u];
            fixture.Entry.PushedItemCounts = [1u];
            fixture.ItemManager
                .Setup(manager => manager.GetItemInfo(500u))
                .Returns(Mock.Of<IItemInfo>(item => item.Id == 500u));
            Mock<IQuest> existing = CreateQuest(state);
            AddQuest(fixture.Manager, existing.Object);
            Assert.True(Offer(fixture));

            fixture.Manager.QuestShareResult(QuestId, true);

            fixture.Inventory.Verify(inventory => inventory.TryAdmitItemExchange(
                It.IsAny<IEnumerable<KeyValuePair<uint, uint>>>(),
                It.IsAny<IEnumerable<KeyValuePair<IItemInfo, uint>>>(),
                It.IsAny<ItemUpdateReason>()), Times.Never);
            fixture.FactoryQuest.Verify(quest => quest.Dispose(), Times.Never);
            existing.Verify(quest => quest.Dispose(), Times.Never);
            existing.Verify(quest => quest.InitialiseTimer(), Times.Never);
        }

        [Fact]
        public void QuestShareResult_ExpiredRepeatableCompletedQuestCanRestart()
        {
            DateTime now = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
            QuestSharingFixture fixture = CreateFixture(now);
            fixture.Entry.QuestRepeatPeriodEnum = (uint)QuestRepeatPeriod.Daily;
            Mock<IQuest> existing = CreateQuest(QuestState.Completed, reset: now.AddSeconds(-1d));
            AddQuest(fixture.Manager, existing.Object);
            Assert.True(Offer(fixture));

            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.Same(existing.Object, Assert.Single(fixture.Manager.GetActiveQuests()));
            Assert.Empty(GetQuestDictionary(fixture.Manager, "completedQuests"));
            existing.Verify(quest => quest.InitialiseTimer(), Times.Once);
        }

        [Theory]
        [InlineData("missing-reset")]
        [InlineData("future-reset")]
        [InlineData("unsupported-period")]
        public void QuestShareResult_InvalidRepeatMetadataRejectsRestart(string metadata)
        {
            DateTime now = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
            QuestSharingFixture fixture = CreateFixture(now);
            fixture.Entry.QuestRepeatPeriodEnum = metadata == "unsupported-period"
                ? uint.MaxValue
                : (uint)QuestRepeatPeriod.Daily;
            DateTime? reset = metadata switch
            {
                "missing-reset" => null,
                "future-reset"  => now.AddSeconds(1d),
                _               => now.AddSeconds(-1d)
            };
            Mock<IQuest> existing = CreateQuest(QuestState.Completed, reset: reset);
            AddQuest(fixture.Manager, existing.Object);
            Assert.True(Offer(fixture));

            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.Empty(fixture.Manager.GetActiveQuests());
            Assert.Single(GetQuestDictionary(fixture.Manager, "completedQuests"));
        }

        [Theory]
        [InlineData("disabled")]
        [InlineData("prerequisite")]
        [InlineData("capacity")]
        public void QuestShareResult_RevalidatesRecipientEligibility(string condition)
        {
            QuestSharingFixture fixture = CreateFixture();
            switch (condition)
            {
                case "disabled":
                    fixture.DisableManager
                        .Setup(manager => manager.IsDisabled(DisableType.Quest, QuestId))
                        .Returns(true);
                    break;
                case "prerequisite":
                    fixture.Entry.PrerequisiteId = 55u;
                    fixture.PrerequisiteManager
                        .Setup(manager => manager.Meets(fixture.Owner.Object, 55u))
                        .Returns(false);
                    break;
                case "capacity":
                    Dictionary<ushort, IQuest> active = GetQuestDictionary(fixture.Manager, "activeQuests");
                    for (ushort id = 1_000; id < 1_040; id++)
                        active.Add(id, CreateQuest(QuestState.Accepted, id: id).Object);
                    break;
            }
            Assert.True(Offer(fixture));

            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.DoesNotContain(fixture.Manager.GetActiveQuests(), quest => quest.Id == QuestId);
        }

        [Theory]
        [InlineData(10104)]
        [InlineData(10105)]
        [InlineData(10106)]
        [InlineData(10107)]
        public void QuestShareResult_ContractFailsClosedUntilCapacityIsImplemented(int contractId)
        {
            ushort questId = (ushort)contractId;
            QuestSharingFixture fixture = CreateFixture(questId: questId);
            fixture.Info.Setup(info => info.IsContract()).Returns(true);
            Assert.True(Offer(fixture));

            fixture.Manager.QuestShareResult(questId, true);

            Assert.Empty(fixture.Manager.GetActiveQuests());
            fixture.FactoryQuest.Verify(quest => quest.InitialiseTimer(), Times.Never);
        }

        [Theory]
        [InlineData("item-prerequisite")]
        [InlineData("exclusion")]
        [InlineData("zero-enum")]
        [InlineData("virtual-item")]
        [InlineData("virtual-count")]
        public void QuestShareResult_UnsupportedPrerequisiteOrPushSemanticsFailClosed(string metadata)
        {
            QuestSharingFixture fixture = CreateFixture();
            switch (metadata)
            {
                case "item-prerequisite":
                    fixture.Entry.PrerequisiteItem = 1u;
                    break;
                case "exclusion":
                    fixture.Entry.QuestIdExclusionPreq1 = 2u;
                    break;
                case "zero-enum":
                    fixture.Entry.QuestShareEnum = 0u;
                    break;
                case "virtual-item":
                    fixture.Entry.VirtualItemIdPushed02 = 3u;
                    break;
                case "virtual-count":
                    fixture.Entry.VirtualItemPushedCount03 = 4u;
                    break;
            }
            Assert.True(Offer(fixture));

            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.Empty(fixture.Manager.GetActiveQuests());
            fixture.Inventory.Verify(inventory => inventory.TryAdmitItemExchange(
                It.IsAny<IEnumerable<KeyValuePair<uint, uint>>>(),
                It.IsAny<IEnumerable<KeyValuePair<IItemInfo, uint>>>(),
                It.IsAny<ItemUpdateReason>()), Times.Never);
            fixture.FactoryQuest.Verify(quest => quest.Dispose(), Times.Never);
        }

        [Fact]
        public void QuestShareResult_AggregatesDuplicatePushedItemsInOneExchange()
        {
            QuestSharingFixture fixture = CreateFixture();
            fixture.Entry.PushedItemIds = [500u, 500u, 600u];
            fixture.Entry.PushedItemCounts = [2u, 3u, 4u];
            IItemInfo item500 = Mock.Of<IItemInfo>(item => item.Id == 500u);
            IItemInfo item600 = Mock.Of<IItemInfo>(item => item.Id == 600u);
            fixture.ItemManager.Setup(manager => manager.GetItemInfo(500u)).Returns(item500);
            fixture.ItemManager.Setup(manager => manager.GetItemInfo(600u)).Returns(item600);
            IReadOnlyCollection<KeyValuePair<IItemInfo, uint>> captured = null;
            fixture.Inventory
                .Setup(inventory => inventory.TryAdmitItemExchange(
                    It.IsAny<IEnumerable<KeyValuePair<uint, uint>>>(),
                    It.IsAny<IEnumerable<KeyValuePair<IItemInfo, uint>>>(),
                    ItemUpdateReason.Quest))
                .Callback<IEnumerable<KeyValuePair<uint, uint>>, IEnumerable<KeyValuePair<IItemInfo, uint>>, ItemUpdateReason>(
                    (_, additions, _) => captured = additions.ToArray())
                .Returns(true);
            Assert.True(Offer(fixture));

            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.Single(fixture.Manager.GetActiveQuests());
            Assert.Equal(2, captured.Count);
            Assert.Contains(captured, pair => pair.Key == item500 && pair.Value == 5u);
            Assert.Contains(captured, pair => pair.Key == item600 && pair.Value == 4u);
            fixture.Inventory.Verify(inventory => inventory.TryAdmitItemExchange(
                It.IsAny<IEnumerable<KeyValuePair<uint, uint>>>(),
                It.IsAny<IEnumerable<KeyValuePair<IItemInfo, uint>>>(),
                ItemUpdateReason.Quest), Times.Once);
        }

        [Theory]
        [InlineData("missing-count")]
        [InlineData("missing-id")]
        [InlineData("overflow")]
        [InlineData("unknown-item")]
        public void QuestShareResult_InvalidPushedItemsFailWithoutMutation(string metadata)
        {
            QuestSharingFixture fixture = CreateFixture();
            switch (metadata)
            {
                case "missing-count":
                    fixture.Entry.PushedItemIds = [500u];
                    fixture.Entry.PushedItemCounts = [0u];
                    break;
                case "missing-id":
                    fixture.Entry.PushedItemIds = [0u];
                    fixture.Entry.PushedItemCounts = [1u];
                    break;
                case "overflow":
                    fixture.Entry.PushedItemIds = [500u, 500u];
                    fixture.Entry.PushedItemCounts = [uint.MaxValue, 1u];
                    break;
                case "unknown-item":
                    fixture.Entry.PushedItemIds = [500u];
                    fixture.Entry.PushedItemCounts = [1u];
                    fixture.ItemManager.Setup(manager => manager.GetItemInfo(500u)).Returns((IItemInfo)null);
                    break;
            }
            Assert.True(Offer(fixture));

            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.Empty(fixture.Manager.GetActiveQuests());
            fixture.Inventory.Verify(inventory => inventory.TryAdmitItemExchange(
                It.IsAny<IEnumerable<KeyValuePair<uint, uint>>>(),
                It.IsAny<IEnumerable<KeyValuePair<IItemInfo, uint>>>(),
                It.IsAny<ItemUpdateReason>()), Times.Never);
            fixture.FactoryQuest.Verify(quest => quest.Dispose(), Times.Once);
        }

        [Fact]
        public void QuestShareResult_InventoryRejectionLeavesNoActiveQuest()
        {
            QuestSharingFixture fixture = CreateFixture();
            fixture.Entry.PushedItemIds = [500u];
            fixture.Entry.PushedItemCounts = [1u];
            fixture.ItemManager
                .Setup(manager => manager.GetItemInfo(500u))
                .Returns(Mock.Of<IItemInfo>(item => item.Id == 500u));
            fixture.Inventory
                .Setup(inventory => inventory.TryAdmitItemExchange(
                    It.IsAny<IEnumerable<KeyValuePair<uint, uint>>>(),
                    It.IsAny<IEnumerable<KeyValuePair<IItemInfo, uint>>>(),
                    ItemUpdateReason.Quest))
                .Returns(false);
            Assert.True(Offer(fixture));

            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.Empty(fixture.Manager.GetActiveQuests());
            fixture.FactoryQuest.Verify(quest => quest.InitialiseTimer(), Times.Never);
            fixture.FactoryQuest.Verify(quest => quest.Dispose(), Times.Once);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void QuestAdmission_InspectionFailureDisposesFreshQuest(bool shared)
        {
            QuestSharingFixture fixture = CreateFixture();
            fixture.FactoryQuest
                .Setup(quest => quest.GetEnumerator())
                .Throws<InvalidOperationException>();

            if (shared)
            {
                Assert.True(Offer(fixture));
                fixture.Manager.QuestShareResult(QuestId, true);
            }
            else
                fixture.Manager.QuestAdd(fixture.Info.Object);

            Assert.Empty(fixture.Manager.GetActiveQuests());
            fixture.FactoryQuest.Verify(quest => quest.Dispose(), Times.Once);
        }

        [Fact]
        public void QuestAdd_InventoryRejectionDisposesFreshQuest()
        {
            QuestSharingFixture fixture = CreateFixture();
            ConfigurePushedItem(fixture);
            fixture.Inventory
                .Setup(inventory => inventory.TryAdmitItemExchange(
                    It.IsAny<IEnumerable<KeyValuePair<uint, uint>>>(),
                    It.IsAny<IEnumerable<KeyValuePair<IItemInfo, uint>>>(),
                    ItemUpdateReason.Quest))
                .Returns(false);

            fixture.Manager.QuestAdd(fixture.Info.Object);

            Assert.Empty(fixture.Manager.GetActiveQuests());
            fixture.FactoryQuest.Verify(quest => quest.Dispose(), Times.Once);
        }

        [Fact]
        public void QuestAdd_TimerInitialisationFailureDisposesFreshQuest()
        {
            QuestSharingFixture fixture = CreateFixture();
            fixture.FactoryQuest
                .Setup(quest => quest.InitialiseTimer())
                .Throws<InvalidOperationException>();

            Assert.Throws<InvalidOperationException>(() => fixture.Manager.QuestAdd(fixture.Info.Object));

            Assert.Empty(fixture.Manager.GetActiveQuests());
            fixture.FactoryQuest.Verify(quest => quest.Dispose(), Times.Once);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void QuestAdmission_RejectedInventoryPreservesExistingQuest(bool shared)
        {
            DateTime now = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
            QuestSharingFixture fixture = CreateFixture(now);
            fixture.Entry.QuestRepeatPeriodEnum = (uint)QuestRepeatPeriod.Daily;
            ConfigurePushedItem(fixture);
            fixture.Inventory
                .Setup(inventory => inventory.TryAdmitItemExchange(
                    It.IsAny<IEnumerable<KeyValuePair<uint, uint>>>(),
                    It.IsAny<IEnumerable<KeyValuePair<IItemInfo, uint>>>(),
                    ItemUpdateReason.Quest))
                .Returns(false);
            Mock<IQuest> existing = CreateQuest(QuestState.Completed, reset: now.AddSeconds(-1d));
            AddQuest(fixture.Manager, existing.Object);

            if (shared)
            {
                Assert.True(Offer(fixture));
                fixture.Manager.QuestShareResult(QuestId, true);
            }
            else
                fixture.Manager.QuestAdd(fixture.Info.Object);

            Assert.Empty(fixture.Manager.GetActiveQuests());
            Assert.Same(existing.Object, Assert.Single(GetQuestDictionary(fixture.Manager, "completedQuests")).Value);
            existing.Verify(quest => quest.Dispose(), Times.Never);
        }

        [Fact]
        public void QuestShareResult_AdmittedInventoryFaultCommitsQuestExactlyOnce()
        {
            QuestSharingFixture fixture = CreateFixture();
            ConfigurePushedItem(fixture);
            bool faultContained = false;
            fixture.Inventory
                .Setup(inventory => inventory.TryAdmitItemExchange(
                    It.IsAny<IEnumerable<KeyValuePair<uint, uint>>>(),
                    It.IsAny<IEnumerable<KeyValuePair<IItemInfo, uint>>>(),
                    ItemUpdateReason.Quest))
                .Callback(() => faultContained = true)
                .Returns(true);
            Assert.True(Offer(fixture));

            fixture.Manager.QuestShareResult(QuestId, true);
            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.True(faultContained);
            Assert.Same(fixture.FactoryQuest.Object, Assert.Single(fixture.Manager.GetActiveQuests()));
            fixture.FactoryQuest.Verify(quest => quest.InitialiseTimer(), Times.Once);
            fixture.FactoryQuest.Verify(quest => quest.Dispose(), Times.Never);
        }

        [Fact]
        public void QuestShareResult_NotificationFailureKeepsAdmittedItemsBoundToActiveQuest()
        {
            QuestSharingFixture fixture = CreateFixture();
            fixture.Entry.PushedItemIds = [500u];
            fixture.Entry.PushedItemCounts = [1u];
            fixture.ItemManager
                .Setup(manager => manager.GetItemInfo(500u))
                .Returns(Mock.Of<IItemInfo>(item => item.Id == 500u));
            fixture.FactoryQuest
                .SetupSet(quest => quest.State = It.IsAny<QuestState>())
                .Throws<InvalidOperationException>();
            Assert.True(Offer(fixture));

            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.Same(fixture.FactoryQuest.Object, Assert.Single(fixture.Manager.GetActiveQuests()));
            fixture.Inventory.Verify(inventory => inventory.TryAdmitItemExchange(
                It.IsAny<IEnumerable<KeyValuePair<uint, uint>>>(),
                It.IsAny<IEnumerable<KeyValuePair<IItemInfo, uint>>>(),
                ItemUpdateReason.Quest), Times.Once);
            fixture.FactoryQuest.Verify(quest => quest.Dispose(), Times.Never);
        }

        [Fact]
        public void OfferQuestShare_ReplacesOnlyAfterNewNotificationSucceeds()
        {
            QuestSharingFixture fixture = CreateFixture();
            Assert.True(Offer(fixture));
            var replacement = CreateSharer(11u, 101ul, fixture.Map, fixture.Owner.Object);
            fixture.Owner
                .Setup(player => player.GetVisible<IPlayer>(11u))
                .Returns(replacement.Player.Object);
            fixture.Session
                .Setup(session => session.TryEnqueueMessageEncrypted(
                    It.Is<ServerQuestShared>(packet => packet.SharerUnitId == 11u)))
                .Throws<InvalidOperationException>();

            bool replaced = fixture.Manager.OfferQuestShare(
                QuestId,
                11u,
                101ul,
                GroupAssociation);
            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.False(replaced);
            Assert.Single(fixture.Manager.GetActiveQuests());
        }

        [Fact]
        public void OfferQuestShare_QueueRejectionPreservesDeliveredOffer()
        {
            QuestSharingFixture fixture = CreateFixture();
            Assert.True(Offer(fixture));
            var replacement = CreateSharer(11u, 101ul, fixture.Map, fixture.Owner.Object);
            fixture.Owner
                .Setup(player => player.GetVisible<IPlayer>(11u))
                .Returns(replacement.Player.Object);
            fixture.Session
                .Setup(session => session.TryEnqueueMessageEncrypted(
                    It.Is<ServerQuestShared>(packet => packet.SharerUnitId == 11u)))
                .Returns(false);

            bool replaced = fixture.Manager.OfferQuestShare(
                QuestId,
                11u,
                101ul,
                GroupAssociation);
            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.False(replaced);
            Assert.Single(fixture.Manager.GetActiveQuests());
            fixture.SharerQuestManager.Verify(manager => manager.CanShareQuest(QuestId), Times.Exactly(2));
        }

        [Fact]
        public void OfferQuestShare_SuccessfulNewOfferReplacesPreviousSource()
        {
            QuestSharingFixture fixture = CreateFixture();
            Assert.True(Offer(fixture));
            var replacement = CreateSharer(11u, 101ul, fixture.Map, fixture.Owner.Object);
            fixture.Owner
                .Setup(player => player.GetVisible<IPlayer>(11u))
                .Returns(replacement.Player.Object);

            Assert.True(fixture.Manager.OfferQuestShare(
                QuestId,
                11u,
                101ul,
                GroupAssociation));
            fixture.Owner
                .Setup(player => player.GetVisible<IPlayer>(SharerGuid))
                .Returns((IPlayer)null);
            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.Single(fixture.Manager.GetActiveQuests());
            replacement.Manager.Verify(manager => manager.CanShareQuest(QuestId), Times.Exactly(2));
        }

        [Fact]
        public void Dispose_ClearsOfferAndRejectsSubsequentOffers()
        {
            QuestSharingFixture fixture = CreateFixture();
            Assert.True(Offer(fixture));

            fixture.Manager.Dispose();
            fixture.Manager.QuestShareResult(QuestId, true);

            Assert.Empty(fixture.Manager.GetActiveQuests());
            Assert.False(Offer(fixture));
        }

        private static bool Offer(QuestSharingFixture fixture)
        {
            return fixture.Manager.OfferQuestShare(
                (ushort)fixture.Entry.Id,
                SharerGuid,
                SharerCharacterId,
                GroupAssociation);
        }

        private static void ConfigurePushedItem(QuestSharingFixture fixture)
        {
            fixture.Entry.PushedItemIds = [500u];
            fixture.Entry.PushedItemCounts = [1u];
            fixture.ItemManager
                .Setup(manager => manager.GetItemInfo(500u))
                .Returns(Mock.Of<IItemInfo>(item => item.Id == 500u));
        }

        private static QuestSharingFixture CreateFixture(DateTime? utcNow = null, ushort questId = QuestId)
        {
            var map = new Mock<IBaseMap>();
            var session = new Mock<IGameSession>();
            session
                .Setup(value => value.TryEnqueueMessageEncrypted(It.IsAny<IWritable>()))
                .Returns(true);
            var inventory = new Mock<IInventory>();
            inventory
                .Setup(value => value.TryAdmitItemExchange(
                    It.IsAny<IEnumerable<KeyValuePair<uint, uint>>>(),
                    It.IsAny<IEnumerable<KeyValuePair<IItemInfo, uint>>>(),
                    It.IsAny<ItemUpdateReason>()))
                .Returns(true);

            var owner = new Mock<IPlayer>();
            owner.SetupGet(player => player.Guid).Returns(OwnerGuid);
            owner.SetupGet(player => player.CharacterId).Returns(OwnerCharacterId);
            owner.SetupGet(player => player.InWorld).Returns(true);
            owner.SetupGet(player => player.Map).Returns(map.Object);
            owner.SetupGet(player => player.GroupAssociation).Returns(GroupAssociation);
            owner.SetupGet(player => player.Session).Returns(session.Object);
            owner.SetupGet(player => player.Inventory).Returns(inventory.Object);
            owner.SetupGet(player => player.Faction1).Returns(Faction.Exile);
            owner.SetupGet(player => player.Level).Returns(50u);

            (Mock<IPlayer> Sharer, Mock<IQuestManager> Manager) sharer = CreateSharer(
                SharerGuid,
                SharerCharacterId,
                map,
                owner.Object,
                questId);
            owner
                .Setup(player => player.GetVisible<IPlayer>(SharerGuid))
                .Returns(sharer.Sharer.Object);

            var entry = new Quest2Entry
            {
                Id                     = questId,
                QuestShareEnum         = 1u,
                QuestPlayerFactionEnum = 0u,
                PrerequisiteLevel      = 1u,
                PushedItemIds          = [],
                PushedItemCounts       = []
            };
            var info = new Mock<IQuestInfo>();
            info.SetupGet(value => value.Entry).Returns(entry);
            info.SetupGet(value => value.PrerequisiteQuests).Returns(ImmutableList<Quest2Entry>.Empty);
            info.SetupGet(value => value.Objectives).Returns(ImmutableList<IQuestObjectiveInfo>.Empty);
            info.Setup(value => value.IsContract()).Returns(false);

            var globalQuestManager = new Mock<IGlobalQuestManager>();
            globalQuestManager.Setup(manager => manager.GetQuestInfo(questId)).Returns(info.Object);
            globalQuestManager
                .Setup(manager => manager.GetQuestCommunicatorQuestStateTriggers(
                    It.IsAny<ushort>(),
                    It.IsAny<QuestState>()))
                .Returns([]);
            var disableManager = new Mock<IDisableManager>();
            disableManager
                .Setup(manager => manager.IsDisabled(DisableType.Quest, questId))
                .Returns(false);
            var itemManager = new Mock<IItemManager>();
            var prerequisiteManager = new Mock<IPrerequisiteManager>();
            var gameTableManager = new Mock<IGameTableManager>();
            Mock<IQuest> factoryQuest = CreateQuest(QuestState.Accepted, id: questId);

            DateTime now = utcNow ?? new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
            var manager = new QuestManager(
                owner.Object,
                new CharacterModel(),
                globalQuestManager.Object,
                null,
                disableManager.Object,
                () => now,
                itemManager.Object,
                prerequisiteManager.Object,
                gameTableManager.Object,
                _ => factoryQuest.Object);
            owner.SetupGet(player => player.QuestManager).Returns(manager);

            return new QuestSharingFixture(
                manager,
                owner,
                sharer.Sharer,
                sharer.Manager,
                map,
                session,
                inventory,
                globalQuestManager,
                disableManager,
                itemManager,
                prerequisiteManager,
                entry,
                info,
                factoryQuest);
        }

        private static (Mock<IPlayer> Player, Mock<IQuestManager> Manager) CreateSharer(
            uint guid,
            ulong characterId,
            Mock<IBaseMap> map,
            IPlayer recipient,
            ushort questId = QuestId)
        {
            var questManager = new Mock<IQuestManager>();
            questManager.Setup(manager => manager.CanShareQuest(questId)).Returns(true);
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Guid).Returns(guid);
            player.SetupGet(value => value.CharacterId).Returns(characterId);
            player.SetupGet(value => value.InWorld).Returns(true);
            player.SetupGet(value => value.Map).Returns(map.Object);
            player.SetupGet(value => value.GroupAssociation).Returns(GroupAssociation);
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);
            player.Setup(value => value.GetVisible<IPlayer>(OwnerGuid)).Returns(recipient);
            return (player, questManager);
        }

        private static Mock<IQuest> CreateQuest(
            QuestState state,
            bool canShare = false,
            DateTime? reset = null,
            ushort id = QuestId)
        {
            var quest = new Mock<IQuest>();
            quest.SetupGet(value => value.Id).Returns(id);
            quest.SetupProperty(value => value.State, state);
            quest.SetupProperty(value => value.Flags, QuestStateFlags.None);
            quest.SetupProperty(value => value.Reset, reset);
            quest.SetupGet(value => value.PendingDelete).Returns(false);
            quest.Setup(value => value.CanShare()).Returns(canShare);
            quest.Setup(value => value.GetEnumerator())
                .Returns(() => Enumerable.Empty<IQuestObjective>().GetEnumerator());
            return quest;
        }

        private static void AddQuest(QuestManager manager, IQuest quest)
        {
            string dictionaryName = quest.State switch
            {
                QuestState.Accepted or QuestState.Achieved => "activeQuests",
                QuestState.Completed                       => "completedQuests",
                _                                          => "inactiveQuests"
            };
            GetQuestDictionary(manager, dictionaryName).Add(quest.Id, quest);
        }

        private static Dictionary<ushort, IQuest> GetQuestDictionary(QuestManager manager, string name)
        {
            FieldInfo field = typeof(QuestManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return (Dictionary<ushort, IQuest>)field.GetValue(manager);
        }

        private static IReadOnlyList<T> GetMessages<T>(Mock<IGameSession> session) where T : IWritable
        {
            return session.Invocations
                .Where(invocation => invocation.Method.Name == nameof(IGameSession.TryEnqueueMessageEncrypted))
                .Select(invocation => invocation.Arguments[0])
                .OfType<T>()
                .ToArray();
        }

        private sealed record QuestSharingFixture(
            QuestManager Manager,
            Mock<IPlayer> Owner,
            Mock<IPlayer> Sharer,
            Mock<IQuestManager> SharerQuestManager,
            Mock<IBaseMap> Map,
            Mock<IGameSession> Session,
            Mock<IInventory> Inventory,
            Mock<IGlobalQuestManager> GlobalQuestManager,
            Mock<IDisableManager> DisableManager,
            Mock<IItemManager> ItemManager,
            Mock<IPrerequisiteManager> PrerequisiteManager,
            Quest2Entry Entry,
            Mock<IQuestInfo> Info,
            Mock<IQuest> FactoryQuest);
    }
}
