using System.Reflection;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Abstract.Character;
using NexusForever.Game.Abstract.Customisation;
using NexusForever.Game.Static;
using NexusForever.Game.Static.AccountInventory;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Reputation;
using NexusForever.GameTable;
using NexusForever.GameTable.Text.Filter;
using NexusForever.GameTable.Text.Static;
using NexusForever.Network.World.Message.Model.Pregame;
using NexusForever.Network.World.Message.Static;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Character;

namespace NexusForever.WorldServer.Tests.Character
{
    public class CharacterCreateHandlerTests
    {
        [Theory]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        public void NonPlayablePath_IsRejectedBeforeDownstreamWork(byte path)
        {
            CharacterCreateFixture fixture = CreateFixture();

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(path));

            fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.Is<ServerCharacterCreate>(message => message.Result == CharacterModifyResult.CreateFailed)),
                Times.Once);
            fixture.TextFilterManager.VerifyNoOtherCalls();
            fixture.DatabaseManager.Verify(manager => manager.GetDatabase<CharacterDatabase>(), Times.Never);
            fixture.GameTableManager.VerifyGet(manager => manager.CharacterCreation, Times.Never);
            fixture.CustomisationManager.Verify(manager => manager.Validate(
                It.IsAny<Race>(),
                It.IsAny<Sex>(),
                It.IsAny<Faction>(),
                It.IsAny<IList<(uint Label, uint Value)>>()), Times.Never);
            fixture.CharacterManager.VerifyGet(manager => manager.NextCharacterId, Times.Never);
            fixture.Session.VerifyGet(session => session.Account, Times.Never);
            fixture.Session.VerifyGet(session => session.Events, Times.Never);
            fixture.Session.VerifySet(session => session.CanProcessIncomingPackets = It.IsAny<bool>(), Times.Never);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void PlayablePath_ReachesExistingNameValidation(byte path)
        {
            CharacterCreateFixture fixture = CreateFixture();
            fixture.TextFilterManager
                .Setup(manager => manager.IsTextValid(
                    "ValidName",
                    TextFilterClass.Strict,
                    Language.English))
                .Returns(false);

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(path));

            fixture.TextFilterManager.Verify(manager => manager.IsTextValid(
                "ValidName",
                TextFilterClass.Strict,
                Language.English), Times.Once);
            fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.Is<ServerCharacterCreate>(message => message.Result == CharacterModifyResult.CreateFailed_InvalidName)),
                Times.Once);
        }

        private static CharacterCreateFixture CreateFixture()
        {
            var textFilterManager = new Mock<ITextFilterManager>();
            var databaseManager = new Mock<IDatabaseManager>();
            var gameTableManager = new Mock<IGameTableManager>();
            var customisationManager = new Mock<ICustomisationManager>();
            var characterManager = new Mock<ICharacterManager>();
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.IsQueued).Returns(false);

            return new CharacterCreateFixture(
                new ClientCharacterCreateHandler(
                    textFilterManager.Object,
                    databaseManager.Object,
                    gameTableManager.Object,
                    customisationManager.Object,
                    characterManager.Object),
                session,
                textFilterManager,
                databaseManager,
                gameTableManager,
                customisationManager,
                characterManager);
        }

        private static ClientCharacterCreate CreateMessage(byte path)
        {
            var message = new ClientCharacterCreate();
            SetProperty(message, nameof(ClientCharacterCreate.Name), "ValidName");
            SetProperty(message, nameof(ClientCharacterCreate.Path), path);
            return message;
        }

        private static void SetProperty<T>(ClientCharacterCreate message, string name, T value)
        {
            typeof(ClientCharacterCreate)
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, value);
        }

        private sealed record CharacterCreateFixture(
            ClientCharacterCreateHandler Handler,
            Mock<IWorldSession> Session,
            Mock<ITextFilterManager> TextFilterManager,
            Mock<IDatabaseManager> DatabaseManager,
            Mock<IGameTableManager> GameTableManager,
            Mock<ICustomisationManager> CustomisationManager,
            Mock<ICharacterManager> CharacterManager);
    }
}
