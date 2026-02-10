using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BoardGameScraper.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "games",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    bgg_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    year_published = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    min_players = table.Column<int>(type: "integer", nullable: true),
                    max_players = table.Column<int>(type: "integer", nullable: true),
                    min_playtime = table.Column<int>(type: "integer", nullable: true),
                    max_playtime = table.Column<int>(type: "integer", nullable: true),
                    avg_rating = table.Column<decimal>(type: "numeric", nullable: true),
                    bgg_rank = table.Column<int>(type: "integer", nullable: true),
                    image_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    thumbnail_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    categories = table.Column<string>(type: "jsonb", nullable: false),
                    mechanics = table.Column<string>(type: "jsonb", nullable: false),
                    designers = table.Column<string>(type: "jsonb", nullable: false),
                    artists = table.Column<string>(type: "jsonb", nullable: false),
                    publishers = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scraped_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_games", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cafe_inventory",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    game_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    available = table.Column<int>(type: "integer", nullable: false),
                    location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    condition = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    added_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cafe_inventory", x => x.id);
                    table.ForeignKey(
                        name: "FK_cafe_inventory_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rulebooks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    game_id = table.Column<int>(type: "integer", nullable: false),
                    bgg_file_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    original_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    file_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    language = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    local_file_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rulebooks", x => x.id);
                    table.ForeignKey(
                        name: "FK_rulebooks_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cafe_inventory_game_id",
                table: "cafe_inventory",
                column: "game_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_games_bgg_id",
                table: "games",
                column: "bgg_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_games_bgg_rank",
                table: "games",
                column: "bgg_rank");

            migrationBuilder.CreateIndex(
                name: "IX_games_status",
                table: "games",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_rulebooks_game_id",
                table: "rulebooks",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "IX_rulebooks_status",
                table: "rulebooks",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cafe_inventory");

            migrationBuilder.DropTable(
                name: "rulebooks");

            migrationBuilder.DropTable(
                name: "games");
        }
    }
}
