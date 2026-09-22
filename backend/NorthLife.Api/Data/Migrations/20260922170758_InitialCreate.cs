using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NorthLife.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    business_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_role", "role IN ('BusinessOwner', 'Admin')");
                });

            migrationBuilder.CreateTable(
                name: "event_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploader_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_images", x => x.id);
                    table.CheckConstraint("ck_event_images_size", "size_bytes > 0");
                    table.ForeignKey(
                        name: "fk_event_images_users_uploader_id",
                        column: x => x.uploader_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    venue_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    locality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    start_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    end_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    image_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_highlighted = table.Column<bool>(type: "boolean", nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.CheckConstraint("ck_events_category", "category IN ('Music', 'Nightlife', 'Food', 'Workshops', 'Outdoors', 'Culture', 'Sports', 'Other')");
                    table.CheckConstraint("ck_events_latitude", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_events_longitude", "longitude BETWEEN -180 AND 180");
                    table.CheckConstraint("ck_events_price", "price >= 0");
                    table.CheckConstraint("ck_events_revision", "revision >= 1");
                    table.CheckConstraint("ck_events_status", "status IN ('Pending', 'Published', 'Rejected')");
                    table.CheckConstraint("ck_events_time_range", "end_at_utc > start_at_utc");
                    table.ForeignKey(
                        name: "fk_events_event_images_image_id",
                        column: x => x.image_id,
                        principalTable: "event_images",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_images_uploader_id",
                table: "event_images",
                column: "uploader_id");

            migrationBuilder.CreateIndex(
                name: "ux_event_images_storage_key",
                table: "event_images",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_events_end_at",
                table: "events",
                column: "end_at_utc",
                filter: "deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_events_highlighted_start_id",
                table: "events",
                columns: new[] { "is_highlighted", "start_at_utc", "id" },
                filter: "status = 'Published' AND deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_events_image_id",
                table: "events",
                column: "image_id");

            migrationBuilder.CreateIndex(
                name: "ix_events_owner_updated",
                table: "events",
                columns: new[] { "owner_id", "updated_at_utc" },
                filter: "deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_events_status_start_id",
                table: "events",
                columns: new[] { "status", "start_at_utc", "id" },
                filter: "deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "event_images");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
