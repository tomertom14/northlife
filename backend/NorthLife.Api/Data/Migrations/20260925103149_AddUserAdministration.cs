using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NorthLife.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Every existing account gets its own random stamp; an empty stamp would reject all tokens.
            migrationBuilder.Sql("UPDATE users SET security_stamp = md5(random()::text || id::text) WHERE security_stamp = '';");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "suspended_at_utc",
                table: "users",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suspension_reason",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    target_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_entries_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_created_id",
                table: "users",
                columns: new[] { "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_id",
                table: "audit_entries",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_created_id",
                table: "audit_entries",
                columns: new[] { "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_target_id",
                table: "audit_entries",
                column: "target_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_users_created_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                table: "users");

            migrationBuilder.DropColumn(
                name: "suspended_at_utc",
                table: "users");

            migrationBuilder.DropColumn(
                name: "suspension_reason",
                table: "users");
        }
    }
}
