using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NurseShifts.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoAssignHeadNurseMorning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoAssignHeadNurseMorning",
                table: "Clinics",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoAssignHeadNurseMorning",
                table: "Clinics");
        }
    }
}
