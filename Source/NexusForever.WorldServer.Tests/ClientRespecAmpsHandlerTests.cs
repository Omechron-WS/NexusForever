using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Abilities;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Abilities;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Spell;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientRespecAmpsHandlerTests
    {
        [Theory]
        [InlineData((byte)4)]
        [InlineData((byte)7)]
        public void OutOfRangeSpec_RejectsBeforeTableOrPlayerAccess(byte specIndex)
        {
            RespecAmpsFixture fixture = CreateFixture();

            Assert.Throws<InvalidPacketValueException>(() => fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(specIndex, AmpRespecType.Full, 0u)));

            AssertNoTableOrPlayerAccess(fixture);
            AssertNoMutationOrPublication(fixture);
        }

        [Theory]
        [InlineData(3, 0u)]
        [InlineData(7, uint.MaxValue)]
        [InlineData((int)AmpRespecType.Section, 0u)]
        [InlineData((int)AmpRespecType.Single, 0u)]
        public void InvalidTypeOrValueShape_RejectsBeforeTableOrPlayerAccess(
            int respecType,
            uint value)
        {
            RespecAmpsFixture fixture = CreateFixture();

            Assert.Throws<InvalidPacketValueException>(() => fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(0, (AmpRespecType)respecType, value)));

            AssertNoTableOrPlayerAccess(fixture);
            AssertNoMutationOrPublication(fixture);
        }

        [Theory]
        [InlineData(65536u)]
        [InlineData(65537u)]
        [InlineData(uint.MaxValue)]
        public void SingleValueAboveUShort_RejectsBeforeTableOrAliasedAmpLookup(uint value)
        {
            RespecAmpsFixture fixture = CreateFixture();

            Assert.Throws<InvalidPacketValueException>(() => fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(0, AmpRespecType.Single, value)));

            AssertNoTableOrPlayerAccess(fixture);
            fixture.ActionSet.Verify(actionSet => actionSet.GetAmp(It.IsAny<ushort>()), Times.Never);
            AssertNoMutationOrPublication(fixture);
        }

        [Theory]
        [InlineData((int)AmpRespecType.Section, 7u)]
        [InlineData((int)AmpRespecType.Section, uint.MaxValue)]
        [InlineData((int)AmpRespecType.Single, 977u)]
        [InlineData((int)AmpRespecType.Single, 65535u)]
        public void MissingExactTableRow_RejectsBeforePlayerAccess(int respecType, uint value)
        {
            RespecAmpsFixture fixture = CreateFixture();
            AmpRespecType type = (AmpRespecType)respecType;

            Assert.Throws<InvalidPacketValueException>(() => fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(0, type, value)));

            VerifyExactTableAccess(fixture, type);
            fixture.Session.VerifyGet(session => session.Player, Times.Never);
            fixture.Player.VerifyGet(player => player.SpellManager, Times.Never);
            AssertNoMutationOrPublication(fixture);
        }

        [Theory]
        [InlineData((int)AmpRespecType.Full, 0u)]
        [InlineData((int)AmpRespecType.Section, 1u)]
        [InlineData((int)AmpRespecType.Single, 1u)]
        public void InactiveSpec_RejectsBeforeActionSetAccess(int respecType, uint value)
        {
            const byte requestedSpec = 3;
            RespecAmpsFixture fixture = CreateFixture(activeActionSet: 2, ownedAmpId: 1u);
            AmpRespecType type = (AmpRespecType)respecType;

            Assert.Throws<InvalidPacketValueException>(() => fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(requestedSpec, type, value)));

            VerifyExactTableAccess(fixture, type);
            fixture.SpellManager.VerifyGet(manager => manager.ActiveActionSet, Times.Once);
            fixture.SpellManager.Verify(manager => manager.GetActionSet(It.IsAny<byte>()), Times.Never);
            AssertNoMutationOrPublication(fixture);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(1u)]
        public void SingleAmpNotExactlyOwned_RejectsBeforeMutationOrPublication(uint? ownedAmpId)
        {
            const uint requestedAmpId = 976u;
            RespecAmpsFixture fixture = CreateFixture(
                activeActionSet: 0,
                ownedAmpLookupId: requestedAmpId,
                ownedAmpId: ownedAmpId);

            Assert.Throws<InvalidPacketValueException>(() => fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(0, AmpRespecType.Single, requestedAmpId)));

            VerifyExactTableAccess(fixture, AmpRespecType.Single);
            fixture.SpellManager.Verify(manager => manager.GetActionSet(0), Times.Once);
            fixture.ActionSet.Verify(actionSet => actionSet.GetAmp((ushort)requestedAmpId), Times.Once);
            AssertNoMutationOrPublication(fixture);
        }

        [Theory]
        [InlineData((byte)0, (int)AmpRespecType.Full, 0u)]
        [InlineData((byte)3, (int)AmpRespecType.Full, 1u)]
        [InlineData((byte)3, (int)AmpRespecType.Full, uint.MaxValue)]
        [InlineData((byte)3, (int)AmpRespecType.Section, 1u)]
        [InlineData((byte)3, (int)AmpRespecType.Section, 6u)]
        [InlineData((byte)0, (int)AmpRespecType.Single, 1u)]
        [InlineData((byte)3, (int)AmpRespecType.Single, 976u)]
        public void ValidRequest_PreservesExactRemovalBuildAndEnqueue(
            byte specIndex,
            int respecType,
            uint value)
        {
            AmpRespecType type = (AmpRespecType)respecType;
            RespecAmpsFixture fixture = CreateFixture(
                activeActionSet: specIndex,
                ownedAmpLookupId: type == AmpRespecType.Single ? value : null,
                ownedAmpId: type == AmpRespecType.Single ? value : null);

            fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(specIndex, type, value));

            VerifyExactTableAccess(fixture, type);
            fixture.SpellManager.VerifyGet(manager => manager.ActiveActionSet, Times.Once);
            fixture.SpellManager.Verify(manager => manager.GetActionSet(specIndex), Times.Once);
            fixture.ActionSet.Verify(actionSet => actionSet.GetAmp((ushort)value),
                type == AmpRespecType.Single ? Times.Once() : Times.Never());
            fixture.ActionSet.Verify(actionSet => actionSet.RemoveAmp(type, value), Times.Once);
            fixture.ActionSet.Verify(actionSet => actionSet.BuildServerAmpList(), Times.Once);
            fixture.Session.Verify(session => session.EnqueueMessageEncrypted(fixture.Response), Times.Once);
        }

        private static RespecAmpsFixture CreateFixture(
            byte activeActionSet = 0,
            uint? ownedAmpLookupId = null,
            uint? ownedAmpId = null)
        {
            var response = new ServerAmpList();

            var actionSet = new Mock<IActionSet>();
            actionSet.Setup(value => value.BuildServerAmpList()).Returns(response);

            if (ownedAmpLookupId.HasValue && ownedAmpId.HasValue)
            {
                var ownedAmp = new Mock<IActionSetAmp>();
                ownedAmp.SetupGet(value => value.Entry).Returns(new EldanAugmentationEntry
                {
                    Id = ownedAmpId.Value
                });
                actionSet
                    .Setup(value => value.GetAmp((ushort)ownedAmpLookupId.Value))
                    .Returns(ownedAmp.Object);
            }

            var spellManager = new Mock<ISpellManager>();
            spellManager.SetupGet(value => value.ActiveActionSet).Returns(activeActionSet);
            spellManager
                .Setup(value => value.GetActionSet(It.IsAny<byte>()))
                .Returns(actionSet.Object);

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.SpellManager).Returns(spellManager.Object);

            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            var gameTableManager = new Mock<IGameTableManager>();
            gameTableManager.SetupGet(value => value.EldanAugmentationCategory).Returns(CreateGameTable(
                new EldanAugmentationCategoryEntry { Id = 1u },
                new EldanAugmentationCategoryEntry { Id = 6u }));
            gameTableManager.SetupGet(value => value.EldanAugmentation).Returns(CreateGameTable(
                new EldanAugmentationEntry { Id = 1u },
                new EldanAugmentationEntry { Id = 976u }));

            return new RespecAmpsFixture(
                new ClientRespecAmpsHandler(gameTableManager.Object),
                session,
                player,
                spellManager,
                actionSet,
                gameTableManager,
                response);
        }

        private static ClientRespecAmps CreateMessage(
            byte specIndex,
            AmpRespecType respecType,
            uint value)
        {
            var message = new ClientRespecAmps();
            SetProperty(message, nameof(ClientRespecAmps.SpecIndex), specIndex);
            SetProperty(message, nameof(ClientRespecAmps.RespecType), respecType);
            SetProperty(message, nameof(ClientRespecAmps.Value), value);
            return message;
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

        private static void VerifyExactTableAccess(RespecAmpsFixture fixture, AmpRespecType type)
        {
            fixture.GameTableManager.VerifyGet(
                manager => manager.EldanAugmentationCategory,
                type == AmpRespecType.Section ? Times.Once() : Times.Never());
            fixture.GameTableManager.VerifyGet(
                manager => manager.EldanAugmentation,
                type == AmpRespecType.Single ? Times.Once() : Times.Never());
        }

        private static void AssertNoTableOrPlayerAccess(RespecAmpsFixture fixture)
        {
            fixture.GameTableManager.VerifyGet(manager => manager.EldanAugmentationCategory, Times.Never);
            fixture.GameTableManager.VerifyGet(manager => manager.EldanAugmentation, Times.Never);
            fixture.Session.VerifyGet(session => session.Player, Times.Never);
            fixture.Player.VerifyGet(player => player.SpellManager, Times.Never);
            fixture.SpellManager.VerifyGet(manager => manager.ActiveActionSet, Times.Never);
            fixture.SpellManager.Verify(manager => manager.GetActionSet(It.IsAny<byte>()), Times.Never);
        }

        private static void AssertNoMutationOrPublication(RespecAmpsFixture fixture)
        {
            fixture.ActionSet.Verify(actionSet => actionSet.RemoveAmp(
                It.IsAny<AmpRespecType>(), It.IsAny<uint>()), Times.Never);
            fixture.ActionSet.Verify(actionSet => actionSet.BuildServerAmpList(), Times.Never);
            fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.IsAny<IWritable>()), Times.Never);
        }

        private sealed record RespecAmpsFixture(
            ClientRespecAmpsHandler Handler,
            Mock<IWorldSession> Session,
            Mock<IPlayer> Player,
            Mock<ISpellManager> SpellManager,
            Mock<IActionSet> ActionSet,
            Mock<IGameTableManager> GameTableManager,
            ServerAmpList Response);
    }
}
