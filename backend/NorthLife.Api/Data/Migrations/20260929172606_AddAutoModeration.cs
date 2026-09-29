using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NorthLife.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "actor_id",
                table: "audit_entries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "auto_moderation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    triggered_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    @checked = table.Column<int>(name: "checked", type: "integer", nullable: false),
                    approved = table.Column<int>(type: "integer", nullable: false),
                    would_approve = table.Column<int>(type: "integer", nullable: false),
                    held = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_moderation_runs", x => x.id);
                    table.CheckConstraint("ck_auto_moderation_runs_mode", "mode IN ('Off', 'NotesOnly', 'Approve')");
                    table.CheckConstraint("ck_auto_moderation_runs_trigger", "trigger IN ('Scheduled', 'Manual')");
                    table.ForeignKey(
                        name: "fk_auto_moderation_runs_users_triggered_by_id",
                        column: x => x.triggered_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "auto_moderation_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    run_at_local = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    min_account_age_days = table.Column<int>(type: "integer", nullable: false),
                    min_approved_events = table.Column<int>(type: "integer", nullable: false),
                    rejection_lookback_days = table.Column<int>(type: "integer", nullable: false),
                    max_auto_approvals_per_owner_per_day = table.Column<int>(type: "integer", nullable: false),
                    max_days_ahead = table.Column<int>(type: "integer", nullable: false),
                    max_duration_days = table.Column<int>(type: "integer", nullable: false),
                    max_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    duplicate_similarity = table.Column<double>(type: "double precision", nullable: false),
                    duplicate_distance_meters = table.Column<int>(type: "integer", nullable: false),
                    banned_words = table.Column<string[]>(type: "text[]", nullable: false),
                    last_scheduled_run_date = table.Column<DateOnly>(type: "date", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_moderation_settings", x => x.id);
                    table.CheckConstraint("ck_auto_moderation_settings_mode", "mode IN ('Off', 'NotesOnly', 'Approve')");
                    table.CheckConstraint("ck_auto_moderation_settings_single_row", "id = '0199a0f7-0017-7000-8000-a07011a70017'");
                });

            migrationBuilder.CreateTable(
                name: "auto_moderation_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_revision = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reasons = table.Column<string>(type: "jsonb", nullable: false),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_moderation_decisions", x => x.id);
                    table.CheckConstraint("ck_auto_moderation_decisions_outcome", "outcome IN ('Approved', 'WouldApprove', 'Held')");
                    table.ForeignKey(
                        name: "fk_auto_moderation_decisions_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_auto_moderation_decisions_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "auto_moderation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "auto_moderation_settings",
                columns: new[] { "id", "banned_words", "duplicate_distance_meters", "duplicate_similarity", "last_scheduled_run_date", "max_auto_approvals_per_owner_per_day", "max_days_ahead", "max_duration_days", "max_price", "min_account_age_days", "min_approved_events", "mode", "rejection_lookback_days", "run_at_local", "updated_at_utc" },
                values: new object[] { new Guid("0199a0f7-0017-7000-8000-a07011a70017"), new[] { "קזינו", "הימורים", "הלוואות" }, 1000, 0.84999999999999998, null, 5, 180, 14, 1000m, 7, 3, "NotesOnly", 90, new TimeOnly(7, 0, 0), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.CreateIndex(
                name: "ix_auto_moderation_decisions_event_decided",
                table: "auto_moderation_decisions",
                columns: new[] { "event_id", "decided_at_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_auto_moderation_decisions_run",
                table: "auto_moderation_decisions",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_moderation_runs_started",
                table: "auto_moderation_runs",
                column: "started_at_utc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_auto_moderation_runs_triggered_by_id",
                table: "auto_moderation_runs",
                column: "triggered_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auto_moderation_decisions");

            migrationBuilder.DropTable(
                name: "auto_moderation_settings");

            migrationBuilder.DropTable(
                name: "auto_moderation_runs");

            // Entries without an actor were written by the automatic service; the old schema cannot
            // hold them, and a placeholder actor id would violate the foreign key to users.
            migrationBuilder.Sql("DELETE FROM audit_entries WHERE actor_id IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_id",
                table: "audit_entries",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
