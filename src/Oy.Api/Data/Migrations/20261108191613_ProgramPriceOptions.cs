using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProgramPriceOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PriceOptions",
                table: "Programs",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            // Seeded camps: the price table moves from the description text into PriceOptions (only untouched values).
            migrationBuilder.Sql(
                """
                UPDATE "Programs" SET "PriceOptions" = '[{"Label":"1 Yetişkin + 1 Çocuk","Price":20000,"Note":"","IsAddon":false},{"Label":"1 Yetişkin + 2 Çocuk","Price":23000,"Note":"","IsAddon":false},{"Label":"2 Yetişkin + 1 Çocuk","Price":24000,"Note":"","IsAddon":false},{"Label":"2 Yetişkin + 2 Çocuk","Price":27000,"Note":"","IsAddon":false},{"Label":"İlave çocuk","Price":3000,"Note":"Her ek çocuk için","IsAddon":true},{"Label":"İlave yetişkin","Price":3500,"Note":"Her ek yetişkin için","IsAddon":true}]'
                WHERE "Slug" = 'likya-yolu-aile-doga-kampi' AND "PriceOptions" = '[]';

                UPDATE "Programs" SET "PriceOptions" = '[{"Label":"1 Yetişkin + 1 Çocuk","Price":12500,"Note":"","IsAddon":false},{"Label":"2 Yetişkin + 1 Çocuk","Price":15000,"Note":"","IsAddon":false},{"Label":"İlave çocuk","Price":2500,"Note":"Her ek çocuk için","IsAddon":true}]'
                WHERE "Slug" = 'aile-macera-kampi' AND "PriceOptions" = '[]';

                UPDATE "Programs"
                SET "Description" = btrim(regexp_replace("Description", E'## Ücretler\r?\n\r?\n(- [^\n]*(\n|$))+(\r?\n)?', ''), E'\r\n ')
                WHERE "Slug" IN ('likya-yolu-aile-doga-kampi', 'aile-macera-kampi') AND "Description" LIKE '%## Ücretler%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceOptions",
                table: "Programs");
        }
    }
}
