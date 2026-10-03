using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NorthLife.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "place_id",
                table: "events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "places",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    locality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    website = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    instagram = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    student_perk = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    image_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    geohash = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_places", x => x.id);
                    table.CheckConstraint("ck_places_category", "category IN ('Food', 'Cafe', 'Nightlife', 'Classes', 'Sports', 'Culture', 'Outdoors', 'Services')");
                    table.CheckConstraint("ck_places_latitude", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_places_longitude", "longitude BETWEEN -180 AND 180");
                    table.CheckConstraint("ck_places_revision", "revision >= 1");
                    table.CheckConstraint("ck_places_status", "status IN ('Pending', 'Published', 'Rejected')");
                    table.ForeignKey(
                        name: "fk_places_event_images_image_id",
                        column: x => x.image_id,
                        principalTable: "event_images",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_places_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "place_opening_hours",
                columns: table => new
                {
                    place_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<short>(type: "smallint", nullable: false),
                    opens_minute = table.Column<short>(type: "smallint", nullable: false),
                    closes_minute = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_place_opening_hours", x => new { x.place_id, x.day_of_week, x.opens_minute });
                    table.CheckConstraint("ck_place_opening_hours_closes", "closes_minute BETWEEN 0 AND 1439");
                    table.CheckConstraint("ck_place_opening_hours_day", "day_of_week BETWEEN 0 AND 6");
                    table.CheckConstraint("ck_place_opening_hours_opens", "opens_minute BETWEEN 0 AND 1439");
                    table.ForeignKey(
                        name: "fk_place_opening_hours_places_place_id",
                        column: x => x.place_id,
                        principalTable: "places",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_events_place_id",
                table: "events",
                column: "place_id",
                filter: "place_id IS NOT NULL AND deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_places_geohash",
                table: "places",
                column: "geohash",
                filter: "deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_places_image_id",
                table: "places",
                column: "image_id");

            migrationBuilder.CreateIndex(
                name: "ix_places_owner_updated",
                table: "places",
                columns: new[] { "owner_id", "updated_at_utc" },
                filter: "deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_places_status_category_name",
                table: "places",
                columns: new[] { "status", "category", "name" },
                filter: "deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_places_status_locality_name",
                table: "places",
                columns: new[] { "status", "locality", "name" },
                filter: "deleted_at_utc IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_events_places_place_id",
                table: "events",
                column: "place_id",
                principalTable: "places",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_events_places_place_id",
                table: "events");

            migrationBuilder.DropTable(
                name: "place_opening_hours");

            migrationBuilder.DropTable(
                name: "places");

            migrationBuilder.DropIndex(
                name: "ix_events_place_id",
                table: "events");

            migrationBuilder.DropColumn(
                name: "place_id",
                table: "events");
        }
    }
}
