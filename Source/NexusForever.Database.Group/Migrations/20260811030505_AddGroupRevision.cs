using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusForever.Database.Group.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<ulong>(
                name: "revision",
                table: "group",
                type: "bigint unsigned",
                nullable: false,
                defaultValue: 1ul);

            migrationBuilder.AddCheckConstraint(
                name: "CK_group_revision_nonzero",
                table: "group",
                sql: "`revision` > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_group_revision_nonzero",
                table: "group");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "group");
        }
    }
}
