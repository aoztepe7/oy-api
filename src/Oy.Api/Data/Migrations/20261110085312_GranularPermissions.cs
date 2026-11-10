using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oy.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Detail permissions (prices, profitability, salaries, parent contacts, fees, dashboard). Existing roles get
    /// the ones matching what they could already see, so nobody loses access on upgrade; admins then narrow roles.
    /// </summary>
    public partial class GranularPermissions : Migration
    {
        private static readonly (string Has, string Grant)[] Grants =
        [
            ("programs.read", "programs.pricing"),
            ("finance.read", "programs.finance"),
            ("finance.read", "dashboard.finance"),
            ("staff.read", "staff.compensation"),
            ("enrollments.read", "enrollments.contact"),
            ("enrollments.read", "enrollments.fees"),
        ];

        private static readonly string[] Added =
        [
            "dashboard.view", "dashboard.finance", "programs.pricing", "programs.finance",
            "staff.compensation", "enrollments.contact", "enrollments.fees",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (has, grant) in Grants)
                migrationBuilder.Sql($"""
                    UPDATE "Roles" SET "Permissions" = array_append("Permissions", '{grant}')
                    WHERE '{has}' = ANY("Permissions") AND NOT '{grant}' = ANY("Permissions");
                    """);
            // Everyone saw the dashboard; teacher-only roles start on their lessons instead.
            migrationBuilder.Sql("""
                UPDATE "Roles" SET "Permissions" = array_append("Permissions", 'dashboard.view')
                WHERE NOT "Permissions" <@ ARRAY['classroom.use']::text[] AND NOT 'dashboard.view' = ANY("Permissions");
                """);
            // dashboard.finance builds on dashboard.view.
            migrationBuilder.Sql("""
                UPDATE "Roles" SET "Permissions" = array_append("Permissions", 'dashboard.view')
                WHERE 'dashboard.finance' = ANY("Permissions") AND NOT 'dashboard.view' = ANY("Permissions");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var key in Added)
                migrationBuilder.Sql($"""UPDATE "Roles" SET "Permissions" = array_remove("Permissions", '{key}');""");
        }
    }
}
