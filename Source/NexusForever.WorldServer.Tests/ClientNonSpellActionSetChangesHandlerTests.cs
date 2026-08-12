using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model.Abilities;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Spell;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientNonSpellActionSetChangesHandlerTests
    {
        [Theory]
        [InlineData(48u)]
        [InlineData(63u)]
        public void OutOfRangeActionBarIndex_IsRejectedBeforeValidationOrAuthority(uint actionBarIndex)
        {
            ActionSetFixture fixture = CreateFixture();

            Assert.Throws<InvalidPacketValueException>(() => fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage((UILocation)actionBarIndex, ShortcutType.BagItem, 1u, 0)));

            AssertNoValidationOrAuthority(fixture);
        }

        [Theory]
        [InlineData((byte)4)]
        [InlineData((byte)15)]
        public void OutOfRangeSpecIndex_IsRejectedBeforeValidationOrAuthority(byte specIndex)
        {
            ActionSetFixture fixture = CreateFixture();

            Assert.Throws<InvalidPacketValueException>(() => fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(UILocation.LAS1, ShortcutType.BagItem, 1u, specIndex)));

            AssertNoValidationOrAuthority(fixture);
        }

        [Theory]
        [InlineData(0u, (byte)0)]
        [InlineData(47u, (byte)3)]
        public void BoundaryAdd_IsForwardedUnchanged(uint actionBarIndex, byte specIndex)
        {
            const uint objectId = 123u;
            ActionSetFixture fixture = CreateFixture();
            UILocation location = (UILocation)actionBarIndex;

            fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(location, ShortcutType.Macro, objectId, specIndex));

            fixture.SpellManager.Verify(manager => manager.GetActionSet(specIndex), Times.Once);
            fixture.ActionSet.Verify(actionSet => actionSet.AddShortcut(
                location,
                ShortcutType.Macro,
                objectId,
                0), Times.Once);
            fixture.ActionSet.Verify(actionSet => actionSet.RemoveShortcut(It.IsAny<UILocation>()), Times.Never);
            fixture.GameTableManager.VerifyGet(manager => manager.Item, Times.Never);
        }

        [Fact]
        public void ValidRemoval_IsForwardedUnchanged()
        {
            const byte specIndex = 2;
            ActionSetFixture fixture = CreateFixture();

            fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(UILocation.Gadget, ShortcutType.None, 0u, specIndex));

            fixture.SpellManager.Verify(manager => manager.GetActionSet(specIndex), Times.Once);
            fixture.ActionSet.Verify(actionSet => actionSet.RemoveShortcut(UILocation.Gadget), Times.Once);
            fixture.ActionSet.Verify(actionSet => actionSet.AddShortcut(
                It.IsAny<UILocation>(),
                It.IsAny<ShortcutType>(),
                It.IsAny<uint>(),
                It.IsAny<byte>()), Times.Never);
            fixture.GameTableManager.VerifyGet(manager => manager.Item, Times.Never);
        }

        private static ActionSetFixture CreateFixture()
        {
            var actionSet = new Mock<IActionSet>();

            var spellManager = new Mock<ISpellManager>();
            spellManager
                .Setup(manager => manager.GetActionSet(It.IsAny<byte>()))
                .Returns(actionSet.Object);

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.SpellManager).Returns(spellManager.Object);

            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            var gameTableManager = new Mock<IGameTableManager>();

            return new ActionSetFixture(
                new ClientNonSpellActionSetChangesHandler(gameTableManager.Object),
                session,
                player,
                spellManager,
                actionSet,
                gameTableManager);
        }

        private static ClientNonSpellActionSetChanges CreateMessage(
            UILocation actionBarIndex,
            ShortcutType shortcutType,
            uint objectId,
            byte specIndex)
        {
            var message = new ClientNonSpellActionSetChanges();
            SetProperty(message, nameof(ClientNonSpellActionSetChanges.ActionBarIndex), actionBarIndex);
            SetProperty(message, nameof(ClientNonSpellActionSetChanges.ShortcutType), shortcutType);
            SetProperty(message, nameof(ClientNonSpellActionSetChanges.ObjectId), objectId);
            SetProperty(message, nameof(ClientNonSpellActionSetChanges.SpecIndex), specIndex);
            return message;
        }

        private static void SetProperty<T>(object target, string name, T value)
        {
            target.GetType()
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                ?.SetValue(target, value);
        }

        private static void AssertNoValidationOrAuthority(ActionSetFixture fixture)
        {
            fixture.GameTableManager.VerifyGet(manager => manager.Item, Times.Never);
            fixture.Session.VerifyGet(session => session.Player, Times.Never);
            fixture.Player.VerifyGet(player => player.SpellManager, Times.Never);
            fixture.SpellManager.Verify(manager => manager.GetActionSet(It.IsAny<byte>()), Times.Never);
            fixture.ActionSet.Verify(actionSet => actionSet.AddShortcut(
                It.IsAny<UILocation>(),
                It.IsAny<ShortcutType>(),
                It.IsAny<uint>(),
                It.IsAny<byte>()), Times.Never);
            fixture.ActionSet.Verify(actionSet => actionSet.RemoveShortcut(It.IsAny<UILocation>()), Times.Never);
        }

        private sealed record ActionSetFixture(
            ClientNonSpellActionSetChangesHandler Handler,
            Mock<IWorldSession> Session,
            Mock<IPlayer> Player,
            Mock<ISpellManager> SpellManager,
            Mock<IActionSet> ActionSet,
            Mock<IGameTableManager> GameTableManager);
    }
}
