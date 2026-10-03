using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NorthLife.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_similarities",
                columns: table => new
                {
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rank = table.Column<short>(type: "smallint", nullable: false),
                    content = table.Column<double>(type: "double precision", nullable: false),
                    collaborative = table.Column<double>(type: "double precision", nullable: false),
                    blended = table.Column<double>(type: "double precision", nullable: false),
                    co_visitors = table.Column<int>(type: "integer", nullable: false),
                    computed_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_similarities", x => new { x.source_event_id, x.target_event_id });
                    table.ForeignKey(
                        name: "fk_event_similarities_events_source_event_id",
                        column: x => x.source_event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_event_similarities_events_target_event_id",
                        column: x => x.target_event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_similarities_source_rank",
                table: "event_similarities",
                columns: new[] { "source_event_id", "rank" });

            migrationBuilder.CreateIndex(
                name: "ix_event_similarities_target",
                table: "event_similarities",
                column: "target_event_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_similarities");
        }
    }
}
