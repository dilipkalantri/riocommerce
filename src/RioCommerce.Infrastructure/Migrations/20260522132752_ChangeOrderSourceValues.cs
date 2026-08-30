using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeOrderSourceValues : Migration
    {
        // Re-defines the order_source enum to exactly {website, counter, franchisee, other}.
        // PostgreSQL can't drop/rename enum labels in a way EF can auto-generate, AND existing rows
        // (website, franchise) must be remapped, so the type is recreated and the column re-typed
        // with an explicit value mapping. All DDL is transactional, so it runs inside the EF migration.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TYPE order_source RENAME TO order_source_old;
CREATE TYPE order_source AS ENUM ('website', 'counter', 'franchisee', 'other');
ALTER TABLE orders
    ALTER COLUMN ""Source"" TYPE order_source
    USING (CASE ""Source""::text
        WHEN 'website'            THEN 'website'
        WHEN 'backend_counter'    THEN 'counter'
        WHEN 'backend_phone'      THEN 'counter'
        WHEN 'backend_whats_app'  THEN 'counter'
        WHEN 'franchise'          THEN 'franchisee'
        WHEN 'franchise_referral' THEN 'franchisee'
        ELSE 'other'
    END)::order_source;
DROP TYPE order_source_old;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TYPE order_source RENAME TO order_source_new;
CREATE TYPE order_source AS ENUM ('website', 'backend_counter', 'backend_phone', 'backend_whats_app', 'franchise', 'franchise_referral');
ALTER TABLE orders
    ALTER COLUMN ""Source"" TYPE order_source
    USING (CASE ""Source""::text
        WHEN 'website'    THEN 'website'
        WHEN 'counter'    THEN 'backend_counter'
        WHEN 'franchisee' THEN 'franchise'
        ELSE 'website'
    END)::order_source;
DROP TYPE order_source_new;
");
        }
    }
}
