using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NorthLife.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analytics_checkpoints",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    processed_until_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analytics_checkpoints", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "event_popularity",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    log_score = table.Column<double>(type: "double precision", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_popularity", x => x.event_id);
                    table.ForeignKey(
                        name: "fk_event_popularity_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_stats_daily",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    impressions = table.Column<int>(type: "integer", nullable: false),
                    detail_views = table.Column<int>(type: "integer", nullable: false),
                    navigations = table.Column<int>(type: "integer", nullable: false),
                    shares = table.Column<int>(type: "integer", nullable: false),
                    visitors = table.Column<int>(type: "integer", nullable: false),
                    visitor_sketch = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_stats_daily", x => new { x.event_id, x.day });
                    table.ForeignKey(
                        name: "fk_event_stats_daily_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_stats_hourly",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hour_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    impressions = table.Column<int>(type: "integer", nullable: false),
                    detail_views = table.Column<int>(type: "integer", nullable: false),
                    navigations = table.Column<int>(type: "integer", nullable: false),
                    shares = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_stats_hourly", x => new { x.event_id, x.hour_utc });
                    table.ForeignKey(
                        name: "fk_event_stats_hourly_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_popularity_log_score",
                table: "event_popularity",
                column: "log_score",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_event_stats_hourly_hour",
                table: "event_stats_hourly",
                column: "hour_utc");

            // Raw interactions, range-partitioned by month so the retention job drops whole partitions
            // (O(1), no table bloat) instead of deleting rows. EF Core cannot model partitioning.
            migrationBuilder.Sql("""
                CREATE TABLE interactions (
                    event_id uuid NOT NULL,
                    visitor_id uuid NOT NULL,
                    type smallint NOT NULL CONSTRAINT ck_interactions_type CHECK (type BETWEEN 1 AND 4),
                    source smallint NOT NULL CONSTRAINT ck_interactions_source CHECK (source BETWEEN 1 AND 7),
                    position smallint NULL CONSTRAINT ck_interactions_position CHECK (position BETWEEN 1 AND 1000),
                    occurred_at_utc timestamptz NOT NULL DEFAULT now()
                ) PARTITION BY RANGE (occurred_at_utc);

                -- Safety net only: monthly partitions are created ahead of time, so this stays empty.
                CREATE TABLE interactions_default PARTITION OF interactions DEFAULT;

                -- Rollup windows scan by time; the deduplication check and "reset my history" by visitor.
                CREATE INDEX ix_interactions_occurred_at ON interactions (occurred_at_utc);
                CREATE INDEX ix_interactions_visitor_event_type ON interactions (visitor_id, event_id, type, occurred_at_utc);

                -- One UTC calendar month per partition, named interactions_yYYYYmMM.
                CREATE FUNCTION analytics_create_interaction_partition(month_start date)
                RETURNS boolean LANGUAGE plpgsql AS $$
                DECLARE
                    first_day date := date_trunc('month', month_start)::date;
                    partition_name text := format('interactions_y%sm%s', to_char(first_day, 'YYYY'), to_char(first_day, 'MM'));
                BEGIN
                    IF to_regclass(partition_name) IS NOT NULL THEN
                        RETURN false;
                    END IF;
                    EXECUTE format(
                        'CREATE TABLE %I PARTITION OF interactions FOR VALUES FROM (%L) TO (%L)',
                        partition_name,
                        first_day::timestamp AT TIME ZONE 'UTC',
                        (first_day + interval '1 month')::timestamp AT TIME ZONE 'UTC');
                    RETURN true;
                END $$;

                CREATE FUNCTION analytics_ensure_interaction_partitions(months_ahead integer)
                RETURNS integer LANGUAGE plpgsql AS $$
                DECLARE
                    first_month date := date_trunc('month', now() AT TIME ZONE 'UTC')::date;
                    created integer := 0;
                BEGIN
                    FOR month_offset IN 0..months_ahead LOOP
                        IF analytics_create_interaction_partition((first_month + make_interval(months => month_offset))::date) THEN
                            created := created + 1;
                        END IF;
                    END LOOP;
                    RETURN created;
                END $$;

                CREATE FUNCTION analytics_drop_interaction_partitions(cutoff timestamptz)
                RETURNS integer LANGUAGE plpgsql AS $$
                DECLARE
                    child record;
                    upper_bound timestamptz;
                    dropped integer := 0;
                BEGIN
                    FOR child IN
                        SELECT child_table.relname
                        FROM pg_inherits
                        JOIN pg_class parent_table ON parent_table.oid = pg_inherits.inhparent
                        JOIN pg_class child_table ON child_table.oid = pg_inherits.inhrelid
                        WHERE parent_table.relname = 'interactions'
                          AND child_table.relname ~ '^interactions_y[0-9]{4}m[0-9]{2}$'
                    LOOP
                        upper_bound := (to_date(substr(child.relname, 15, 4) || substr(child.relname, 20, 2), 'YYYYMM')
                            + interval '1 month')::timestamp AT TIME ZONE 'UTC';
                        IF upper_bound <= cutoff THEN
                            EXECUTE format('DROP TABLE %I', child.relname);
                            dropped := dropped + 1;
                        END IF;
                    END LOOP;
                    RETURN dropped;
                END $$;

                SELECT analytics_ensure_interaction_partitions(2);

                INSERT INTO analytics_checkpoints (name, processed_until_utc) VALUES ('rollup', now());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION analytics_drop_interaction_partitions(timestamptz);
                DROP FUNCTION analytics_ensure_interaction_partitions(integer);
                DROP FUNCTION analytics_create_interaction_partition(date);
                DROP TABLE interactions;
                """);

            migrationBuilder.DropTable(
                name: "analytics_checkpoints");

            migrationBuilder.DropTable(
                name: "event_popularity");

            migrationBuilder.DropTable(
                name: "event_stats_daily");

            migrationBuilder.DropTable(
                name: "event_stats_hourly");
        }
    }
}
