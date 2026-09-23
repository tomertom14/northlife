using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NorthLife.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicEventQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_events_status_category_start_id",
                table: "events",
                columns: new[] { "status", "category", "start_at_utc", "id" },
                filter: "deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_events_status_locality_start_id",
                table: "events",
                columns: new[] { "status", "locality", "start_at_utc", "id" },
                filter: "deleted_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_events_status_category_start_id",
                table: "events");

            migrationBuilder.DropIndex(
                name: "ix_events_status_locality_start_id",
                table: "events");
        }
    }
}
