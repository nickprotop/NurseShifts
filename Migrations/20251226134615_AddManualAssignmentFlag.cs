using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NurseShifts.Migrations
{
    /// <inheritdoc />
    public partial class AddManualAssignmentFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManualAssignment",
                table: "ShiftAssignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsManualAssignment",
                table: "ShiftAssignments");
        }
    }
}
