using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Map;
using NexusForever.Game.Static.Map;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Static;

namespace NexusForever.Game.Tests.Map
{
    public sealed class MapManagerTests
    {
        [Fact]
        public void ProcessPending_DeniedAdmission_DoesNotSuppressLaterValidAdmission()
        {
            const MapType baseMapType = (MapType)0;
            var entry = new WorldEntry
            {
                Id   = 42u,
                Type = baseMapType
            };
            Mock<IMapPosition> deniedPosition = CreatePosition(entry);
            Mock<IMapPosition> validPosition = CreatePosition(entry);
            var deniedPlayer = new Mock<IPlayer>(MockBehavior.Strict);
            var validPlayer = new Mock<IPlayer>(MockBehavior.Strict);
            var map = new Mock<IMap>(MockBehavior.Strict);
            map.Setup(value => value.Initialise(entry));
            map.Setup(value => value.CanEnter(deniedPlayer.Object, deniedPosition.Object))
                .Returns(GenericError.InstanceInvalidDestination);
            deniedPlayer.Setup(value => value.OnTeleportToFailed(GenericError.InstanceInvalidDestination));
            map.Setup(value => value.CanEnter(validPlayer.Object, validPosition.Object))
                .Returns((GenericError?)null);
            map.Setup(value => value.EnqueueAdd(validPlayer.Object, validPosition.Object));
            var mapFactory = new Mock<IMapFactory>(MockBehavior.Strict);
            mapFactory.Setup(value => value.CreateMap(baseMapType)).Returns(map.Object);
            var manager = new MapManager(mapFactory.Object, new MapUpdater());
            manager.AddToMap(deniedPlayer.Object, deniedPosition.Object);
            manager.AddToMap(validPlayer.Object, validPosition.Object);

            InvokeProcessPending(manager);

            deniedPlayer.Verify(value => value.OnTeleportToFailed(GenericError.InstanceInvalidDestination), Times.Once);
            map.Verify(value => value.EnqueueAdd(validPlayer.Object, validPosition.Object), Times.Once);
            deniedPlayer.VerifyNoOtherCalls();
            validPlayer.VerifyNoOtherCalls();
            mapFactory.VerifyAll();
            map.VerifyAll();
        }

        private static Mock<IMapPosition> CreatePosition(WorldEntry entry)
        {
            var info = new Mock<IMapInfo>(MockBehavior.Strict);
            info.SetupGet(value => value.Entry).Returns(entry);
            var position = new Mock<IMapPosition>(MockBehavior.Strict);
            position.SetupGet(value => value.Info).Returns(info.Object);
            return position;
        }

        private static void InvokeProcessPending(MapManager manager)
        {
            MethodInfo method = typeof(MapManager).GetMethod("ProcessPending", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(manager, null);
        }
    }
}
