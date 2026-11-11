using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BillingTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_EnrollmentId",
                table: "Payments");

            migrationBuilder.AddColumn<DateOnly>(
                name: "Period",
                table: "Payments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "IssuedOn",
                table: "Invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetAmount",
                table: "Invoices",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "Invoices",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "Invoices",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Billing",
                table: "Enrollments",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    MonthlyDueDay = table.Column<int>(type: "integer", nullable: false),
                    ReminderDays = table.Column<int>(type: "integer", nullable: false),
                    ItemName = table.Column<string>(type: "text", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    DefaultTaxId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Obligations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: false),
                    StartMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    EndMonth = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    StaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Obligations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Obligations_Staff_StaffId",
                        column: x => x.StaffId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ObligationPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObligationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObligationPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObligationPayments_Obligations_ObligationId",
                        column: x => x.ObligationId,
                        principalTable: "Obligations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_EnrollmentId_Period",
                table: "Payments",
                columns: new[] { "EnrollmentId", "Period" });

            migrationBuilder.CreateIndex(
                name: "IX_ObligationPayments_ObligationId_Month",
                table: "ObligationPayments",
                columns: new[] { "ObligationId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Obligations_StaffId",
                table: "Obligations",
                column: "StaffId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingSettings");

            migrationBuilder.DropTable(
                name: "ObligationPayments");

            migrationBuilder.DropTable(
                name: "Obligations");

            migrationBuilder.DropIndex(
                name: "IX_Payments_EnrollmentId_Period",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Period",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "IssuedOn",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "NetAmount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Billing",
                table: "Enrollments");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_EnrollmentId",
                table: "Payments",
                column: "EnrollmentId");
        }
    }
}
