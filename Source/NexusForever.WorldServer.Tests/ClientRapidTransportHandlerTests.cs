using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Reputation;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Entity.Player;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientRapidTransportHandlerTests
    {
        private const ushort TaxiNodeId = 10;
        private const uint WorldLocationId = 20u;
        private const uint RapidTransportSpellId = 30u;

        [Theory]
        [InlineData(0u, Faction.Exile)]
        [InlineData(0u, Faction.Dominion)]
        [InlineData(1u, Faction.Exile)]
        [InlineData(2u, Faction.Dominion)]
        public void AllowedFaction_PreservesExactRapidTransportCast(
            uint taxiNodeFaction,
            Faction playerFaction)
        {
            RapidTransportFixture fixture = CreateFixture(taxiNodeFaction, playerFaction);

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage());

            fixture.GameTableManager.VerifyGet(value => value.WorldLocation2, Times.Once);
            fixture.GameTableManager.VerifyGet(value => value.GameFormula, Times.Once);
            fixture.Player.Verify(value => value.CastSpell(
                RapidTransportSpellId,
                It.Is<ISpellParameters>(parameters => parameters.TaxiNode == TaxiNodeId)), Times.Once);
        }

        [Theory]
        [InlineData(1u, Faction.Dominion)]
        [InlineData(2u, Faction.Exile)]
        [InlineData(3u, Faction.Exile)]
        [InlineData(uint.MaxValue, Faction.Dominion)]
        public void DisallowedOrUnknownFaction_RejectsBeforeLocationFormulaOrCast(
            uint taxiNodeFaction,
            Faction playerFaction)
        {
            RapidTransportFixture fixture = CreateFixture(taxiNodeFaction, playerFaction);

            Assert.Throws<InvalidPacketValueException>(() =>
                fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage()));

            AssertNoLocationFormulaOrCast(fixture);
        }

        [Fact]
        public void MissingTaxiNode_RejectsBeforePlayerOrDownstreamAccess()
        {
            RapidTransportFixture fixture = CreateFixture(includeTaxiNode: false);

            Assert.Throws<InvalidPacketValueException>(() =>
                fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage()));

            fixture.Session.VerifyGet(value => value.Player, Times.Never);
            AssertNoLocationFormulaOrCast(fixture);
        }

        [Fact]
        public void BelowAutoUnlockLevel_RejectsBeforeFactionOrDownstreamAccess()
        {
            RapidTransportFixture fixture = CreateFixture(
                taxiNodeFaction: 1u,
                playerFaction: Faction.Exile,
                playerLevel: 9u,
                autoUnlockLevel: 10u);

            Assert.Throws<InvalidPacketValueException>(() =>
                fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage()));

            fixture.Player.VerifyGet(value => value.Faction1, Times.Never);
            AssertNoLocationFormulaOrCast(fixture);
        }

        private static RapidTransportFixture CreateFixture(
            uint taxiNodeFaction = 0u,
            Faction playerFaction = Faction.Exile,
            uint playerLevel = 50u,
            uint autoUnlockLevel = 1u,
            bool includeTaxiNode = true)
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Level).Returns(playerLevel);
            player.SetupGet(value => value.Faction1).Returns(playerFaction);

            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            TaxiNodeEntry[] taxiNodes = includeTaxiNode
                ? [
                    new TaxiNodeEntry
                    {
                        Id = TaxiNodeId,
                        TaxiNodeFactionEnum = taxiNodeFaction,
                        WorldLocation2Id = WorldLocationId,
                        AutoUnlockLevel = autoUnlockLevel
                    }
                ]
                : [];

            var gameTableManager = new Mock<IGameTableManager>();
            gameTableManager.SetupGet(value => value.TaxiNode).Returns(CreateGameTable(taxiNodes));
            gameTableManager.SetupGet(value => value.WorldLocation2).Returns(CreateGameTable(
                new WorldLocation2Entry { Id = WorldLocationId }));
            gameTableManager.SetupGet(value => value.GameFormula).Returns(CreateGameTable(
                new GameFormulaEntry { Id = 1307u, Dataint0 = RapidTransportSpellId }));

            return new RapidTransportFixture(
                new ClientRapidTransportHandler(gameTableManager.Object),
                session,
                player,
                gameTableManager);
        }

        private static ClientRapidTransport CreateMessage()
        {
            var message = new ClientRapidTransport();
            typeof(ClientRapidTransport)
                .GetProperty(nameof(ClientRapidTransport.TaxiNode), BindingFlags.Instance | BindingFlags.Public)
                ?.SetValue(message, TaxiNodeId);
            return message;
        }

        private static GameTable<T> CreateGameTable<T>(params T[] entries) where T : class, new()
        {
            var table = (GameTable<T>)RuntimeHelpers.GetUninitializedObject(typeof(GameTable<T>));
            typeof(GameTable<T>).GetProperty(nameof(GameTable<T>.Entries))?.SetValue(table, entries);

            FieldInfo idField = typeof(T).GetFields().First();
            uint maximumId = entries.Select(entry => (uint)idField.GetValue(entry)).DefaultIfEmpty().Max();
            int[] lookup = Enumerable.Repeat(-1, checked((int)maximumId + 1)).ToArray();
            for (int index = 0; index < entries.Length; index++)
                lookup[(uint)idField.GetValue(entries[index])] = index;

            typeof(GameTable<T>)
                .GetField("lookup", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, lookup);
            typeof(GameTable<T>)
                .GetField("header", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, new GameTableHeader { MaxId = maximumId + 1ul });
            return table;
        }

        private static void AssertNoLocationFormulaOrCast(RapidTransportFixture fixture)
        {
            fixture.GameTableManager.VerifyGet(value => value.WorldLocation2, Times.Never);
            fixture.GameTableManager.VerifyGet(value => value.GameFormula, Times.Never);
            fixture.Player.Verify(value => value.CastSpell(
                It.IsAny<uint>(), It.IsAny<ISpellParameters>()), Times.Never);
        }

        private sealed record RapidTransportFixture(
            ClientRapidTransportHandler Handler,
            Mock<IWorldSession> Session,
            Mock<IPlayer> Player,
            Mock<IGameTableManager> GameTableManager);
    }
}
