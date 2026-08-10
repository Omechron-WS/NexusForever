using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using NexusForever.Database.Character.Migrations;

namespace NexusForever.Game.Tests.Persistence
{
    public sealed class FocusPropertyMigrationTests
    {
        private static readonly HashSet<SeedKey> expectedKeys =
        [
            new SeedKey(5u, 3u, 1u),
            new SeedKey(107u, 3u, 1u),
            new SeedKey(108u, 3u, 1u),
            new SeedKey(5u, 4u, 1u),
            new SeedKey(107u, 4u, 1u),
            new SeedKey(108u, 4u, 1u),
            new SeedKey(5u, 7u, 1u),
            new SeedKey(107u, 7u, 1u),
            new SeedKey(108u, 7u, 1u)
        ];

        [Fact]
        public void Up_InsertsFocusPropertiesForFocusUsingClasses()
        {
            InsertDataOperation operation = Assert.Single(new TestablePhase6FocusProperties()
                .BuildUpOperations()
                .OfType<InsertDataOperation>());

            Assert.Equal("property_base", operation.Table);
            Assert.Equal(
                ["property", "subtype", "type", "modType", "note", "value"],
                operation.Columns);

            Dictionary<SeedKey, SeedValue> rows = ReadInsertRows(operation);
            Assert.Equal(expectedKeys, rows.Keys.ToHashSet());

            foreach ((SeedKey key, SeedValue seed) in rows)
            {
                Assert.Equal((ushort)2, seed.ModType);
                Assert.StartsWith("Class - ", seed.Note);

                float expectedValue = key.Property switch
                {
                    5u   => 1000f,
                    107u => 0.005f,
                    108u => 0.02f,
                    _    => throw new InvalidOperationException()
                };
                Assert.Equal(expectedValue, seed.Value);
            }
        }

        [Fact]
        public void Down_DeletesEveryInsertedFocusProperty()
        {
            DeleteDataOperation[] operations = new TestablePhase6FocusProperties()
                .BuildDownOperations()
                .OfType<DeleteDataOperation>()
                .ToArray();

            Assert.Equal(9, operations.Length);
            var keys = new HashSet<SeedKey>();
            foreach (DeleteDataOperation operation in operations)
            {
                Assert.Equal("property_base", operation.Table);
                Assert.Equal(["property", "subtype", "type"], operation.KeyColumns);
                keys.Add(new SeedKey(
                    Convert.ToUInt32(operation.KeyValues[0, 0]),
                    Convert.ToUInt32(operation.KeyValues[0, 1]),
                    Convert.ToUInt32(operation.KeyValues[0, 2])));
            }

            Assert.Equal(expectedKeys, keys);
        }

        private static Dictionary<SeedKey, SeedValue> ReadInsertRows(InsertDataOperation operation)
        {
            int propertyIndex = Array.IndexOf(operation.Columns, "property");
            int subtypeIndex = Array.IndexOf(operation.Columns, "subtype");
            int typeIndex = Array.IndexOf(operation.Columns, "type");
            int modTypeIndex = Array.IndexOf(operation.Columns, "modType");
            int noteIndex = Array.IndexOf(operation.Columns, "note");
            int valueIndex = Array.IndexOf(operation.Columns, "value");
            var rows = new Dictionary<SeedKey, SeedValue>();

            for (int row = 0; row < operation.Values.GetLength(0); row++)
            {
                var key = new SeedKey(
                    Convert.ToUInt32(operation.Values[row, propertyIndex]),
                    Convert.ToUInt32(operation.Values[row, subtypeIndex]),
                    Convert.ToUInt32(operation.Values[row, typeIndex]));
                rows.Add(key, new SeedValue(
                    Convert.ToUInt16(operation.Values[row, modTypeIndex]),
                    Convert.ToString(operation.Values[row, noteIndex]),
                    Convert.ToSingle(operation.Values[row, valueIndex])));
            }

            return rows;
        }

        private readonly record struct SeedKey(uint Property, uint Subtype, uint Type);
        private readonly record struct SeedValue(ushort ModType, string Note, float Value);

        private sealed class TestablePhase6FocusProperties : Phase6FocusProperties
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
