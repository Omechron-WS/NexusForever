using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model.Abilities;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Spell;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientRequestActionSetChangesHandlerTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(11)]
        [InlineData(13)]
        [InlineData(15)]
        public void NonRetailActionCount_IsRejectedBeforePlayerAccess(int actionCount)
        {
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientRequestActionSetChangesHandler();

            Assert.Throws<InvalidPacketValueException>(() =>
                handler.HandleMessage(session.Object, CreateMessage(0, actionCount)));

            session.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData((byte)4)]
        [InlineData((byte)7)]
        public void UnconstructedSpec_IsRejectedBeforePlayerAccess(byte actionSetIndex)
        {
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientRequestActionSetChangesHandler();

            Assert.Throws<InvalidPacketValueException>(() =>
                handler.HandleMessage(session.Object, CreateMessage(actionSetIndex, 12)));

            session.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData((byte)0, (byte)1)]
        [InlineData((byte)3, (byte)2)]
        public void InactiveSpec_IsRejectedBeforeActionSetAccessOrMutation(
            byte actionSetIndex,
            byte activeActionSet)
        {
            var spellManager = new Mock<ISpellManager>(MockBehavior.Strict);
            spellManager.SetupGet(value => value.ActiveActionSet).Returns(activeActionSet);
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(value => value.SpellManager).Returns(spellManager.Object);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(value => value.Player).Returns(player.Object);
            var handler = new ClientRequestActionSetChangesHandler();

            Assert.Throws<InvalidPacketValueException>(() =>
                handler.HandleMessage(session.Object, CreateMessage(actionSetIndex, 12)));

            session.VerifyGet(value => value.Player, Times.Once);
            player.VerifyGet(value => value.SpellManager, Times.Once);
            spellManager.VerifyGet(value => value.ActiveActionSet, Times.Once);
            spellManager.Verify(value => value.GetActionSet(It.IsAny<byte>()), Times.Never);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            spellManager.VerifyNoOtherCalls();
        }

        [Fact]
        public void RetailShapeForCurrentSpec_PreservesMutationAndPublicationFlow()
        {
            const byte specIndex = 3;
            const uint spellId = 123u;
            const ushort ampId = 77;
            var actionSetResponse = new ServerActionSet();
            var ampResponse = new ServerAmpList();

            var actionSet = new Mock<IActionSet>(MockBehavior.Strict);
            actionSet.SetupGet(value => value.Actions).Returns(Array.Empty<IActionSetShortcut>());
            actionSet.Setup(value => value.GetShortcut(It.IsAny<UILocation>())).Returns((IActionSetShortcut)null);
            actionSet.Setup(value => value.AddShortcut(
                UILocation.LAS1,
                ShortcutType.SpellbookItem,
                spellId,
                1));
            actionSet.Setup(value => value.BuildServerActionSet()).Returns(actionSetResponse);
            actionSet.SetupGet(value => value.Amps).Returns(Array.Empty<IActionSetAmp>());
            actionSet.Setup(value => value.AddAmp(ampId));
            actionSet.Setup(value => value.BuildServerAmpList()).Returns(ampResponse);

            var spellManager = new Mock<ISpellManager>(MockBehavior.Strict);
            spellManager.SetupGet(value => value.ActiveActionSet).Returns(specIndex);
            spellManager.Setup(value => value.GetActionSet(specIndex)).Returns(actionSet.Object);
            spellManager.Setup(value => value.UpdateSpell(spellId, 2, specIndex));
            spellManager.Setup(value => value.SendServerAbilityPoints());

            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(value => value.SpellManager).Returns(spellManager.Object);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(value => value.Player).Returns(player.Object);
            session.Setup(value => value.EnqueueMessageEncrypted(actionSetResponse));
            session.Setup(value => value.EnqueueMessageEncrypted(ampResponse));
            var handler = new ClientRequestActionSetChangesHandler();
            ClientRequestActionSetChanges message = CreateMessage(specIndex, 12);
            message.Actions[0] = spellId;
            message.ActionTiers.Add(CreateActionTier(spellId, 2));
            message.Amps.Add(ampId);

            handler.HandleMessage(session.Object, message);

            session.VerifyGet(value => value.Player, Times.Once);
            player.VerifyGet(value => value.SpellManager, Times.Once);
            spellManager.VerifyGet(value => value.ActiveActionSet, Times.Once);
            spellManager.Verify(value => value.GetActionSet(specIndex), Times.Once);
            actionSet.VerifyGet(value => value.Actions, Times.Once);
            for (UILocation location = 0; location < (UILocation)12; location++)
                actionSet.Verify(value => value.GetShortcut(location), Times.Once);
            actionSet.Verify(value => value.AddShortcut(
                UILocation.LAS1,
                ShortcutType.SpellbookItem,
                spellId,
                1), Times.Once);
            spellManager.Verify(value => value.UpdateSpell(spellId, 2, specIndex), Times.Once);
            actionSet.Verify(value => value.BuildServerActionSet(), Times.Once);
            session.Verify(value => value.EnqueueMessageEncrypted(actionSetResponse), Times.Once);
            spellManager.Verify(value => value.SendServerAbilityPoints(), Times.Once);
            actionSet.VerifyGet(value => value.Amps, Times.Once);
            actionSet.Verify(value => value.AddAmp(ampId), Times.Once);
            actionSet.Verify(value => value.BuildServerAmpList(), Times.Once);
            session.Verify(value => value.EnqueueMessageEncrypted(ampResponse), Times.Once);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            spellManager.VerifyNoOtherCalls();
            actionSet.VerifyNoOtherCalls();
        }

        private static ClientRequestActionSetChanges CreateMessage(byte actionSetIndex, int actionCount)
        {
            var message = new ClientRequestActionSetChanges();
            typeof(ClientRequestActionSetChanges)
                .GetProperty(
                    nameof(ClientRequestActionSetChanges.ActionSetIndex),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, actionSetIndex);
            message.Actions.AddRange(Enumerable.Repeat(0u, actionCount));
            return message;
        }

        private static ClientRequestActionSetChanges.ActionTier CreateActionTier(uint action, byte tier)
        {
            var actionTier = new ClientRequestActionSetChanges.ActionTier();
            typeof(ClientRequestActionSetChanges.ActionTier)
                .GetProperty(
                    nameof(ClientRequestActionSetChanges.ActionTier.Action),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(actionTier, action);
            typeof(ClientRequestActionSetChanges.ActionTier)
                .GetProperty(
                    nameof(ClientRequestActionSetChanges.ActionTier.Tier),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(actionTier, tier);
            return actionTier;
        }
    }
}
