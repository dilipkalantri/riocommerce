using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUrlRedirects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "url_redirects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OldUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NewUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RedirectType = table.Column<int>(type: "integer", nullable: false, defaultValue: 301),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    HitCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastHitAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table => { table.PrimaryKey("PK_url_redirects", x => x.Id); });

            // Unique on OldUrl so the same source can't have two rules. Hot-path index for middleware lookup.
            migrationBuilder.CreateIndex(
                name: "IX_url_redirects_OldUrl",
                table: "url_redirects",
                column: "OldUrl",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "url_redirects");
        }
    }
}
