using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Guild;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Guild;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Guild;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Guild;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientGuildRegisterHandlerTests
    {
        private const uint GuildMinimumLevelFormulaId = 1044u;
        private const uint WarPartyMinimumLevelFormulaId = 872u;
        private const uint GuildCreateCostFormulaId = 764u;
        private const uint CommunityCreateCostFormulaId = 1159u;

        [Theory]
        [InlineData(GuildType.Guild, 11u, GuildMinimumLevelFormulaId, 12u)]
        [InlineData(GuildType.WarParty, 49u, WarPartyMinimumLevelFormulaId, 50u)]
        public void BelowMinimumLevel_IsRejectedBeforeAffordabilityOrGuildAccess(
            GuildType guildType,
            uint level,
            uint formulaId,
            uint minimumLevel)
        {
            RegisterFixture fixture = CreateFixture(
                level,
                new GameFormulaEntry { Id = formulaId, Dataint0 = minimumLevel },
                new GameFormulaEntry { Id = GuildCreateCostFormulaId, Dataint0 = 7u });
            ServerGuildResult result = null;
            CaptureResult(fixture.Session, value => result = value);

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(guildType));

            Assert.NotNull(result);
            Assert.Equal(GuildResult.NotHighEnoughLevel, result.Result);
            Assert.Equal(0ul, result.GuildIdentity.Id);
            Assert.Equal((ushort)0, result.GuildIdentity.RealmId);
            Assert.Equal(0u, result.ReferenceId);
            Assert.Equal(string.Empty, result.TargetName);
            fixture.Session.VerifyGet(value => value.Player, Times.Once);
            fixture.Session.Verify(value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()), Times.Once);
            fixture.Player.VerifyGet(value => value.Level, Times.Once);
            fixture.GameTableManager.VerifyGet(value => value.GameFormula, Times.Once);
            fixture.Session.VerifyNoOtherCalls();
            fixture.Player.VerifyNoOtherCalls();
            fixture.GameTableManager.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(GuildType.Guild, GuildCreateCostFormulaId)]
        [InlineData(GuildType.WarParty, GuildCreateCostFormulaId)]
        public void MissingMinimumLevelFormula_FailsClosedBeforeLevelOrGuildAccess(
            GuildType guildType,
            uint unrelatedFormulaId)
        {
            RegisterFixture fixture = CreateFixture(
                50u,
                new GameFormulaEntry { Id = unrelatedFormulaId, Dataint0 = 7u });
            ServerGuildResult result = null;
            CaptureResult(fixture.Session, value => result = value);

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(guildType));

            Assert.NotNull(result);
            Assert.Equal(GuildResult.NotHighEnoughLevel, result.Result);
            fixture.Session.VerifyGet(value => value.Player, Times.Never);
            fixture.Session.Verify(value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()), Times.Once);
            fixture.Player.VerifyGet(value => value.Level, Times.Never);
            fixture.GameTableManager.VerifyGet(value => value.GameFormula, Times.Once);
            fixture.Session.VerifyNoOtherCalls();
            fixture.Player.VerifyNoOtherCalls();
            fixture.GameTableManager.VerifyNoOtherCalls();
        }

        [Fact]
        public void GuildAtMinimumLevel_PreservesCostAndRegistrationFlow()
        {
            const ulong createCost = 7ul;
            RegisterFixture fixture = CreateFixture(
                12u,
                new GameFormulaEntry { Id = GuildMinimumLevelFormulaId, Dataint0 = 12u },
                new GameFormulaEntry { Id = GuildCreateCostFormulaId, Dataint0 = (uint)createCost });
            ClientGuildRegister message = CreateMessage(GuildType.Guild);
            var currencyManager = new Mock<ICurrencyManager>(MockBehavior.Strict);
            currencyManager
                .Setup(value => value.CanAfford(CurrencyType.Credits, createCost))
                .Returns(true);
            var guildManager = new Mock<IGuildManager>(MockBehavior.Strict);
            guildManager
                .Setup(value => value.CanRegisterGuild(message))
                .Returns(new GuildResultInfo(GuildResult.Success));
            guildManager.Setup(value => value.RegisterGuild(message));
            fixture.Player.SetupGet(value => value.CurrencyManager).Returns(currencyManager.Object);
            fixture.Player.SetupGet(value => value.GuildManager).Returns(guildManager.Object);

            fixture.Handler.HandleMessage(fixture.Session.Object, message);

            fixture.Session.VerifyGet(value => value.Player, Times.Exactly(4));
            fixture.Player.VerifyGet(value => value.Level, Times.Once);
            fixture.Player.VerifyGet(value => value.CurrencyManager, Times.Once);
            fixture.Player.VerifyGet(value => value.GuildManager, Times.Exactly(2));
            currencyManager.Verify(value => value.CanAfford(CurrencyType.Credits, createCost), Times.Once);
            guildManager.Verify(value => value.CanRegisterGuild(message), Times.Once);
            guildManager.Verify(value => value.RegisterGuild(message), Times.Once);
            fixture.GameTableManager.VerifyGet(value => value.GameFormula, Times.Exactly(2));
            fixture.Session.VerifyNoOtherCalls();
            fixture.Player.VerifyNoOtherCalls();
            currencyManager.VerifyNoOtherCalls();
            guildManager.VerifyNoOtherCalls();
            fixture.GameTableManager.VerifyNoOtherCalls();
        }

        [Fact]
        public void WarPartyAtMinimumLevel_PreservesRegistrationWithoutCurrencyAccess()
        {
            RegisterFixture fixture = CreateFixture(
                50u,
                new GameFormulaEntry { Id = WarPartyMinimumLevelFormulaId, Dataint0 = 50u });
            ClientGuildRegister message = CreateMessage(GuildType.WarParty);
            var guildManager = new Mock<IGuildManager>(MockBehavior.Strict);
            guildManager
                .Setup(value => value.CanRegisterGuild(message))
                .Returns(new GuildResultInfo(GuildResult.Success));
            guildManager.Setup(value => value.RegisterGuild(message));
            fixture.Player.SetupGet(value => value.GuildManager).Returns(guildManager.Object);

            fixture.Handler.HandleMessage(fixture.Session.Object, message);

            fixture.Session.VerifyGet(value => value.Player, Times.Exactly(3));
            fixture.Player.VerifyGet(value => value.Level, Times.Once);
            fixture.Player.VerifyGet(value => value.GuildManager, Times.Exactly(2));
            guildManager.Verify(value => value.CanRegisterGuild(message), Times.Once);
            guildManager.Verify(value => value.RegisterGuild(message), Times.Once);
            fixture.GameTableManager.VerifyGet(value => value.GameFormula, Times.Once);
            fixture.Session.VerifyNoOtherCalls();
            fixture.Player.VerifyNoOtherCalls();
            guildManager.VerifyNoOtherCalls();
            fixture.GameTableManager.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(GuildType.None)]
        [InlineData((GuildType)8)]
        [InlineData((GuildType)9)]
        [InlineData((GuildType)10)]
        [InlineData((GuildType)11)]
        [InlineData((GuildType)12)]
        [InlineData((GuildType)13)]
        [InlineData((GuildType)14)]
        [InlineData((GuildType)15)]
        public void NonClientGuildType_IsRejectedBeforeDependencies(GuildType guildType)
        {
            RegisterFixture fixture = CreateFixture(0u);

            Assert.Throws<InvalidPacketValueException>(() =>
                fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(guildType)));

            fixture.Session.VerifyNoOtherCalls();
            fixture.Player.VerifyNoOtherCalls();
            fixture.GameTableManager.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(GuildType.Circle)]
        [InlineData(GuildType.ArenaTeam2v2)]
        [InlineData(GuildType.ArenaTeam3v3)]
        [InlineData(GuildType.ArenaTeam5v5)]
        public void OtherClientGuildType_HasNoNewMinimumLevelOrFormulaGate(GuildType guildType)
        {
            RegisterFixture fixture = CreateFixture(0u);
            ClientGuildRegister message = CreateMessage(guildType);
            var guildManager = new Mock<IGuildManager>(MockBehavior.Strict);
            guildManager
                .Setup(value => value.CanRegisterGuild(message))
                .Returns(new GuildResultInfo(GuildResult.Success));
            guildManager.Setup(value => value.RegisterGuild(message));
            fixture.Player.SetupGet(value => value.GuildManager).Returns(guildManager.Object);

            fixture.Handler.HandleMessage(fixture.Session.Object, message);

            fixture.Session.VerifyGet(value => value.Player, Times.Exactly(2));
            fixture.Player.VerifyGet(value => value.Level, Times.Never);
            fixture.Player.VerifyGet(value => value.GuildManager, Times.Exactly(2));
            guildManager.Verify(value => value.CanRegisterGuild(message), Times.Once);
            guildManager.Verify(value => value.RegisterGuild(message), Times.Once);
            fixture.GameTableManager.VerifyGet(value => value.GameFormula, Times.Never);
            fixture.Session.VerifyNoOtherCalls();
            fixture.Player.VerifyNoOtherCalls();
            guildManager.VerifyNoOtherCalls();
            fixture.GameTableManager.VerifyNoOtherCalls();
        }

        [Fact]
        public void Community_PreservesExistingCostAndRegistrationFlow()
        {
            const ulong createCost = 50ul;
            RegisterFixture fixture = CreateFixture(
                0u,
                new GameFormulaEntry { Id = CommunityCreateCostFormulaId, Dataint0 = (uint)createCost });
            ClientGuildRegister message = CreateMessage(GuildType.Community);
            var currencyManager = new Mock<ICurrencyManager>(MockBehavior.Strict);
            currencyManager
                .Setup(value => value.CanAfford(CurrencyType.Credits, createCost))
                .Returns(true);
            var guildManager = new Mock<IGuildManager>(MockBehavior.Strict);
            guildManager
                .Setup(value => value.CanRegisterGuild(message))
                .Returns(new GuildResultInfo(GuildResult.Success));
            guildManager.Setup(value => value.RegisterGuild(message));
            fixture.Player.SetupGet(value => value.CurrencyManager).Returns(currencyManager.Object);
            fixture.Player.SetupGet(value => value.GuildManager).Returns(guildManager.Object);

            fixture.Handler.HandleMessage(fixture.Session.Object, message);

            fixture.Session.VerifyGet(value => value.Player, Times.Exactly(3));
            fixture.Player.VerifyGet(value => value.Level, Times.Never);
            fixture.Player.VerifyGet(value => value.CurrencyManager, Times.Once);
            fixture.Player.VerifyGet(value => value.GuildManager, Times.Exactly(2));
            currencyManager.Verify(value => value.CanAfford(CurrencyType.Credits, createCost), Times.Once);
            guildManager.Verify(value => value.CanRegisterGuild(message), Times.Once);
            guildManager.Verify(value => value.RegisterGuild(message), Times.Once);
            fixture.GameTableManager.VerifyGet(value => value.GameFormula, Times.Once);
            fixture.Session.VerifyNoOtherCalls();
            fixture.Player.VerifyNoOtherCalls();
            currencyManager.VerifyNoOtherCalls();
            guildManager.VerifyNoOtherCalls();
            fixture.GameTableManager.VerifyNoOtherCalls();
        }

        private static RegisterFixture CreateFixture(uint level, params GameFormulaEntry[] entries)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(value => value.Level).Returns(level);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(value => value.Player).Returns(player.Object);
            var gameTableManager = new Mock<IGameTableManager>(MockBehavior.Strict);
            gameTableManager.SetupGet(value => value.GameFormula).Returns(CreateGameTable(entries));
            return new RegisterFixture(
                new ClientGuildRegisterHandler(gameTableManager.Object),
                session,
                player,
                gameTableManager);
        }

        private static void CaptureResult(
            Mock<IWorldSession> session,
            Action<ServerGuildResult> capture)
        {
            session
                .Setup(value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                .Callback<IWritable>(message => capture(Assert.IsType<ServerGuildResult>(message)));
        }

        private static ClientGuildRegister CreateMessage(GuildType guildType)
        {
            var message = new ClientGuildRegister();
            typeof(ClientGuildRegister)
                .GetProperty(nameof(ClientGuildRegister.GuildType), BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, guildType);
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

        private sealed record RegisterFixture(
            ClientGuildRegisterHandler Handler,
            Mock<IWorldSession> Session,
            Mock<IPlayer> Player,
            Mock<IGameTableManager> GameTableManager);
    }
}
