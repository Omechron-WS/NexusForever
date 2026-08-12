using Moq;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Command.Handler;

namespace NexusForever.WorldServer.Tests.Command
{
    public sealed class RealmShutdownCommandCategoryTests
    {
        [Theory]
        [InlineData(0d)]
        [InlineData(-1d)]
        public void Start_NonPositiveDelayReportsErrorBeforeSingletonLookup(double seconds)
        {
            var context = new Mock<ICommandContext>();
            var category = new RealmCommandCategory.RealmShutdownCommandCategory();

            category.HandleRealmShutdownStart(context.Object, TimeSpan.FromSeconds(seconds));

            context.Verify(
                value => value.SendError("Time till shutdown must be positive!"),
                Times.Once);
            context.VerifyNoOtherCalls();
        }
    }
}
