using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFacultySharingEffectiveFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Adds the nullable EffectiveFrom timestamp column to FacultySharingRules.
            // The property was added to the entity earlier but no migration was generated;
            // this catches the DB up so PayoutAsync stops throwing 42703.
            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveFrom",
                table: "FacultySharingRules",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EffectiveFrom",
                table: "FacultySharingRules");
        }
    }
}
