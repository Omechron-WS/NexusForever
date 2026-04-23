using NexusForever.Game.Abstract.CSI;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.CSI;
using Moq;

namespace NexusForever.Game.Tests.CSI
{
    public class ClientSideInteractionTests
    {
        [Fact]
        public void IClientSideInteraction_InterfaceExists()
        {
            var type = typeof(IClientSideInteraction);
            Assert.NotNull(type);
            Assert.True(type.IsInterface);
        }

        [Fact]
        public void IClientSideInteraction_HasClientUniqueId()
        {
            var prop = typeof(IClientSideInteraction).GetProperty("ClientUniqueId");
            Assert.NotNull(prop);
            Assert.Equal(typeof(uint), prop.PropertyType);
        }

        [Fact]
        public void IClientSideInteraction_HasActivateUnit()
        {
            var prop = typeof(IClientSideInteraction).GetProperty("ActivateUnit");
            Assert.NotNull(prop);
            Assert.Equal(typeof(IWorldEntity), prop.PropertyType);
        }

        [Fact]
        public void IClientSideInteraction_HasTriggerMethods()
        {
            var type = typeof(IClientSideInteraction);
            Assert.NotNull(type.GetMethod("TriggerReady"));
            Assert.NotNull(type.GetMethod("TriggerSuccess"));
            Assert.NotNull(type.GetMethod("TriggerFail"));
        }

        [Fact]
        public void ClientSideInteraction_ImplementsInterface()
        {
            var type = Type.GetType("NexusForever.Game.CSI.ClientSideInteraction, NexusForever.Game");
            Assert.NotNull(type);
            Assert.True(typeof(IClientSideInteraction).IsAssignableFrom(type));
        }

        [Fact]
        public void ISpellParameters_HasClientSideInteraction()
        {
            var prop = typeof(ISpellParameters).GetProperty("ClientSideInteraction");
            Assert.NotNull(prop);
            Assert.Equal(typeof(IClientSideInteraction), prop.PropertyType);
        }

        [Fact]
        public void IWorldEntity_HasOnActivateSuccess()
        {
            var method = typeof(IWorldEntity).GetMethod("OnActivateSuccess");
            Assert.NotNull(method);
        }

        [Fact]
        public void IWorldEntity_HasOnActivateFail()
        {
            var method = typeof(IWorldEntity).GetMethod("OnActivateFail");
            Assert.NotNull(method);
        }

        [Fact]
        public void TriggerSuccess_CallsEntityOnActivateSuccess()
        {
            var mockPlayer = new Mock<IPlayer>();
            var mockEntity = new Mock<IWorldEntity>();

            var type = Type.GetType("NexusForever.Game.CSI.ClientSideInteraction, NexusForever.Game");
            var csi = Activator.CreateInstance(type, mockPlayer.Object, mockEntity.Object, 42u) as IClientSideInteraction;

            csi.TriggerSuccess();

            mockEntity.Verify(e => e.OnActivateSuccess(mockPlayer.Object), Times.Once);
        }

        [Fact]
        public void TriggerFail_CallsEntityOnActivateFail()
        {
            var mockPlayer = new Mock<IPlayer>();
            var mockEntity = new Mock<IWorldEntity>();

            var type = Type.GetType("NexusForever.Game.CSI.ClientSideInteraction, NexusForever.Game");
            var csi = Activator.CreateInstance(type, mockPlayer.Object, mockEntity.Object, 42u) as IClientSideInteraction;

            csi.TriggerFail();

            mockEntity.Verify(e => e.OnActivateFail(mockPlayer.Object), Times.Once);
        }

        [Fact]
        public void Constructor_SetsProperties()
        {
            var mockPlayer = new Mock<IPlayer>();
            var mockEntity = new Mock<IWorldEntity>();

            var type = Type.GetType("NexusForever.Game.CSI.ClientSideInteraction, NexusForever.Game");
            var csi = Activator.CreateInstance(type, mockPlayer.Object, mockEntity.Object, 99u) as IClientSideInteraction;

            Assert.Equal(99u, csi.ClientUniqueId);
            Assert.Same(mockEntity.Object, csi.ActivateUnit);
        }
    }
}
