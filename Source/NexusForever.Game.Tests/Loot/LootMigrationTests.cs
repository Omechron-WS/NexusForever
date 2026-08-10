using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using NexusForever.Database.World.Migrations;

namespace NexusForever.Game.Tests.Loot
{
    public class LootMigrationTests
    {
        [Fact]
        public void Up_CreatesLootTablesWithExpectedRelationships()
        {
            IReadOnlyList<MigrationOperation> operations = new TestableLootTables().BuildUpOperations();
            Dictionary<string, CreateTableOperation> tables = operations
                .OfType<CreateTableOperation>()
                .ToDictionary(operation => operation.Name);

            Assert.Equal(4, tables.Count);
            Assert.Contains("entity_loot", tables.Keys);
            Assert.Contains("item_loot", tables.Keys);
            Assert.Contains("loot_group", tables.Keys);
            Assert.Contains("loot_item", tables.Keys);

            Assert.Contains(tables["loot_group"].ForeignKeys,
                key => key.PrincipalTable == "loot_group" && key.Columns.SequenceEqual(["parentId"]));
            Assert.Contains(tables["entity_loot"].ForeignKeys,
                key => key.PrincipalTable == "loot_group" && key.Columns.SequenceEqual(["lootGroupId"]));
            Assert.Contains(tables["item_loot"].ForeignKeys,
                key => key.PrincipalTable == "loot_group" && key.Columns.SequenceEqual(["lootGroupId"]));
            Assert.Contains(tables["loot_item"].ForeignKeys,
                key => key.PrincipalTable == "loot_group" && key.Columns.SequenceEqual(["id"]));

            string[] indexedTables = operations
                .OfType<CreateIndexOperation>()
                .Select(operation => operation.Table)
                .OrderBy(table => table)
                .ToArray();
            Assert.Equal(["entity_loot", "item_loot", "loot_group"], indexedTables);
        }

        [Fact]
        public void Down_DropsDependentTablesBeforeLootGroups()
        {
            DropTableOperation[] operations = new TestableLootTables()
                .BuildDownOperations()
                .OfType<DropTableOperation>()
                .ToArray();

            Assert.Equal("loot_group", operations[^1].Name);
            Assert.Contains(operations, operation => operation.Name == "entity_loot");
            Assert.Contains(operations, operation => operation.Name == "item_loot");
            Assert.Contains(operations, operation => operation.Name == "loot_item");
        }

        private sealed class TestableLootTables : LootTables
        {
            public IReadOnlyList<MigrationOperation> BuildUpOperations()
            {
                var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
                Up(builder);
                return builder.Operations;
            }

            public IReadOnlyList<MigrationOperation> BuildDownOperations()
            {
                var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
                Down(builder);
                return builder.Operations;
            }
        }
    }
}
