using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oy.Api.Data.Migrations
{
    /// <summary>
    /// DemoData wrote '{}' into the demo enrollments' Answers (a JSON list), so every query that loaded them failed
    /// (dashboard, enrollments, finance: HTTP 500). Turn any non-array Answers into an empty list. Such values can't
    /// be read at all, so nothing readable is lost.
    /// </summary>
    public partial class FixDemoEnrollmentAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
UPDATE "Enrollments" SET "Answers" = '[]'::jsonb WHERE jsonb_typeof("Answers") <> 'array';
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
