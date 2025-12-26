using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NurseShifts.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DefaultContractedHoursPerWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultMaxOvertimeHoursPerWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultMinRestHoursBetweenShifts = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultMaxConsecutiveWorkDays = table.Column<int>(type: "INTEGER", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    AdminUsername = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    AdminPasswordHash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clinics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    HeadNurseId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clinics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Nurses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    PrimaryClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPoolNurse = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmploymentType = table.Column<int>(type: "INTEGER", nullable: false),
                    ContractedHoursPerWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxOvertimeHoursPerWeek = table.Column<int>(type: "INTEGER", nullable: true),
                    HireDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CanHandleResponsibility = table.Column<bool>(type: "INTEGER", nullable: false),
                    SkillLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    PreferredShifts = table.Column<int>(type: "INTEGER", nullable: false),
                    AvoidedShifts = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxConsecutiveWorkDays = table.Column<int>(type: "INTEGER", nullable: true),
                    MinRestHoursBetweenShifts = table.Column<int>(type: "INTEGER", nullable: true),
                    PrefersConsecutiveDays = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nurses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Nurses_Clinics_PrimaryClinicId",
                        column: x => x.PrimaryClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    ShiftType = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiredNurses = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiredSeniorNurses = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiresResponsibleNurse = table.Column<bool>(type: "INTEGER", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: true),
                    ShiftDurationHours = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftConfigurations_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompensatoryTimeOffs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NurseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    HoursCompensated = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompensatoryTimeOffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompensatoryTimeOffs_Nurses_NurseId",
                        column: x => x.NurseId,
                        principalTable: "Nurses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NurseClinicAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NurseId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    CanBeBorrowed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NurseClinicAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NurseClinicAssignments_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NurseClinicAssignments_Nurses_NurseId",
                        column: x => x.NurseId,
                        principalTable: "Nurses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NurseLeaves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NurseId = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaveType = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NurseLeaves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NurseLeaves_Nurses_NurseId",
                        column: x => x.NurseId,
                        principalTable: "Nurses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NurseShiftWishes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NurseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    WishType = table.Column<int>(type: "INTEGER", nullable: false),
                    ShiftType = table.Column<int>(type: "INTEGER", nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NurseShiftWishes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NurseShiftWishes_Nurses_NurseId",
                        column: x => x.NurseId,
                        principalTable: "Nurses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OvertimeBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NurseId = table.Column<int>(type: "INTEGER", nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ContractedHours = table.Column<int>(type: "INTEGER", nullable: false),
                    ActualHours = table.Column<int>(type: "INTEGER", nullable: false),
                    CompensatoryHoursUsed = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimeBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OvertimeBalances_Nurses_NurseId",
                        column: x => x.NurseId,
                        principalTable: "Nurses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ShiftType = table.Column<int>(type: "INTEGER", nullable: false),
                    ClinicId = table.Column<int>(type: "INTEGER", nullable: false),
                    NurseId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsResponsibleNurse = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBorrowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftAssignments_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftAssignments_Nurses_NurseId",
                        column: x => x.NurseId,
                        principalTable: "Nurses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "AdminPasswordHash", "AdminUsername", "DefaultContractedHoursPerWeek", "DefaultMaxConsecutiveWorkDays", "DefaultMaxOvertimeHoursPerWeek", "DefaultMinRestHoursBetweenShifts", "Language" },
                values: new object[] { 1, "$2a$11$.lea7HLBOb5xwftzvm8hXOriVBT1cJSBln266PVSU7UCbsHpLQ3QS", "admin", 40, 5, 8, 11, "en" });

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_HeadNurseId",
                table: "Clinics",
                column: "HeadNurseId");

            migrationBuilder.CreateIndex(
                name: "IX_CompensatoryTimeOffs_NurseId",
                table: "CompensatoryTimeOffs",
                column: "NurseId");

            migrationBuilder.CreateIndex(
                name: "IX_NurseClinicAssignments_ClinicId",
                table: "NurseClinicAssignments",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_NurseClinicAssignments_NurseId_ClinicId",
                table: "NurseClinicAssignments",
                columns: new[] { "NurseId", "ClinicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NurseLeaves_NurseId",
                table: "NurseLeaves",
                column: "NurseId");

            migrationBuilder.CreateIndex(
                name: "IX_Nurses_PrimaryClinicId",
                table: "Nurses",
                column: "PrimaryClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_NurseShiftWishes_NurseId",
                table: "NurseShiftWishes",
                column: "NurseId");

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeBalances_NurseId_WeekStartDate",
                table: "OvertimeBalances",
                columns: new[] { "NurseId", "WeekStartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_ClinicId",
                table: "ShiftAssignments",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_Date_ShiftType_NurseId",
                table: "ShiftAssignments",
                columns: new[] { "Date", "ShiftType", "NurseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_NurseId",
                table: "ShiftAssignments",
                column: "NurseId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftConfigurations_ClinicId_ShiftType_DayOfWeek",
                table: "ShiftConfigurations",
                columns: new[] { "ClinicId", "ShiftType", "DayOfWeek" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Clinics_Nurses_HeadNurseId",
                table: "Clinics",
                column: "HeadNurseId",
                principalTable: "Nurses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Clinics_Nurses_HeadNurseId",
                table: "Clinics");

            migrationBuilder.DropTable(
                name: "CompensatoryTimeOffs");

            migrationBuilder.DropTable(
                name: "NurseClinicAssignments");

            migrationBuilder.DropTable(
                name: "NurseLeaves");

            migrationBuilder.DropTable(
                name: "NurseShiftWishes");

            migrationBuilder.DropTable(
                name: "OvertimeBalances");

            migrationBuilder.DropTable(
                name: "ShiftAssignments");

            migrationBuilder.DropTable(
                name: "ShiftConfigurations");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "Nurses");

            migrationBuilder.DropTable(
                name: "Clinics");
        }
    }
}
