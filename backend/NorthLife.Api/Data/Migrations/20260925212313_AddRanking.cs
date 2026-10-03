using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NorthLife.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRanking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "geohash",
                table: "events",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "",
                collation: "C");

            migrationBuilder.CreateTable(
                name: "position_propensities",
                columns: table => new
                {
                    surface = table.Column<short>(type: "smallint", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    propensity = table.Column<double>(type: "double precision", nullable: false),
                    raw_propensity = table.Column<double>(type: "double precision", nullable: false),
                    naive_ratio = table.Column<double>(type: "double precision", nullable: false),
                    impressions = table.Column<int>(type: "integer", nullable: false),
                    clicks = table.Column<int>(type: "integer", nullable: false),
                    estimated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_propensities", x => new { x.surface, x.position });
                });

            migrationBuilder.CreateIndex(
                name: "ix_events_geohash",
                table: "events",
                column: "geohash",
                filter: "deleted_at_utc IS NULL");

            // The same geohash algorithm as Ranking/Geohash.cs, in PL/pgSQL, to backfill existing
            // events (the application sets it on every save from now on) and to cross-check the two.
            migrationBuilder.Sql("""
                CREATE FUNCTION geohash_encode(latitude double precision, longitude double precision, hash_length integer)
                RETURNS text LANGUAGE plpgsql IMMUTABLE STRICT AS $$
                DECLARE
                    alphabet constant text := '0123456789bcdefghjkmnpqrstuvwxyz';
                    min_lat double precision := -90;
                    max_lat double precision := 90;
                    min_lon double precision := -180;
                    max_lon double precision := 180;
                    middle double precision;
                    result text := '';
                    bits integer := 0;
                    code integer := 0;
                    even_bit boolean := true;
                BEGIN
                    WHILE length(result) < hash_length LOOP
                        IF even_bit THEN
                            middle := (min_lon + max_lon) / 2;
                            IF longitude >= middle THEN code := code * 2 + 1; min_lon := middle;
                            ELSE code := code * 2; max_lon := middle;
                            END IF;
                        ELSE
                            middle := (min_lat + max_lat) / 2;
                            IF latitude >= middle THEN code := code * 2 + 1; min_lat := middle;
                            ELSE code := code * 2; max_lat := middle;
                            END IF;
                        END IF;
                        even_bit := NOT even_bit;
                        bits := bits + 1;
                        IF bits = 5 THEN
                            result := result || substr(alphabet, code + 1, 1);
                            bits := 0;
                            code := 0;
                        END IF;
                    END LOOP;
                    RETURN result;
                END $$;

                UPDATE events SET geohash = geohash_encode(latitude::double precision, longitude::double precision, 9);

                -- The list (filters and sort) a feed interaction came from, hashed: the click model's "query".
                ALTER TABLE interactions ADD COLUMN context_key integer NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE interactions DROP COLUMN context_key;
                DROP FUNCTION geohash_encode(double precision, double precision, integer);
                """);

            migrationBuilder.DropTable(
                name: "position_propensities");

            migrationBuilder.DropIndex(
                name: "ix_events_geohash",
                table: "events");

            migrationBuilder.DropColumn(
                name: "geohash",
                table: "events");
        }
    }
}
