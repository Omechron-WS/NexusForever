using System.Numerics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.Session;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Entity.Model;
using NexusForever.Shared;
using Moq;

namespace NexusForever.Game.Tests.Entity
{
    [Collection(VitalServiceProviderCollection.Name)]
    public sealed class UnitDeathLifecycleTests : IDisposable
    {
        private readonly IServiceProvider previousProvider;
        private readonly ServiceProvider serviceProvider;

        public UnitDeathLifecycleTests()
        {
            previousProvider = LegacyServiceProvider.Provider;

            var entityManager = new EntityManager();
            typeof(EntityManager)
                .GetMethod("InitialiseEntityStats", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(entityManager, null);

            serviceProvider = new ServiceCollection()
                .AddSingleton(entityManager)
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = previousProvider;
            serviceProvider.Dispose();
        }

        [Fact]
        public void ModifyHealth_DuplicateLethalHitsRewardAndScheduleOnce()
        {
            var map = new Mock<IBaseMap>();
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(true);
            var loot = new Mock<ILootInstance>();
            TestUnitEntity entity = CreateEntity(map.Object, 42u, loot.Object);

            entity.ModifyHealth(100u, DamageType.Physical, null);
            entity.ModifyHealth(100u, DamageType.Physical, null);

            Assert.Equal(1, entity.RewardCount);
            Assert.Equal(EntityDeathState.JustDied, entity.StateDuringReward);
            Assert.Equal(EntityDeathState.Corpse, entity.CurrentDeathState);
            map.Verify(world => world.ScheduleRespawn(entity), Times.Once);
        }

        [Fact]
        public void RemoveLoot_FinalPackageStartsFiveSecondCleanup()
        {
            var map = new Mock<IBaseMap>();
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(true);
            var loot = new Mock<ILootInstance>();
            TestUnitEntity entity = CreateEntity(map.Object, 42u, loot.Object);
            entity.ModifyHealth(100u, DamageType.Physical, null);

            entity.RemoveLoot(loot.Object);

            Assert.Equal(EntityDeathState.CorpseLooted, entity.CurrentDeathState);
            entity.Update(4.5d);
            Assert.Equal(EntityDeathState.CorpseLooted, entity.CurrentDeathState);
            map.Verify(world => world.EnqueueRemove(entity), Times.Never);

            entity.Update(0.5d);

            Assert.Equal(EntityDeathState.Dead, entity.CurrentDeathState);
            map.Verify(world => world.EnqueueRemove(entity), Times.Once);

            entity.Update(10d);
            map.Verify(world => world.EnqueueRemove(entity), Times.Once);
        }

        [Fact]
        public void RemoveLoot_UnknownPackageDoesNotStartCleanup()
        {
            var map = new Mock<IBaseMap>();
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(true);
            var loot = new Mock<ILootInstance>();
            TestUnitEntity entity = CreateEntity(map.Object, 42u, loot.Object);
            entity.ModifyHealth(100u, DamageType.Physical, null);

            entity.RemoveLoot(Mock.Of<ILootInstance>());

            Assert.Equal(EntityDeathState.Corpse, entity.CurrentDeathState);
        }

        [Fact]
        public void OnDeath_NoLootStartsFiveSecondCleanup()
        {
            var map = new Mock<IBaseMap>();
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(true);
            TestUnitEntity entity = CreateEntity(map.Object, 42u);

            entity.ModifyHealth(100u, DamageType.Physical, null);

            Assert.Equal(EntityDeathState.CorpseLooted, entity.CurrentDeathState);
            entity.Update(5d);
            Assert.Equal(EntityDeathState.Dead, entity.CurrentDeathState);
            map.Verify(world => world.EnqueueRemove(entity), Times.Once);
        }

        [Fact]
        public void Update_UnlootedCorpseUsesThirtyMinuteTimeoutInsteadOfLegacyTenMinutes()
        {
            var map = new Mock<IBaseMap>();
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(true);
            TestUnitEntity entity = CreateEntity(map.Object, 42u, Mock.Of<ILootInstance>());
            entity.ModifyHealth(100u, DamageType.Physical, null);

            entity.Update(600d);

            Assert.Equal(EntityDeathState.Corpse, entity.CurrentDeathState);
            map.Verify(world => world.EnqueueRemove(entity), Times.Never);

            entity.Update(1199.5d);
            Assert.Equal(EntityDeathState.Corpse, entity.CurrentDeathState);

            entity.Update(0.5d);

            Assert.Equal(EntityDeathState.Dead, entity.CurrentDeathState);
            map.Verify(world => world.EnqueueRemove(entity), Times.Once);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-1d)]
        [InlineData(0d)]
        public void Update_InvalidElapsedTimeDoesNotAdvanceCorpseTimeout(double elapsed)
        {
            var map = new Mock<IBaseMap>();
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(true);
            TestUnitEntity entity = CreateEntity(map.Object, 42u, Mock.Of<ILootInstance>());
            entity.ModifyHealth(100u, DamageType.Physical, null);

            entity.Update(elapsed);

            Assert.Equal(EntityDeathState.Corpse, entity.CurrentDeathState);
            map.Verify(world => world.EnqueueRemove(entity), Times.Never);
        }

        [Fact]
        public void Update_ExtremeElapsedTimeCleansCorpseOnce()
        {
            var map = new Mock<IBaseMap>();
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(true);
            TestUnitEntity entity = CreateEntity(map.Object, 42u, Mock.Of<ILootInstance>());
            entity.ModifyHealth(100u, DamageType.Physical, null);

            entity.Update(double.MaxValue);
            entity.Update(double.MaxValue);

            Assert.Equal(EntityDeathState.Dead, entity.CurrentDeathState);
            map.Verify(world => world.EnqueueRemove(entity), Times.Once);
        }

        [Fact]
        public void OnDeath_TemporaryEntityCleansUpWithoutRespawn()
        {
            var map = new Mock<IBaseMap>();
            TestUnitEntity entity = CreateEntity(map.Object, 0u);

            entity.ModifyHealth(100u, DamageType.Physical, null);
            entity.Update(5d);

            map.Verify(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>()), Times.Never);
            map.Verify(world => world.EnqueueRemove(entity), Times.Once);
        }

        [Fact]
        public void OnDeath_RejectedRespawnReservationDoesNotBlockCorpseCleanup()
        {
            var map = new Mock<IBaseMap>();
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(false);
            TestUnitEntity entity = CreateEntity(map.Object, 42u);

            entity.ModifyHealth(100u, DamageType.Physical, null);
            entity.Update(5d);

            Assert.Equal(EntityDeathState.Dead, entity.CurrentDeathState);
            map.Verify(world => world.ScheduleRespawn(entity), Times.Once);
            map.Verify(world => world.EnqueueRemove(entity), Times.Once);
        }

        [Fact]
        public void OnDeath_RewardFailureIsContainedAndCannotRetryRewards()
        {
            var map = new Mock<IBaseMap>();
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(true);
            TestUnitEntity entity = CreateEntity(map.Object, 42u);
            entity.ThrowDuringRewards = true;

            entity.ModifyHealth(100u, DamageType.Physical, null);

            Assert.Equal(EntityDeathState.CorpseLooted, entity.CurrentDeathState);
            Assert.Equal(1, entity.RewardCount);
            map.Verify(world => world.ScheduleRespawn(entity), Times.Once);

            entity.ModifyHealth(100u, DamageType.Physical, null);
            Assert.Equal(1, entity.RewardCount);
        }

        [Fact]
        public void OnDeath_ParticipantRewardFailureDoesNotBlockLaterParticipants()
        {
            var map = new Mock<IBaseMap>();
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(true);
            TestUnitEntity entity = CreateEntity(map.Object, 42u);
            Mock<IPlayer> failingPlayer = CreateRewardParticipant(10u, 100ul);
            Mock<IPlayer> laterPlayer = CreateRewardParticipant(20u, 200ul);
            entity.UseParticipantRewards = true;
            entity.FailingParticipantCharacterId = failingPlayer.Object.CharacterId;
            entity.AddRewardParticipant(failingPlayer.Object);
            entity.AddRewardParticipant(laterPlayer.Object);

            entity.ModifyHealth(100u, DamageType.Physical, null);
            entity.ModifyHealth(100u, DamageType.Physical, null);

            Assert.Equal(1, entity.GetParticipantRewardCount(failingPlayer.Object.CharacterId));
            Assert.Equal(1, entity.GetParticipantRewardCount(laterPlayer.Object.CharacterId));
            Assert.Equal(EntityDeathState.CorpseLooted, entity.CurrentDeathState);
            map.Verify(world => world.ScheduleRespawn(entity), Times.Once);
        }

        [Fact]
        public void ModifyHealth_DuplicateLethalHitsRewardEachParticipantOnce()
        {
            var map = new Mock<IBaseMap>();
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(true);
            TestUnitEntity entity = CreateEntity(map.Object, 42u);
            Mock<IPlayer> firstPlayer = CreateRewardParticipant(10u, 100ul);
            Mock<IPlayer> secondPlayer = CreateRewardParticipant(20u, 200ul);
            entity.UseParticipantRewards = true;
            entity.AddRewardParticipant(firstPlayer.Object);
            entity.AddRewardParticipant(secondPlayer.Object);

            entity.ModifyHealth(100u, DamageType.Physical, null);
            entity.ModifyHealth(100u, DamageType.Physical, null);

            Assert.Equal(1, entity.GetParticipantRewardCount(firstPlayer.Object.CharacterId));
            Assert.Equal(1, entity.GetParticipantRewardCount(secondPlayer.Object.CharacterId));
            map.Verify(world => world.ScheduleRespawn(entity), Times.Once);
        }

        [Fact]
        public void OnDeath_NotificationFailureDoesNotBlockRewardsOrFinalisation()
        {
            var map = new Mock<IBaseMap>();
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(true);
            TestUnitEntity entity = CreateEntity(map.Object, 42u);
            entity.ThrowDuringDeathNotification = true;

            entity.ModifyHealth(100u, DamageType.Physical, null);

            Assert.Equal(1, entity.RewardCount);
            Assert.Equal(EntityDeathState.CorpseLooted, entity.CurrentDeathState);
            map.Verify(world => world.ScheduleRespawn(entity), Times.Once);
        }

        [Fact]
        public void OnDeath_ThreatCleanupFailureDoesNotBlockFinalisation()
        {
            var map = new Mock<IBaseMap>();
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(true);
            TestUnitEntity entity = CreateEntity(map.Object, 42u);
            entity.ThrowDuringThreatCleanup = true;

            entity.ModifyHealth(100u, DamageType.Physical, null);

            Assert.Equal(EntityDeathState.CorpseLooted, entity.CurrentDeathState);
            map.Verify(world => world.ScheduleRespawn(entity), Times.Once);
        }

        [Fact]
        public void Update_RemovalFailureLeavesCorpseRetryable()
        {
            var map = new Mock<IBaseMap>();
            int removalAttempts = 0;
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(true);
            TestUnitEntity entity = CreateEntity(map.Object, 42u);
            map.Setup(world => world.EnqueueRemove(entity)).Callback(() =>
            {
                removalAttempts++;
                if (removalAttempts == 1)
                    throw new InvalidOperationException("removal failure");
            });
            entity.ModifyHealth(100u, DamageType.Physical, null);

            entity.Update(5d);

            Assert.Equal(EntityDeathState.CorpseLooted, entity.CurrentDeathState);
            Assert.Equal(1, removalAttempts);

            entity.Update(0.5d);

            Assert.Equal(EntityDeathState.Dead, entity.CurrentDeathState);
            Assert.Equal(2, removalAttempts);
        }

        private static TestUnitEntity CreateEntity(
            IBaseMap map,
            uint entityId,
            ILootInstance generatedLoot = null)
        {
            var entity = new TestUnitEntity(new Mock<IMovementManager>().Object);
            entity.AttachToMap(map, entityId);
            entity.MaxHealth = 100u;
            entity.SetHealth(100u);
            entity.GeneratedLoot = generatedLoot;
            return entity;
        }

        private static Mock<IPlayer> CreateRewardParticipant(uint guid, ulong characterId)
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Guid).Returns(guid);
            player.SetupGet(value => value.CharacterId).Returns(characterId);
            player.SetupGet(value => value.ThreatManager).Returns(new Mock<IThreatManager>().Object);
            player.SetupGet(value => value.Session).Returns(new Mock<IGameSession>().Object);
            return player;
        }

        private sealed class TestUnitEntity : UnitEntity
        {
            public override EntityType Type => EntityType.NonPlayer;

            public EntityDeathState? CurrentDeathState => DeathState;
            public EntityDeathState? StateDuringReward { get; private set; }
            public int RewardCount { get; private set; }
            public bool ThrowDuringRewards { get; set; }
            public bool ThrowDuringDeathNotification { get; set; }
            public bool ThrowDuringThreatCleanup { get; set; }
            public bool UseParticipantRewards { get; set; }
            public ulong? FailingParticipantCharacterId { get; set; }
            public ILootInstance GeneratedLoot { get; set; }

            private readonly Dictionary<ulong, int> participantRewardCounts = [];

            public TestUnitEntity(IMovementManager movementManager)
                : base(movementManager)
            {
            }

            public void AttachToMap(IBaseMap map, uint entityId)
            {
                PropertyInfo mapProperty = typeof(GridEntity).GetProperty(nameof(Map));
                MethodInfo mapSetter = mapProperty.GetSetMethod(true);
                mapSetter.Invoke(this, [map]);
                Guid = 1u;
                Position = Vector3.Zero;
                EntityId = entityId;
            }

            public void SetHealth(uint health)
            {
                Health = health;
            }

            public void AddRewardParticipant(IPlayer player)
            {
                AddVisible(player);
                ThreatManager.UpdateThreat(player, 1);
            }

            public int GetParticipantRewardCount(ulong characterId)
            {
                return participantRewardCounts.GetValueOrDefault(characterId);
            }

            protected override void GenerateRewards()
            {
                if (UseParticipantRewards)
                {
                    base.GenerateRewards();
                    return;
                }

                RewardCount++;
                StateDuringReward = DeathState;

                if (GeneratedLoot != null)
                    AddLoot(GeneratedLoot);

                if (ThrowDuringRewards)
                    throw new InvalidOperationException("reward failure");
            }

            protected override void RewardKiller(IPlayer player)
            {
                participantRewardCounts[player.CharacterId] = GetParticipantRewardCount(player.CharacterId) + 1;

                if (FailingParticipantCharacterId == player.CharacterId)
                    throw new InvalidOperationException("participant reward failure");
            }

            protected override void PublishDeathState()
            {
                if (ThrowDuringDeathNotification)
                    throw new InvalidOperationException("notification failure");

                base.PublishDeathState();
            }

            protected override void ClearThreatsAfterDeath()
            {
                if (ThrowDuringThreatCleanup)
                    throw new InvalidOperationException("threat cleanup failure");

                base.ClearThreatsAfterDeath();
            }

            protected override float CalculateDefaultProperty(Property property)
            {
                return 0f;
            }

            protected override IEntityModel BuildEntityModel()
            {
                return new NonPlayerEntityModel();
            }
        }
    }
}
