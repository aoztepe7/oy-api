using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class GuardiansFeedPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "Users",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                // Every existing login is a panel (staff) user.
                defaultValue: "Staff");

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "FeedPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    ImageIds = table.Column<string>(type: "jsonb", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorName = table.Column<string>(type: "text", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedPosts_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GuardianLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Relation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuardianLinks_Enrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Enrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GuardianLinks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrivateFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoredName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
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
                    table.PrimaryKey("PK_PrivateFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Caption = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedByName = table.Column<string>(type: "text", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassPhotos_PrivateFiles_FileId",
                        column: x => x.FileId,
                        principalTable: "PrivateFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassPhotos_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClassPhotoTags",
                columns: table => new
                {
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassPhotoTags", x => new { x.PhotoId, x.EnrollmentId });
                    table.ForeignKey(
                        name: "FK_ClassPhotoTags_ClassPhotos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "ClassPhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClassPhotoTags_Enrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Enrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassPhotos_FileId",
                table: "ClassPhotos",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassPhotos_PublishedAt",
                table: "ClassPhotos",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ClassPhotos_SessionId",
                table: "ClassPhotos",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassPhotoTags_EnrollmentId",
                table: "ClassPhotoTags",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedPosts_ProgramId",
                table: "FeedPosts",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedPosts_PublishedAt",
                table: "FeedPosts",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianLinks_EnrollmentId",
                table: "GuardianLinks",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianLinks_UserId_EnrollmentId",
                table: "GuardianLinks",
                columns: new[] { "UserId", "EnrollmentId" },
                unique: true);

            // New permissions for existing roles: whoever edits enrollments can open guardian accounts,
            // whoever edits programs can post to the guardians' feed. Admins narrow this per role.
            migrationBuilder.Sql("""
                UPDATE "Roles" SET "Permissions" = array_append("Permissions", 'guardians.manage')
                WHERE 'enrollments.write' = ANY("Permissions") AND NOT 'guardians.manage' = ANY("Permissions");
                UPDATE "Roles" SET "Permissions" = array_append("Permissions", 'feed.manage')
                WHERE 'programs.write' = ANY("Permissions") AND NOT 'feed.manage' = ANY("Permissions");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassPhotoTags");

            migrationBuilder.DropTable(
                name: "FeedPosts");

            migrationBuilder.DropTable(
                name: "GuardianLinks");

            migrationBuilder.DropTable(
                name: "ClassPhotos");

            migrationBuilder.DropTable(
                name: "PrivateFiles");

            // Guardian logins have no role; they cannot exist once RoleId is required again.
            migrationBuilder.Sql("""DELETE FROM "Users" WHERE "Kind" = 'Guardian';""");
            migrationBuilder.Sql("""
                UPDATE "Roles" SET "Permissions" = array_remove(array_remove("Permissions", 'guardians.manage'), 'feed.manage');
                """);

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Users");

            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "Users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
