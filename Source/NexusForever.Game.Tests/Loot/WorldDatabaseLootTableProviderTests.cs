using NexusForever.Database;
using NexusForever.Database.World;
using NexusForever.Game.Loot;
using Moq;

namespace NexusForever.Game.Tests.Loot
{
    public class WorldDatabaseLootTableProviderTests
    {
        [Fact]
        public void LoadLootTables_WhenWorldDatabaseIsUnavailable_ThrowsInvalidOperationException()
        {
            var databaseManager = new Mock<IDatabaseManager>();
            databaseManager.Setup(manager => manager.GetDatabase<WorldDatabase>()).Returns((WorldDatabase)null);
            var provider = new WorldDatabaseLootTableProvider(databaseManager.Object);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => provider.LoadLootTables());

            Assert.Contains("must be initialised", exception.Message);
        }
    }
}
