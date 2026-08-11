using System.Collections.Immutable;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Map;
using NexusForever.Game.Static.Entity;
using Moq;

namespace NexusForever.Game.Tests.Map
{
    public sealed class EntityRespawnSchedulerTests
    {
        [Fact]
        public void TrySchedule_RejectsPlayerTemporaryAndMismatchedEntities()
        {
            var scheduler = new EntityRespawnScheduler();
            IWorldEntity temporaryEntity = CreateEntity(0u, EntityType.NonPlayer);
            IPlayer player = CreatePlayer(1u);
            IWorldEntity mismatchedEntity = CreateEntity(2u, EntityType.NonPlayer);

            Assert.False(scheduler.TrySchedule(temporaryEntity, CreateModel(0u, EntityType.NonPlayer)));
            Assert.False(scheduler.TrySchedule(player, CreateModel(1u, EntityType.Player)));
            Assert.False(scheduler.TrySchedule(mismatchedEntity, CreateModel(3u, EntityType.NonPlayer)));
            Assert.False(scheduler.TrySchedule(mismatchedEntity, CreateModel(2u, EntityType.Chest)));
        }

        [Fact]
        public void TrySchedule_DuplicatePersistentIdentifierCreatesOneReservation()
        {
            var scheduler = new EntityRespawnScheduler();
            EntityModel model = CreateModel(1u);

            Assert.True(scheduler.TrySchedule(CreateEntity(1u), model));
            Assert.False(scheduler.TrySchedule(CreateEntity(1u), model));
            Assert.True(scheduler.IsPending(1u));
        }

        [Fact]
        public void Update_RespawnsOnceAtThirtySecondBoundaryAndRequiresExactAcknowledgement()
        {
            var scheduler = new EntityRespawnScheduler();
            EntityModel model = CreateModel(1u);
            IWorldEntity corpse = CreateEntity(1u);
            IWorldEntity spawnedEntity = CreateEntity(1u);
            int spawnCount = 0;
            Assert.True(scheduler.TrySchedule(corpse, model));

            scheduler.Update(29.5d, _ => true, _ =>
            {
                spawnCount++;
                return spawnedEntity;
            });
            Assert.Equal(0, spawnCount);

            scheduler.Update(0.5d, _ => true, _ =>
            {
                spawnCount++;
                return spawnedEntity;
            });

            Assert.Equal(1, spawnCount);
            Assert.True(scheduler.IsPending(1u));
            Assert.False(scheduler.Acknowledge(corpse));
            Assert.True(scheduler.IsPending(1u));

            scheduler.Update(100d, _ => true, _ =>
            {
                spawnCount++;
                return spawnedEntity;
            });

            Assert.Equal(1, spawnCount);
            Assert.True(scheduler.Acknowledge(spawnedEntity));
            Assert.False(scheduler.IsPending(1u));

            scheduler.Update(100d, _ => true, _ =>
            {
                spawnCount++;
                return spawnedEntity;
            });
            Assert.Equal(1, spawnCount);
        }

        [Fact]
        public void Update_DueRespawnWaitsForGridReactivationAndSuppressesCachedSpawn()
        {
            var scheduler = new EntityRespawnScheduler();
            EntityModel model = CreateModel(1u);
            IWorldEntity spawnedEntity = CreateEntity(1u);
            bool gridActive = false;
            int spawnCount = 0;
            Assert.True(scheduler.TrySchedule(CreateEntity(1u), model));

            scheduler.Update(30d, _ => gridActive, _ =>
            {
                spawnCount++;
                return spawnedEntity;
            });
            scheduler.Update(100d, _ => gridActive, _ =>
            {
                spawnCount++;
                return spawnedEntity;
            });

            Assert.Equal(0, spawnCount);
            Assert.True(scheduler.IsPending(model.Id));

            gridActive = true;
            scheduler.Update(0.5d, _ => gridActive, _ =>
            {
                spawnCount++;
                return spawnedEntity;
            });

            Assert.Equal(1, spawnCount);
            Assert.True(scheduler.IsPending(model.Id));
        }

        [Fact]
        public void Fail_RequiresExactEntityAndCreatesFreshRetry()
        {
            var scheduler = new EntityRespawnScheduler();
            EntityModel model = CreateModel(1u);
            IWorldEntity corpse = CreateEntity(1u);
            IWorldEntity firstSpawn = CreateEntity(1u);
            IWorldEntity secondSpawn = CreateEntity(1u);
            IWorldEntity nextSpawn = firstSpawn;
            int spawnCount = 0;
            Assert.True(scheduler.TrySchedule(corpse, model));

            scheduler.Update(30d, _ => true, _ =>
            {
                spawnCount++;
                return nextSpawn;
            });

            Assert.False(scheduler.Fail(corpse));
            Assert.True(scheduler.Fail(firstSpawn));
            nextSpawn = secondSpawn;

            scheduler.Update(0.5d, _ => true, _ =>
            {
                spawnCount++;
                return nextSpawn;
            });
            Assert.Equal(1, spawnCount);

            scheduler.Update(0.5d, _ => true, _ =>
            {
                spawnCount++;
                return nextSpawn;
            });

            Assert.Equal(2, spawnCount);
            Assert.False(scheduler.Acknowledge(firstSpawn));
            Assert.True(scheduler.Acknowledge(secondSpawn));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-1d)]
        [InlineData(0d)]
        public void Update_InvalidElapsedTimeDoesNotAdvance(double elapsed)
        {
            var scheduler = new EntityRespawnScheduler();
            Assert.True(scheduler.TrySchedule(CreateEntity(1u), CreateModel(1u)));

            scheduler.Update(elapsed, _ => true, _ => throw new InvalidOperationException());

            Assert.True(scheduler.IsPending(1u));
        }

        [Fact]
        public void Update_ExtremeElapsedTimeEnqueuesOnlyOnce()
        {
            var scheduler = new EntityRespawnScheduler();
            IWorldEntity spawnedEntity = CreateEntity(1u);
            int spawnCount = 0;
            Assert.True(scheduler.TrySchedule(CreateEntity(1u), CreateModel(1u)));

            scheduler.Update(double.MaxValue, _ => true, _ =>
            {
                spawnCount++;
                return spawnedEntity;
            });
            scheduler.Update(double.MaxValue, _ => true, _ =>
            {
                spawnCount++;
                return spawnedEntity;
            });

            Assert.Equal(1, spawnCount);
        }

        [Fact]
        public void Update_SpawnFailureDoesNotBlockLaterReservationAndRetriesAtBoundedCadence()
        {
            var scheduler = new EntityRespawnScheduler();
            IWorldEntity retryEntity = CreateEntity(1u);
            IWorldEntity validEntity = CreateEntity(2u);
            int failedReservationAttempts = 0;
            int validReservationAttempts = 0;
            Assert.True(scheduler.TrySchedule(CreateEntity(1u), CreateModel(1u)));
            Assert.True(scheduler.TrySchedule(CreateEntity(2u), CreateModel(2u)));

            IWorldEntity Enqueue(EntityModel model)
            {
                if (model.Id == 1u)
                {
                    failedReservationAttempts++;
                    if (failedReservationAttempts == 1)
                        throw new InvalidOperationException("spawn failure");

                    return retryEntity;
                }

                validReservationAttempts++;
                return validEntity;
            }

            scheduler.Update(30d, _ => true, Enqueue);

            Assert.Equal(1, failedReservationAttempts);
            Assert.Equal(1, validReservationAttempts);
            Assert.True(scheduler.Acknowledge(validEntity));

            scheduler.Update(0.5d, _ => true, Enqueue);
            Assert.Equal(1, failedReservationAttempts);

            scheduler.Update(0.5d, _ => true, Enqueue);

            Assert.Equal(2, failedReservationAttempts);
            Assert.True(scheduler.Acknowledge(retryEntity));
        }

        [Fact]
        public void EntityCache_GetEntityReturnsPersistentModel()
        {
            EntityModel model = CreateModel(1u);
            var cache = new EntityCache(ImmutableList.Create(model));

            Assert.Same(model, cache.GetEntity(1u));
            Assert.Null(cache.GetEntity(0u));
            Assert.Null(cache.GetEntity(2u));
        }

        [Fact]
        public void EntityCache_DuplicateNonzeroIdentifierIsRejected()
        {
            ImmutableList<EntityModel> models = ImmutableList.Create(
                CreateModel(1u),
                CreateModel(1u));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new EntityCache(models));

            Assert.Contains("duplicate entity identifier 1", exception.Message);
        }

        [Fact]
        public void EntityCache_DuplicateZeroIdentifierIsAllowedButNotIndexed()
        {
            var cache = new EntityCache(ImmutableList.Create(
                CreateModel(0u),
                CreateModel(0u)));

            Assert.Equal(2u, cache.EntityCount);
            Assert.Null(cache.GetEntity(0u));
        }

        [Fact]
        public void CreateAndEnqueueEntity_InvalidPositionDisposesInitialisedEntity()
        {
            var entityFactory = new Mock<IEntityFactory>();
            var entity = new Mock<IWorldEntity>();
            EntityModel model = CreateModel(1u);
            entityFactory.Setup(factory => factory.CreateWorldEntity(model.Type)).Returns(entity.Object);
            var map = new RejectingBaseMap(entityFactory.Object, Mock.Of<IPublicEventManager>());

            IWorldEntity result = map.CreateAndEnqueue(model);

            Assert.Null(result);
            entity.Verify(worldEntity => worldEntity.Initialise(model), Times.Once);
            entity.Verify(worldEntity => worldEntity.Dispose(), Times.Once);
        }

        [Fact]
        public void IsRegisteredEntity_RejectsStaleReferenceWithReusedGuid()
        {
            var map = new RejectingBaseMap(Mock.Of<IEntityFactory>(), Mock.Of<IPublicEventManager>());
            var registeredEntity = new Mock<IGridEntity>();
            registeredEntity.SetupGet(entity => entity.Guid).Returns(7u);
            registeredEntity.SetupGet(entity => entity.Map).Returns(map);
            var staleEntity = new Mock<IGridEntity>();
            staleEntity.SetupGet(entity => entity.Guid).Returns(7u);
            staleEntity.SetupGet(entity => entity.Map).Returns(map);
            var detachedEntity = new Mock<IGridEntity>();
            detachedEntity.SetupGet(entity => entity.Guid).Returns(8u);
            detachedEntity.SetupGet(entity => entity.Map).Returns((IBaseMap)null);
            map.Register(registeredEntity.Object);
            map.Register(detachedEntity.Object);

            Assert.True(map.IsRegistered(registeredEntity.Object));
            Assert.False(map.IsRegistered(staleEntity.Object));
            Assert.False(map.IsRegistered(detachedEntity.Object));
        }

        private static EntityModel CreateModel(uint id, EntityType type = EntityType.NonPlayer)
        {
            return new EntityModel
            {
                Id   = id,
                Type = type
            };
        }

        private static IWorldEntity CreateEntity(uint entityId, EntityType type = EntityType.NonPlayer)
        {
            var entity = new Mock<IWorldEntity>();
            entity.SetupGet(worldEntity => worldEntity.EntityId).Returns(entityId);
            entity.SetupGet(worldEntity => worldEntity.Type).Returns(type);
            return entity.Object;
        }

        private static IPlayer CreatePlayer(uint entityId)
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(entity => entity.EntityId).Returns(entityId);
            player.SetupGet(entity => entity.Type).Returns(EntityType.Player);
            return player.Object;
        }

        private sealed class RejectingBaseMap : BaseMap
        {
            public RejectingBaseMap(
                IEntityFactory entityFactory,
                IPublicEventManager publicEventManager)
                : base(entityFactory, publicEventManager)
            {
            }

            public IWorldEntity CreateAndEnqueue(EntityModel model)
            {
                return CreateAndEnqueueEntity(model);
            }

            public void Register(IGridEntity entity)
            {
                entities.Add(entity.Guid, entity);
            }

            public bool IsRegistered(IGridEntity entity)
            {
                return IsRegisteredEntity(entity);
            }

            public override bool CanEnter(IGridEntity entity, IMapPosition position)
            {
                return false;
            }
        }
    }
}
