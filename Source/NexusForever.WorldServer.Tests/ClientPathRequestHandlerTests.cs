using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using NexusForever.Game.Abstract.Account;
using NexusForever.Game.Abstract.Account.Currency;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.AccountInventory;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model.PlayerPath;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Path;
using Path = NexusForever.Game.Static.PlayerPath.Path;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientPathRequestHandlerTests
    {
        [Theory]
        [InlineData((Path)4)]
        [InlineData((Path)5)]
        [InlineData((Path)6)]
        [InlineData((Path)7)]
        public void Unlock_ReservedThreeBitPath_IsRejectedBeforeAuthorityOrMutation(Path path)
        {
            PathFixture fixture = CreateFixture();

            Assert.Throws<InvalidPacketValueException>(() =>
                fixture.UnlockHandler.HandleMessage(fixture.Session.Object, CreateUnlockRequest(path)));

            AssertNoAuthorityOrMutation(fixture);
        }

        [Theory]
        [InlineData((Path)4)]
        [InlineData((Path)5)]
        [InlineData((Path)6)]
        [InlineData((Path)7)]
        public void Change_ReservedThreeBitPath_IsRejectedBeforeAuthorityOrMutation(Path path)
        {
            PathFixture fixture = CreateFixture();

            Assert.Throws<InvalidPacketValueException>(() =>
                fixture.ChangeHandler.HandleMessage(fixture.Session.Object, CreateChangeRequest(path)));

            AssertNoAuthorityOrMutation(fixture);
        }

        [Theory]
        [InlineData(Path.Soldier)]
        [InlineData(Path.Settler)]
        [InlineData(Path.Scientist)]
        [InlineData(Path.Explorer)]
        public void Unlock_PlayablePath_PreservesExistingFlow(Path path)
        {
            PathFixture fixture = CreateFixture();
            fixture.AccountCurrencyManager
                .Setup(manager => manager.CanAfford(AccountCurrencyType.ServiceToken, 25uL))
                .Returns(true);
            fixture.PathManager.Setup(manager => manager.IsPathUnlocked(path)).Returns(false);

            fixture.UnlockHandler.HandleMessage(fixture.Session.Object, CreateUnlockRequest(path));

            fixture.PathManager.Verify(manager => manager.UnlockPath(path), Times.Once);
            fixture.AccountCurrencyManager.Verify(manager => manager.CurrencySubtractAmount(
                AccountCurrencyType.ServiceToken,
                25uL,
                0uL), Times.Once);
        }

        [Theory]
        [InlineData(Path.Soldier)]
        [InlineData(Path.Settler)]
        [InlineData(Path.Scientist)]
        [InlineData(Path.Explorer)]
        public void Change_PlayablePath_PreservesExistingFlow(Path path)
        {
            PathFixture fixture = CreateFixture();
            fixture.Player.SetupGet(player => player.PathActivatedTime).Returns(DateTime.UnixEpoch);
            fixture.PathManager.Setup(manager => manager.IsPathUnlocked(path)).Returns(true);
            fixture.PathManager.Setup(manager => manager.IsPathActive(path)).Returns(false);

            fixture.ChangeHandler.HandleMessage(fixture.Session.Object, CreateChangeRequest(path));

            fixture.PathManager.Verify(manager => manager.ActivatePath(path), Times.Once);
            fixture.AccountCurrencyManager.Verify(manager => manager.CurrencySubtractAmount(
                It.IsAny<AccountCurrencyType>(),
                It.IsAny<ulong>(),
                It.IsAny<ulong>()), Times.Never);
        }

        private static PathFixture CreateFixture()
        {
            var pathManager = new Mock<IPathManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.PathManager).Returns(pathManager.Object);

            var accountCurrencyManager = new Mock<IAccountCurrencyManager>();
            var account = new Mock<IAccount>();
            account.SetupGet(value => value.CurrencyManager).Returns(accountCurrencyManager.Object);

            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);
            session.SetupGet(value => value.Account).Returns(account.Object);

            var gameTableManager = new Mock<IGameTableManager>();
            gameTableManager.SetupGet(value => value.GameFormula).Returns(CreateGameTable(
                new GameFormulaEntry { Id = 2365u, Dataint0 = 25u },
                new GameFormulaEntry { Id = 2366u, Dataint0 = 3600u, Dataint01 = 10u }));

            return new PathFixture(
                new ClientPathUnlockHandler(gameTableManager.Object),
                new ClientPathChangeRequestHandler(gameTableManager.Object),
                session,
                player,
                accountCurrencyManager,
                pathManager,
                gameTableManager);
        }

        private static ClientPathUnlockRequest CreateUnlockRequest(Path path)
        {
            var request = new ClientPathUnlockRequest();
            SetProperty(request, nameof(ClientPathUnlockRequest.Path), path);
            return request;
        }

        private static ClientPathChangeRequest CreateChangeRequest(Path path)
        {
            var request = new ClientPathChangeRequest();
            SetProperty(request, nameof(ClientPathChangeRequest.Path), path);
            SetProperty(request, nameof(ClientPathChangeRequest.OnCooldown), false);
            return request;
        }

        private static void SetProperty<T>(object target, string name, T value)
        {
            target.GetType()
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                ?.SetValue(target, value);
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

        private static void AssertNoAuthorityOrMutation(PathFixture fixture)
        {
            fixture.GameTableManager.VerifyGet(manager => manager.GameFormula, Times.Never);
            fixture.Session.VerifyGet(session => session.Player, Times.Never);
            fixture.Session.VerifyGet(session => session.Account, Times.Never);
            fixture.PathManager.VerifyNoOtherCalls();
            fixture.AccountCurrencyManager.VerifyNoOtherCalls();
        }

        private sealed record PathFixture(
            ClientPathUnlockHandler UnlockHandler,
            ClientPathChangeRequestHandler ChangeHandler,
            Mock<IWorldSession> Session,
            Mock<IPlayer> Player,
            Mock<IAccountCurrencyManager> AccountCurrencyManager,
            Mock<IPathManager> PathManager,
            Mock<IGameTableManager> GameTableManager);
    }
}
