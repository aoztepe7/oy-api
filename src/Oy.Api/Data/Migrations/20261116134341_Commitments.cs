using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Commitments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommitmentTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitmentTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommitmentSignatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SignedTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SignedBody = table.Column<string>(type: "text", nullable: false),
                    BodyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SignerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SignatureImage = table.Column<byte[]>(type: "bytea", nullable: true),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SignedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    WitnessName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitmentSignatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommitmentSignatures_CommitmentTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "CommitmentTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommitmentSignatures_Enrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Enrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProgramCommitments",
                columns: table => new
                {
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramCommitments", x => new { x.ProgramId, x.TemplateId });
                    table.ForeignKey(
                        name: "FK_ProgramCommitments_CommitmentTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "CommitmentTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProgramCommitments_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommitmentSignatures_EnrollmentId_TemplateId",
                table: "CommitmentSignatures",
                columns: new[] { "EnrollmentId", "TemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommitmentSignatures_TemplateId",
                table: "CommitmentSignatures",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_CommitmentSignatures_Token",
                table: "CommitmentSignatures",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgramCommitments_TemplateId",
                table: "ProgramCommitments",
                column: "TemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommitmentSignatures");

            migrationBuilder.DropTable(
                name: "ProgramCommitments");

            migrationBuilder.DropTable(
                name: "CommitmentTemplates");
        }
    }
}
