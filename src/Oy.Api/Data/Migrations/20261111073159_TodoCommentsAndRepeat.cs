using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TodoCommentsAndRepeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NextId",
                table: "Todos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Repeat",
                table: "Todos",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<int>(
                name: "RepeatDay",
                table: "Todos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TodoComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TodoId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TodoComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TodoComments_Todos_TodoId",
                        column: x => x.TodoId,
                        principalTable: "Todos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TodoComments_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TodoComments_AuthorId",
                table: "TodoComments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_TodoComments_TodoId_CreatedAt",
                table: "TodoComments",
                columns: new[] { "TodoId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TodoComments");

            migrationBuilder.DropColumn(
                name: "NextId",
                table: "Todos");

            migrationBuilder.DropColumn(
                name: "Repeat",
                table: "Todos");

            migrationBuilder.DropColumn(
                name: "RepeatDay",
                table: "Todos");
        }
    }
}
