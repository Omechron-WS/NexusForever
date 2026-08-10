using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NexusForever.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class Phase6FocusProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "property_base",
                columns: new[] { "property", "subtype", "type", "modType", "note", "value" },
                values: new object[,]
                {
                    { 5u, 3u, 1u, (ushort)2, "Class - Esper - Base Focus Pool", 1000f },
                    { 107u, 3u, 1u, (ushort)2, "Class - Esper - Focus Recovery Rate In Combat", 0.005f },
                    { 108u, 3u, 1u, (ushort)2, "Class - Esper - Focus Recovery Rate Out of Combat", 0.02f },
                    { 5u, 4u, 1u, (ushort)2, "Class - Medic - Base Focus Pool", 1000f },
                    { 107u, 4u, 1u, (ushort)2, "Class - Medic - Base Focus Recovery Rate In Combat", 0.005f },
                    { 108u, 4u, 1u, (ushort)2, "Class - Medic - Base Focus Recovery Rate Out of Combat", 0.02f },
                    { 5u, 7u, 1u, (ushort)2, "Class - Spellslinger - Base Focus Pool", 1000f },
                    { 107u, 7u, 1u, (ushort)2, "Class - Spellslinger - Focus Recovery Rate In Combat", 0.005f },
                    { 108u, 7u, 1u, (ushort)2, "Class - Spellslinger - Focus Recovery Rate Out of Combat", 0.02f }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "property_base",
                keyColumns: new[] { "property", "subtype", "type" },
                keyValues: new object[] { 5u, 3u, 1u });

            migrationBuilder.DeleteData(
                table: "property_base",
                keyColumns: new[] { "property", "subtype", "type" },
                keyValues: new object[] { 107u, 3u, 1u });

            migrationBuilder.DeleteData(
                table: "property_base",
                keyColumns: new[] { "property", "subtype", "type" },
                keyValues: new object[] { 108u, 3u, 1u });

            migrationBuilder.DeleteData(
                table: "property_base",
                keyColumns: new[] { "property", "subtype", "type" },
                keyValues: new object[] { 5u, 4u, 1u });

            migrationBuilder.DeleteData(
                table: "property_base",
                keyColumns: new[] { "property", "subtype", "type" },
                keyValues: new object[] { 107u, 4u, 1u });

            migrationBuilder.DeleteData(
                table: "property_base",
                keyColumns: new[] { "property", "subtype", "type" },
                keyValues: new object[] { 108u, 4u, 1u });

            migrationBuilder.DeleteData(
                table: "property_base",
                keyColumns: new[] { "property", "subtype", "type" },
                keyValues: new object[] { 5u, 7u, 1u });

            migrationBuilder.DeleteData(
                table: "property_base",
                keyColumns: new[] { "property", "subtype", "type" },
                keyValues: new object[] { 107u, 7u, 1u });

            migrationBuilder.DeleteData(
                table: "property_base",
                keyColumns: new[] { "property", "subtype", "type" },
                keyValues: new object[] { 108u, 7u, 1u });
        }
    }
}
