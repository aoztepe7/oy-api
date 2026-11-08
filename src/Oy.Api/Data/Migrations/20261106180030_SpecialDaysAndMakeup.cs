using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SpecialDaysAndMakeup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMakeup",
                table: "Sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "MakeupForSessionId",
                table: "Sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ThemeId",
                table: "Sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SpecialDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecialDays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_MakeupForSessionId",
                table: "Sessions",
                column: "MakeupForSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ThemeId",
                table: "Sessions",
                column: "ThemeId");

            migrationBuilder.CreateIndex(
                name: "IX_SpecialDays_Date_Name",
                table: "SpecialDays",
                columns: new[] { "Date", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_Sessions_MakeupForSessionId",
                table: "Sessions",
                column: "MakeupForSessionId",
                principalTable: "Sessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_Themes_ThemeId",
                table: "Sessions",
                column: "ThemeId",
                principalTable: "Themes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_Sessions_MakeupForSessionId",
                table: "Sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_Themes_ThemeId",
                table: "Sessions");

            migrationBuilder.DropTable(
                name: "SpecialDays");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_MakeupForSessionId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_ThemeId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "IsMakeup",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "MakeupForSessionId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ThemeId",
                table: "Sessions");
        }
    }
}
