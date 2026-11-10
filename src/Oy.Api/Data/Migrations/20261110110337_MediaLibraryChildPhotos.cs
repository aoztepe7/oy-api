using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MediaLibraryChildPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Folder",
                table: "PrivateFiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OriginalName",
                table: "PrivateFiles",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<Guid>(
                name: "SessionId",
                table: "ClassPhotos",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateOnly>(
                name: "TakenOn",
                table: "ClassPhotos",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Folder = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OriginalName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateFiles_Folder_CreatedAt",
                table: "PrivateFiles",
                columns: new[] { "Folder", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassPhotos_ProgramId",
                table: "ClassPhotos",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_Folder_CreatedAt",
                table: "MediaAssets",
                columns: new[] { "Folder", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_Url",
                table: "MediaAssets",
                column: "Url",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ClassPhotos_Programs_ProgramId",
                table: "ClassPhotos",
                column: "ProgramId",
                principalTable: "Programs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Existing private files: lesson photos → "ders", the rest were feed images → "akis".
            migrationBuilder.Sql("""
                UPDATE "PrivateFiles" f SET "Folder" = CASE
                    WHEN EXISTS (SELECT 1 FROM "ClassPhotos" c WHERE c."FileId" = f."Id") THEN 'ders' ELSE 'akis' END
                WHERE "Folder" = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClassPhotos_Programs_ProgramId",
                table: "ClassPhotos");

            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropIndex(
                name: "IX_PrivateFiles_Folder_CreatedAt",
                table: "PrivateFiles");

            migrationBuilder.DropIndex(
                name: "IX_ClassPhotos_ProgramId",
                table: "ClassPhotos");

            migrationBuilder.DropColumn(
                name: "Folder",
                table: "PrivateFiles");

            migrationBuilder.DropColumn(
                name: "OriginalName",
                table: "PrivateFiles");

            migrationBuilder.DropColumn(
                name: "TakenOn",
                table: "ClassPhotos");

            migrationBuilder.AlterColumn<Guid>(
                name: "SessionId",
                table: "ClassPhotos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
