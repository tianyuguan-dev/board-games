using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BoardGames.Migrations
{
    /// <inheritdoc />
    public partial class AddAvalonGameHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AvalonGameHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoomId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PlayerCount = table.Column<int>(type: "integer", nullable: false),
                    MaxRejects = table.Column<int>(type: "integer", nullable: false),
                    Winner = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    WinReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BonusAssassination = table.Column<bool>(type: "boolean", nullable: false),
                    EarlyAssassination = table.Column<bool>(type: "boolean", nullable: false),
                    AssassinTargetSeat = table.Column<int>(type: "integer", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvalonGameHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AvalonGamePlayers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SeatIndex = table.Column<int>(type: "integer", nullable: false),
                    Nickname = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsWinner = table.Column<bool>(type: "boolean", nullable: false),
                    BalanceDelta = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvalonGamePlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AvalonGamePlayers_AvalonGameHistories_GameId",
                        column: x => x.GameId,
                        principalTable: "AvalonGameHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AvalonGameProposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    MissionIndex = table.Column<int>(type: "integer", nullable: false),
                    ProposalIndex = table.Column<int>(type: "integer", nullable: false),
                    LeaderSeatIndex = table.Column<int>(type: "integer", nullable: false),
                    TeamSeats = table.Column<int[]>(type: "integer[]", nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: true),
                    SuccessCount = table.Column<int>(type: "integer", nullable: false),
                    FailCount = table.Column<int>(type: "integer", nullable: false),
                    MissionResult = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvalonGameProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AvalonGameProposals_AvalonGameHistories_GameId",
                        column: x => x.GameId,
                        principalTable: "AvalonGameHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AvalonGameVotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProposalId = table.Column<int>(type: "integer", nullable: false),
                    VoterSeatIndex = table.Column<int>(type: "integer", nullable: false),
                    Approve = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvalonGameVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AvalonGameVotes_AvalonGameProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "AvalonGameProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AvalonGameHistories_EndedAt",
                table: "AvalonGameHistories",
                column: "EndedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_AvalonGamePlayers_GameId",
                table: "AvalonGamePlayers",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_AvalonGamePlayers_UserId",
                table: "AvalonGamePlayers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AvalonGameProposals_GameId",
                table: "AvalonGameProposals",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_AvalonGameVotes_ProposalId",
                table: "AvalonGameVotes",
                column: "ProposalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvalonGamePlayers");

            migrationBuilder.DropTable(
                name: "AvalonGameVotes");

            migrationBuilder.DropTable(
                name: "AvalonGameProposals");

            migrationBuilder.DropTable(
                name: "AvalonGameHistories");
        }
    }
}
