using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateKeyChannelUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // An older migration created a unique index on Key alone. Phase 6 relaxes uniqueness to
            // (Key, Channel) so the same event can fan out across Email + SMS + WhatsApp templates.
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_message_templates_Key\";");

            migrationBuilder.CreateIndex(
                name: "IX_message_templates_Key_Channel",
                table: "message_templates",
                columns: new[] { "Key", "Channel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_message_templates_Key_Channel",
                table: "message_templates");

            migrationBuilder.CreateIndex(
                name: "IX_message_templates_Key",
                table: "message_templates",
                column: "Key",
                unique: true);
        }
    }
}
