using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.Map.Lock;
using NexusForever.Game.Map.Instance;
using NexusForever.Game.Static.Map;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Static;

namespace NexusForever.Game.Tests.Map
{
    public class InstancedMapTests
    {
        [Fact]
        public void Update_InstanceFailure_DoesNotSuppressSiblingInstances()
        {
            const double lastTick = 0.25d;
            var map = new TestInstancedMap();
            map.Initialise(new WorldEntry
            {
                Id = 42u
            });
            Mock<IMapInstance> firstInstance = CreateInstance();
            Mock<IMapInstance> failedInstance = CreateInstance();
            failedInstance.Setup(instance => instance.Update(lastTick)).Throws(new InvalidOperationException("instance failure"));
            Mock<IMapInstance> finalInstance = CreateInstance();
            Dictionary<Guid, IMapInstance> instances = GetInstances(map);
            instances.Add(firstInstance.Object.InstanceId, firstInstance.Object);
            instances.Add(failedInstance.Object.InstanceId, failedInstance.Object);
            instances.Add(finalInstance.Object.InstanceId, finalInstance.Object);

            map.Update(lastTick);

            firstInstance.Verify(instance => instance.Update(lastTick), Times.Once);
            failedInstance.Verify(instance => instance.Update(lastTick), Times.Once);
            finalInstance.Verify(instance => instance.Update(lastTick), Times.Once);
        }

        [Fact]
        public void Update_DeniedAdmission_DoesNotDropWaitingOrSuppressLaterValidAdmission()
        {
            const double lastTick = 0.25d;
            var map = new TestInstancedMap();
            map.Initialise(new WorldEntry
            {
                Id = 42u
            });

            Guid waitingId = Guid.NewGuid();
            Guid liveId = Guid.NewGuid();
            Mock<IMapLock> waitingLock = CreateMapLock(waitingId);
            Mock<IMapLock> liveLock = CreateMapLock(liveId);
            Mock<IMapPosition> waitingPosition = CreatePosition(waitingLock.Object);
            Mock<IMapPosition> deniedPosition = CreatePosition(liveLock.Object);
            Mock<IMapPosition> validPosition = CreatePosition(liveLock.Object);
            var waitingPlayer = new Mock<IPlayer>(MockBehavior.Strict);
            var deniedPlayer = new Mock<IPlayer>(MockBehavior.Strict);
            var validPlayer = new Mock<IPlayer>(MockBehavior.Strict);

            var waitingInstance = new Mock<IMapInstance>(MockBehavior.Strict);
            waitingInstance.SetupSequence(value => value.UnloadStatus)
                .Returns(MapUnloadStatus.UnloadingEntities)
                .Returns(MapUnloadStatus.UnloadingEntities)
                .Returns(MapUnloadStatus.UnloadingEntities)
                .Returns((MapUnloadStatus?)null)
                .Returns((MapUnloadStatus?)null);
            waitingInstance.Setup(value => value.Update(lastTick));
            waitingInstance.Setup(value => value.CanEnter(waitingPlayer.Object, waitingPosition.Object))
                .Returns((GenericError?)null);
            waitingInstance.Setup(value => value.EnqueueAdd(waitingPlayer.Object, waitingPosition.Object));

            var liveInstance = new Mock<IMapInstance>(MockBehavior.Strict);
            liveInstance.SetupGet(value => value.UnloadStatus).Returns((MapUnloadStatus?)null);
            liveInstance.Setup(value => value.Update(lastTick));
            liveInstance.Setup(value => value.CanEnter(deniedPlayer.Object, deniedPosition.Object))
                .Returns(GenericError.InstanceInvalidDestination);
            deniedPlayer.Setup(value => value.OnTeleportToFailed(GenericError.InstanceInvalidDestination));
            liveInstance.Setup(value => value.CanEnter(validPlayer.Object, validPosition.Object))
                .Returns((GenericError?)null);
            liveInstance.Setup(value => value.EnqueueAdd(validPlayer.Object, validPosition.Object));

            Dictionary<Guid, IMapInstance> instances = GetInstances(map);
            instances.Add(waitingId, waitingInstance.Object);
            instances.Add(liveId, liveInstance.Object);
            map.EnqueueAdd(waitingPlayer.Object, waitingPosition.Object);
            map.EnqueueAdd(deniedPlayer.Object, deniedPosition.Object);
            map.EnqueueAdd(validPlayer.Object, validPosition.Object);

            map.Update(lastTick);

            deniedPlayer.Verify(value => value.OnTeleportToFailed(GenericError.InstanceInvalidDestination), Times.Once);
            liveInstance.Verify(value => value.EnqueueAdd(validPlayer.Object, validPosition.Object), Times.Once);
            waitingInstance.Verify(value => value.EnqueueAdd(
                It.IsAny<IGridEntity>(),
                It.IsAny<IMapPosition>()), Times.Never);

            map.Update(lastTick);

            waitingInstance.Verify(value => value.EnqueueAdd(waitingPlayer.Object, waitingPosition.Object), Times.Once);
            deniedPlayer.Verify(value => value.OnTeleportToFailed(GenericError.InstanceInvalidDestination), Times.Once);
            waitingPlayer.VerifyNoOtherCalls();
            deniedPlayer.VerifyNoOtherCalls();
            validPlayer.VerifyNoOtherCalls();
        }

        private static Mock<IMapInstance> CreateInstance()
        {
            var instance = new Mock<IMapInstance>();
            instance.SetupGet(mock => mock.InstanceId).Returns(Guid.NewGuid());
            return instance;
        }

        private static Mock<IMapLock> CreateMapLock(Guid instanceId)
        {
            var mapLock = new Mock<IMapLock>(MockBehavior.Strict);
            mapLock.SetupGet(value => value.InstanceId).Returns(instanceId);
            return mapLock;
        }

        private static Mock<IMapPosition> CreatePosition(IMapLock mapLock)
        {
            var info = new Mock<IMapInfo>(MockBehavior.Strict);
            info.SetupGet(value => value.MapLock).Returns(mapLock);
            var position = new Mock<IMapPosition>(MockBehavior.Strict);
            position.SetupGet(value => value.Info).Returns(info.Object);
            return position;
        }

        private static Dictionary<Guid, IMapInstance> GetInstances(TestInstancedMap map)
        {
            FieldInfo field = typeof(InstancedMap<IMapInstance>).GetField("instances", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return Assert.IsType<Dictionary<Guid, IMapInstance>>(field.GetValue(map));
        }

        private sealed class TestInstancedMap : InstancedMap<IMapInstance>
        {
            protected override IMapLock GetMapLock(IPlayer player)
            {
                throw new NotSupportedException();
            }

            protected override IMapInstance CreateInstance(IPlayer player, IMapLock mapLock)
            {
                throw new NotSupportedException();
            }
        }
    }
}
