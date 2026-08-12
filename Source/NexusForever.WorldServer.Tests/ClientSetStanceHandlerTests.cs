using System.Reflection;
using System.Runtime.CompilerServices;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Abilities;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Spell;
using Moq;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientSetStanceHandlerTests
    {
        private const byte InitialInnateIndex = 2;

        [Theory]
        [InlineData(Class.Warrior, 0, 0u)]
        [InlineData(Class.Warrior, 1, 39778u)]
        [InlineData(Class.Engineer, 0, 0u)]
        [InlineData(Class.Engineer, 1, 39778u)]
        [InlineData(Class.Esper, 0, 0u)]
        [InlineData(Class.Medic, 0, 0u)]
        [InlineData(Class.Stalker, 0, 0u)]
        [InlineData(Class.Stalker, 1, 39778u)]
        [InlineData(Class.Stalker, 2, 39779u)]
        [InlineData(Class.Spellslinger, 0, 0u)]
        public void PlayableUnlockedSlot_CommitsExactIndexAndPublishes(
            Class playerClass,
            byte innateIndex,
            uint prerequisiteId)
        {
            StanceFixture fixture = CreateFixture(playerClass, CreateBuildClassEntry(playerClass));
            if (prerequisiteId != 0u)
            {
                fixture.PrerequisiteManager
                    .Setup(manager => manager.Meets(fixture.Player.Object, prerequisiteId))
                    .Returns(true);
            }

            fixture.Handle(innateIndex);

            fixture.Player.VerifySet(player => player.InnateIndex = innateIndex, Times.Once);
            fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.Is<ServerStanceChanged>(message => message.InnateIndex == innateIndex)), Times.Once);
            fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.IsAny<IWritable>()), Times.Once);
            if (prerequisiteId == 0u)
            {
                fixture.PrerequisiteManager.Verify(manager => manager.Meets(
                    It.IsAny<IPlayer>(), It.IsAny<uint>()), Times.Never);
            }
            else
            {
                fixture.PrerequisiteManager.Verify(manager => manager.Meets(
                    fixture.Player.Object, prerequisiteId), Times.Once);
            }
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(255)]
        public void ReservedEightBitIndex_IsRejectedBeforeAuthorityLookupOrMutation(byte innateIndex)
        {
            StanceFixture fixture = CreateFixture(Class.Stalker, CreateBuildClassEntry(Class.Stalker));

            Assert.Throws<InvalidPacketValueException>(() => fixture.Handle(innateIndex));

            AssertNoMutation(fixture);
            fixture.GameTableManager.VerifyGet(manager => manager.Class, Times.Never);
            AssertNoPrerequisiteEvaluation(fixture);
        }

        [Theory]
        [InlineData(Class.Warrior, 2)]
        [InlineData(Class.Engineer, 2)]
        [InlineData(Class.Esper, 1)]
        [InlineData(Class.Esper, 2)]
        [InlineData(Class.Medic, 1)]
        [InlineData(Class.Medic, 2)]
        [InlineData(Class.Spellslinger, 1)]
        [InlineData(Class.Spellslinger, 2)]
        public void EmptyClassSlot_IsRejectedBeforePrerequisiteOrMutation(
            Class playerClass,
            byte innateIndex)
        {
            StanceFixture fixture = CreateFixture(playerClass, CreateBuildClassEntry(playerClass));

            Assert.Throws<InvalidPacketValueException>(() => fixture.Handle(innateIndex));

            AssertNoMutation(fixture);
            AssertNoPrerequisiteEvaluation(fixture);
        }

        [Theory]
        [InlineData(Class.Warrior, 1, 39778u)]
        [InlineData(Class.Stalker, 2, 39779u)]
        public void LockedClassSlot_IsRejectedBeforeMutationOrPublication(
            Class playerClass,
            byte innateIndex,
            uint prerequisiteId)
        {
            StanceFixture fixture = CreateFixture(playerClass, CreateBuildClassEntry(playerClass));
            fixture.PrerequisiteManager
                .Setup(manager => manager.Meets(fixture.Player.Object, prerequisiteId))
                .Returns(false);

            Assert.Throws<InvalidPacketValueException>(() => fixture.Handle(innateIndex));

            fixture.PrerequisiteManager.Verify(manager => manager.Meets(
                fixture.Player.Object, prerequisiteId), Times.Once);
            AssertNoMutation(fixture);
        }

        [Fact]
        public void MissingClassRow_IsRejectedBeforeMutationOrPublication()
        {
            StanceFixture fixture = CreateFixture(Class.Warrior);

            Assert.Throws<InvalidPacketValueException>(() => fixture.Handle(0));

            AssertNoMutation(fixture);
            AssertNoPrerequisiteEvaluation(fixture);
        }

        [Fact]
        public void MalformedClassRow_IsRejectedBeforeMutationOrPublication()
        {
            ClassEntry classEntry = CreateBuildClassEntry(Class.Warrior);
            classEntry.PrerequisiteIdInnateAbility = [0u, 39778u];
            StanceFixture fixture = CreateFixture(Class.Warrior, classEntry);

            Assert.Throws<InvalidPacketValueException>(() => fixture.Handle(0));

            AssertNoMutation(fixture);
            AssertNoPrerequisiteEvaluation(fixture);
        }

        [Fact]
        public void PrerequisiteEvaluationFailure_PropagatesBeforeMutationOrPublication()
        {
            StanceFixture fixture = CreateFixture(Class.Warrior, CreateBuildClassEntry(Class.Warrior));
            fixture.PrerequisiteManager
                .Setup(manager => manager.Meets(fixture.Player.Object, 39778u))
                .Throws(new InvalidOperationException("Test prerequisite failure."));

            Assert.Throws<InvalidOperationException>(() => fixture.Handle(1));

            fixture.PrerequisiteManager.Verify(manager => manager.Meets(
                fixture.Player.Object, 39778u), Times.Once);
            AssertNoMutation(fixture);
        }

        private static StanceFixture CreateFixture(Class playerClass, params ClassEntry[] classEntries)
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Class).Returns(playerClass);
            player.SetupProperty(value => value.InnateIndex, InitialInnateIndex);

            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            var gameTableManager = new Mock<IGameTableManager>();
            gameTableManager
                .SetupGet(value => value.Class)
                .Returns(CreateGameTable(classEntries));

            var prerequisiteManager = new Mock<IPrerequisiteManager>();
            var handler = new ClientSetStanceHandler(
                gameTableManager.Object,
                prerequisiteManager.Object);
            return new StanceFixture(
                handler,
                session,
                player,
                gameTableManager,
                prerequisiteManager);
        }

        private static ClassEntry CreateBuildClassEntry(Class playerClass)
        {
            return playerClass switch
            {
                Class.Warrior => CreateClassEntry(
                    playerClass,
                    [46867u, 47022u, 0u],
                    [44926u, 44932u, 0u],
                    [0u, 39778u, 0u]),
                Class.Engineer => CreateClassEntry(
                    playerClass,
                    [47866u, 47881u, 0u],
                    [45000u, 41504u, 0u],
                    [0u, 39778u, 0u]),
                Class.Esper => CreateClassEntry(
                    playerClass,
                    [58586u, 0u, 0u],
                    [0u, 0u, 0u],
                    [0u, 0u, 0u]),
                Class.Medic => CreateClassEntry(
                    playerClass,
                    [69450u, 0u, 0u],
                    [0u, 0u, 0u],
                    [0u, 0u, 0u]),
                Class.Stalker => CreateClassEntry(
                    playerClass,
                    [46073u, 46074u, 38782u],
                    [45057u, 45058u, 46059u],
                    [0u, 39778u, 39779u]),
                Class.Spellslinger => CreateClassEntry(
                    playerClass,
                    [47273u, 0u, 0u],
                    [0u, 0u, 0u],
                    [0u, 0u, 0u]),
                _ => throw new ArgumentOutOfRangeException(nameof(playerClass))
            };
        }

        private static ClassEntry CreateClassEntry(
            Class playerClass,
            uint[] activeSpells,
            uint[] passiveSpells,
            uint[] prerequisites)
        {
            return new ClassEntry
            {
                Id = (uint)playerClass,
                Spell4IdInnateAbilityActive = activeSpells,
                Spell4IdInnateAbilityPassive = passiveSpells,
                PrerequisiteIdInnateAbility = prerequisites
            };
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

        private static ClientSetStance CreateMessage(byte innateIndex)
        {
            var message = new ClientSetStance();
            typeof(ClientSetStance)
                .GetProperty(
                    nameof(ClientSetStance.InnateIndex),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, innateIndex);
            return message;
        }

        private static void AssertNoMutation(StanceFixture fixture)
        {
            Assert.Equal(InitialInnateIndex, fixture.Player.Object.InnateIndex);
            fixture.Player.VerifySet(player => player.InnateIndex = It.IsAny<byte>(), Times.Never);
            fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.IsAny<IWritable>()), Times.Never);
        }

        private static void AssertNoPrerequisiteEvaluation(StanceFixture fixture)
        {
            fixture.PrerequisiteManager.Verify(manager => manager.Meets(
                It.IsAny<IPlayer>(), It.IsAny<uint>()), Times.Never);
        }

        private sealed record StanceFixture(
            ClientSetStanceHandler Handler,
            Mock<IWorldSession> Session,
            Mock<IPlayer> Player,
            Mock<IGameTableManager> GameTableManager,
            Mock<IPrerequisiteManager> PrerequisiteManager)
        {
            public void Handle(byte innateIndex)
            {
                Handler.HandleMessage(Session.Object, CreateMessage(innateIndex));
            }
        }
    }
}
