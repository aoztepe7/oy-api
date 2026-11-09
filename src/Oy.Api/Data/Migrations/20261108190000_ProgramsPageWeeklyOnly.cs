using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oy.Api.Data.Migrations
{
    /// <summary>
    /// Content fixes (no schema change), applied once to databases seeded before them. Only values still
    /// equal to the original seed are changed, so anything edited in the panel is left alone.
    /// - "Programlar" lists only regular groups (WeeklyClass); camps/one-off events live on "Kamp ve Etkinlikler".
    /// - Home "Etkinliklerimiz" slider shows camps and events.
    /// - Seed images: real photos instead of text posters in crops, posters shown uncropped, portrait focus.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261108190000_ProgramsPageWeeklyOnly")]
    public partial class ProgramsPageWeeklyOnly : Migration
    {
        private static string SetProgramType(string column, string slug, string newType, string extraCondition = "") => $"""
            UPDATE "Pages" SET "{column}" = (
                SELECT jsonb_agg(
                    CASE WHEN b->>'Type' = 'programs' AND b#>>'{"{"}Props,programType{"}"}' = 'all' {extraCondition}
                         THEN jsonb_set(b, '{"{"}Props,programType{"}"}', '"{newType}"')
                         ELSE b END
                    ORDER BY ord)
                FROM jsonb_array_elements("{column}") WITH ORDINALITY AS t(b, ord))
            WHERE "Slug" = '{slug}' AND "{column}" IS NOT NULL AND jsonb_array_length("{column}") > 0;
            """;

        /// <summary>Home hero still on the seeded text poster → full-bleed cover layout with a real photo.</summary>
        private static string HeroToCover(string column) => $$"""
            UPDATE "Pages" SET "{{column}}" = (
                SELECT jsonb_agg(
                    CASE WHEN b->>'Type' = 'hero' AND b#>>'{Props,image}' = '/media/seed/hero.jpeg'
                         THEN jsonb_set(jsonb_set(b, '{Props,image}', '"/media/seed/galeri-24.webp?focus=40,60"'), '{Props,layout}', '"cover"')
                         ELSE b END
                    ORDER BY ord)
                FROM jsonb_array_elements("{{column}}") WITH ORDINALITY AS t(b, ord))
            WHERE "Slug" = 'home' AND "{{column}}" IS NOT NULL AND jsonb_array_length("{{column}}") > 0;
            """;

        private static string ReplaceImage(string column, string from, string to) =>
            $"""UPDATE "Pages" SET "{column}" = replace("{column}"::text, '"{from}"', '"{to}"')::jsonb WHERE "{column}" IS NOT NULL;""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var column in new[] { "Draft", "Published" })
            {
                migrationBuilder.Sql(SetProgramType(column, "programlar", "WeeklyClass"));
                migrationBuilder.Sql(SetProgramType(column, "home", "events", "AND b#>>'{Props,title}' = 'Etkinliklerimiz'"));
                migrationBuilder.Sql(HeroToCover(column));
                migrationBuilder.Sql(ReplaceImage(column, "/media/seed/neler-yapiyoruz.jpeg", "/media/seed/galeri-03.webp"));
                migrationBuilder.Sql(ReplaceImage(column, "/media/seed/cocuklar.png", "/media/seed/galeri-28.webp?focus=50,40"));
                migrationBuilder.Sql(ReplaceImage(column, "/media/seed/pelin-oztepe.jpeg", "/media/seed/pelin-oztepe.jpeg?focus=50,22"));
            }
            migrationBuilder.Sql("""
                UPDATE "Pages" SET "SeoDescription" = 'Eylül – Haziran dönemini kapsayan düzenli Orman Okulu grupları.'
                WHERE "Slug" = 'programlar' AND "SeoDescription" = 'Orman okulu grupları, kamplar, doğa atölyeleri ve aile programları.';

                UPDATE "Programs" SET "CoverImageUrl" = "CoverImageUrl" || '?fit=contain'
                WHERE "CoverImageUrl" IN ('/media/seed/likya-yolu-kampi.jpeg', '/media/seed/aile-macera-kampi.jpeg');

                UPDATE "Programs" SET "AgeMin" = 5, "AgeMax" = 8
                WHERE "Slug" = 'aile-macera-kampi' AND "AgeMin" = 4 AND "AgeMax" = 12;

                UPDATE "Staff" SET "PhotoUrl" = '/media/seed/pelin-oztepe.jpeg?focus=50,22'
                WHERE "PhotoUrl" = '/media/seed/pelin-oztepe.jpeg';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
