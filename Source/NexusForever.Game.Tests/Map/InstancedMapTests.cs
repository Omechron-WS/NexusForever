using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.Map.Lock;
using NexusForever.Game.Map.Instance;
using NexusForever.GameTable.Model;

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

        private static Mock<IMapInstance> CreateInstance()
        {
            var instance = new Mock<IMapInstance>();
            instance.SetupGet(mock => mock.InstanceId).Returns(Guid.NewGuid());
            return instance;
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
