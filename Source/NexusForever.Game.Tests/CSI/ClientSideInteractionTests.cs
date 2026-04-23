using NexusForever.Game.Abstract.CSI;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.CSI;
using NexusForever.Game.Static.CSI;
using Moq;

namespace NexusForever.Game.Tests.CSI
{
    public class ClientSideInteractionTests
    {
        [Fact]
        public void Constructor_SetsProperties()
        {
            var mockPlayer = new Mock<IPlayer>();
            var mockEntity = new Mock<IWorldEntity>();

            var csi = new ClientSideInteraction(mockPlayer.Object, mockEntity.Object, 99u);

            Assert.Equal(99u, csi.ClientUniqueId);
            Assert.Same(mockEntity.Object, csi.ActivateUnit);
            Assert.Equal(CSIType.Interaction, csi.CsiType);
            Assert.Null(csi.Entry);
        }

        [Fact]
        public void Constructor_ThrowsOnNullOwner()
        {
            var mockEntity = new Mock<IWorldEntity>();
            Assert.Throws<ArgumentNullException>(() =>
                new ClientSideInteraction(null, mockEntity.Object, 1u));
        }

        [Fact]
        public void Constructor_ThrowsOnNullEntity()
        {
            var mockPlayer = new Mock<IPlayer>();
            Assert.Throws<ArgumentNullException>(() =>
                new ClientSideInteraction(mockPlayer.Object, null, 1u));
        }

        [Fact]
        public void TriggerSuccess_CallsEntityOnActivateSuccess()
        {
            var mockPlayer = new Mock<IPlayer>();
            var mockEntity = new Mock<IWorldEntity>();

            var csi = new ClientSideInteraction(mockPlayer.Object, mockEntity.Object, 42u);
            csi.TriggerSuccess();

            mockEntity.Verify(e => e.OnActivateSuccess(mockPlayer.Object), Times.Once);
        }

        [Fact]
        public void TriggerFail_CallsEntityOnActivateFail()
        {
            var mockPlayer = new Mock<IPlayer>();
            var mockEntity = new Mock<IWorldEntity>();

            var csi = new ClientSideInteraction(mockPlayer.Object, mockEntity.Object, 42u);
            csi.TriggerFail();

            mockEntity.Verify(e => e.OnActivateFail(mockPlayer.Object), Times.Once);
        }

        [Fact]
        public void ImplementsIClientSideInteraction()
        {
            Assert.True(typeof(IClientSideInteraction).IsAssignableFrom(typeof(ClientSideInteraction)));
        }

        [Fact]
        public void ISpellParameters_HasClientSideInteractionProperty()
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
        public void DefaultCsiTypeIsInteraction_WhenNoCsiEntry()
        {
            var mockPlayer = new Mock<IPlayer>();
            var mockEntity = new Mock<IWorldEntity>();

            var csi = new ClientSideInteraction(mockPlayer.Object, mockEntity.Object, 1u, 0);

            Assert.Equal(CSIType.Interaction, csi.CsiType);
        }
    }
}
