using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Reputation;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.Network.World.Message.Model.Utility;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Entity.Player;
using NetworkItem = NexusForever.Network.World.Message.Model.Shared.Item;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientInspectPlayerRequestHandlerTests
    {
        private const uint RequesterUnitId = 41u;
        private const uint TargetUnitId = 73u;

        [Theory]
        [InlineData(Faction.Exile, Faction.Exile, 0ul, 0ul, true)]
        [InlineData(Faction.Exile, Faction.Dominion, 29ul, 29ul, true)]
        [InlineData(Faction.Exile, Faction.Dominion, 0ul, 0ul, false)]
        [InlineData(Faction.Exile, Faction.Dominion, 29ul, 30ul, false)]
        public void VisiblePlayer_RequiresSameCurrentFactionOrSameNonzeroGroup(
            Faction requesterFaction,
            Faction targetFaction,
            ulong requesterGroup,
            ulong targetGroup,
            bool admitted)
        {
            InspectFixture fixture = CreateFixture();
            fixture.Requester.SetupGet(player => player.Faction1).Returns(requesterFaction);
            fixture.Target.SetupGet(player => player.Faction1).Returns(targetFaction);
            fixture.Requester.SetupGet(player => player.GroupAssociation).Returns(requesterGroup);
            fixture.Target.SetupGet(player => player.GroupAssociation).Returns(targetGroup);

            fixture.Handle(TargetUnitId);

            if (admitted)
            {
                fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                    It.Is<ServerInspectPlayerResponse>(message =>
                        message.UnitId == fixture.Target.Object.Guid
                        && message.Items.Count == 2
                        && ReferenceEquals(message.Items[0], fixture.NetworkItems[0])
                        && ReferenceEquals(message.Items[1], fixture.NetworkItems[1]))), Times.Once);
                fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                    It.IsAny<IWritable>()), Times.Once);
                fixture.Target.VerifyGet(player => player.Inventory, Times.Once);
            }
            else
            {
                AssertNoDisclosure(fixture);
            }
        }

        [Fact]
        public void UnknownUnit_IsIgnoredBeforeTargetInventoryOrResponse()
        {
            InspectFixture fixture = CreateFixture();
            fixture.Requester
                .Setup(player => player.GetVisible<IGridEntity>(TargetUnitId))
                .Returns((IGridEntity)null);

            fixture.Handle(TargetUnitId);

            AssertNoDisclosure(fixture);
        }

        [Fact]
        public void PersistentCharacterIdCollisionOutsideVisibility_IsIgnoredBeforeInventoryOrResponse()
        {
            InspectFixture fixture = CreateFixture();
            fixture.Target.SetupGet(player => player.CharacterId).Returns(TargetUnitId);
            fixture.Requester
                .Setup(player => player.GetVisible<IGridEntity>(TargetUnitId))
                .Returns((IGridEntity)null);

            fixture.Handle(TargetUnitId);

            fixture.Target.VerifyGet(player => player.CharacterId, Times.Never);
            AssertNoDisclosure(fixture);
        }

        [Fact]
        public void VisibleNonPlayer_IsIgnoredBeforeInventoryOrResponse()
        {
            InspectFixture fixture = CreateFixture();
            var nonPlayer = new Mock<IGridEntity>();
            nonPlayer.SetupGet(entity => entity.Guid).Returns(TargetUnitId);
            fixture.Requester
                .Setup(player => player.GetVisible<IGridEntity>(TargetUnitId))
                .Returns(nonPlayer.Object);

            fixture.Handle(TargetUnitId);

            AssertNoDisclosure(fixture);
        }

        [Fact]
        public void SelfUnit_IsIgnoredBeforeVisibilityLookupOrResponse()
        {
            InspectFixture fixture = CreateFixture();

            fixture.Handle(RequesterUnitId);

            fixture.Requester.Verify(
                player => player.GetVisible<IGridEntity>(It.IsAny<uint>()),
                Times.Never);
            AssertNoDisclosure(fixture);
        }

        [Fact]
        public void VisiblePlayerWithMismatchedRuntimeGuid_IsIgnoredBeforeInventoryOrResponse()
        {
            InspectFixture fixture = CreateFixture();
            fixture.Target.SetupGet(player => player.Guid).Returns(TargetUnitId + 1u);

            fixture.Handle(TargetUnitId);

            AssertNoDisclosure(fixture);
        }

        [Theory]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        public void PlayerOutsideExactLiveMap_IsIgnoredBeforeInventoryOrResponse(
            bool requesterInWorld,
            bool targetInWorld,
            bool sameMap)
        {
            InspectFixture fixture = CreateFixture();
            fixture.Requester.SetupGet(player => player.InWorld).Returns(requesterInWorld);
            fixture.Target.SetupGet(player => player.InWorld).Returns(targetInWorld);
            if (!sameMap)
                fixture.Target.SetupGet(player => player.Map).Returns(Mock.Of<IBaseMap>());

            fixture.Handle(TargetUnitId);

            AssertNoDisclosure(fixture);
        }

        private static InspectFixture CreateFixture()
        {
            var map = new Mock<IBaseMap>();
            var requester = new Mock<IPlayer>();
            requester.SetupGet(player => player.Guid).Returns(RequesterUnitId);
            requester.SetupGet(player => player.InWorld).Returns(true);
            requester.SetupGet(player => player.Map).Returns(map.Object);
            requester.SetupGet(player => player.Faction1).Returns(Faction.Exile);

            var firstNetworkItem = new NetworkItem { Guid = 101ul, ItemId = 1001u };
            var secondNetworkItem = new NetworkItem { Guid = 102ul, ItemId = 1002u };
            NetworkItem[] networkItems = [firstNetworkItem, secondNetworkItem];

            var firstItem = new Mock<IItem>();
            firstItem.Setup(item => item.Build()).Returns(firstNetworkItem);
            var secondItem = new Mock<IItem>();
            secondItem.Setup(item => item.Build()).Returns(secondNetworkItem);
            IItem[] items = [firstItem.Object, secondItem.Object];

            var equippedBag = new Mock<IBag>();
            equippedBag.SetupGet(bag => bag.Location).Returns(InventoryLocation.Equipped);
            equippedBag.Setup(bag => bag.GetEnumerator())
                .Returns(() => ((IEnumerable<IItem>)items).GetEnumerator());
            IBag[] bags = [equippedBag.Object];

            var inventory = new Mock<IInventory>();
            inventory.Setup(value => value.GetEnumerator())
                .Returns(() => ((IEnumerable<IBag>)bags).GetEnumerator());

            var target = new Mock<IPlayer>();
            target.SetupGet(player => player.Guid).Returns(TargetUnitId);
            target.SetupGet(player => player.InWorld).Returns(true);
            target.SetupGet(player => player.Map).Returns(map.Object);
            target.SetupGet(player => player.Faction1).Returns(Faction.Exile);
            target.SetupGet(player => player.Inventory).Returns(inventory.Object);

            requester
                .Setup(player => player.GetVisible<IGridEntity>(TargetUnitId))
                .Returns(target.Object);

            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(requester.Object);

            return new InspectFixture(
                new ClientInspectPlayerRequestHandler(),
                session,
                requester,
                target,
                inventory,
                networkItems);
        }

        private static void AssertNoDisclosure(InspectFixture fixture)
        {
            fixture.Target.VerifyGet(player => player.Inventory, Times.Never);
            fixture.Inventory.Verify(
                inventory => inventory.GetEnumerator(),
                Times.Never);
            fixture.Session.Verify(
                session => session.EnqueueMessageEncrypted(It.IsAny<IWritable>()),
                Times.Never);
        }

        private sealed record InspectFixture(
            ClientInspectPlayerRequestHandler Handler,
            Mock<IWorldSession> Session,
            Mock<IPlayer> Requester,
            Mock<IPlayer> Target,
            Mock<IInventory> Inventory,
            NetworkItem[] NetworkItems)
        {
            public void Handle(uint unitId)
            {
                Handler.HandleMessage(Session.Object, new ClientInspectPlayerRequest
                {
                    UnitId = unitId
                });
            }
        }
    }
}
