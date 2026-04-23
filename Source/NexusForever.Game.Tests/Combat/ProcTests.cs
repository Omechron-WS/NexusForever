using NexusForever.Game.Static.Combat;

namespace NexusForever.Game.Tests.Combat
{
    public class ProcTests
    {
        [Fact]
        public void ProcType_EnumExists()
        {
            Assert.True(typeof(ProcType).IsEnum);
        }

        [Fact]
        public void ProcType_HasExpectedValues()
        {
            Assert.Equal(11, (int)ProcType.BeginMoving);
            Assert.Equal(145, (int)ProcType.CriticalDamage);
            Assert.Equal(214, (int)ProcType.StopsMoving);
        }

        [Fact]
        public void IProcInfo_InterfaceExists()
        {
            var type = Type.GetType("NexusForever.Game.Abstract.Combat.IProcInfo, NexusForever.Game.Abstract");
            Assert.NotNull(type);
            Assert.True(type.IsInterface);
        }

        [Fact]
        public void ProcInfo_ImplementsInterface()
        {
            var interfaceType = Type.GetType("NexusForever.Game.Abstract.Combat.IProcInfo, NexusForever.Game.Abstract");
            var implType = Type.GetType("NexusForever.Game.Combat.ProcInfo, NexusForever.Game");
            Assert.NotNull(implType);
            Assert.True(interfaceType.IsAssignableFrom(implType));
        }
    }
}
