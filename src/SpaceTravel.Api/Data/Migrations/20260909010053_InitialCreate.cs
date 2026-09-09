using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaceTravel.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Planets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DistanceRank = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Planets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shuttles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MaxLifeForms = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxWeightKg = table.Column<double>(type: "REAL", nullable: false),
                    CurrentPlanetId = table.Column<int>(type: "INTEGER", nullable: true),
                    FlyingToPlanetId = table.Column<int>(type: "INTEGER", nullable: true),
                    PlannedDestinationId = table.Column<int>(type: "INTEGER", nullable: true),
                    DepartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ArrivesAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shuttles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shuttles_Planets_CurrentPlanetId",
                        column: x => x.CurrentPlanetId,
                        principalTable: "Planets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shuttles_Planets_FlyingToPlanetId",
                        column: x => x.FlyingToPlanetId,
                        principalTable: "Planets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shuttles_Planets_PlannedDestinationId",
                        column: x => x.PlannedDestinationId,
                        principalTable: "Planets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TravelHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TravelRequestId = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginPlanetId = table.Column<int>(type: "INTEGER", nullable: false),
                    DestinationPlanetId = table.Column<int>(type: "INTEGER", nullable: false),
                    LifeFormCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalWeightKg = table.Column<double>(type: "REAL", nullable: false),
                    ShuttleId = table.Column<int>(type: "INTEGER", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RejectionReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TravelHistory_Planets_DestinationPlanetId",
                        column: x => x.DestinationPlanetId,
                        principalTable: "Planets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TravelHistory_Planets_OriginPlanetId",
                        column: x => x.OriginPlanetId,
                        principalTable: "Planets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TravelRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OriginPlanetId = table.Column<int>(type: "INTEGER", nullable: false),
                    DestinationPlanetId = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AssignedShuttleId = table.Column<int>(type: "INTEGER", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RejectionReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TravelRequests_Planets_DestinationPlanetId",
                        column: x => x.DestinationPlanetId,
                        principalTable: "Planets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TravelRequests_Planets_OriginPlanetId",
                        column: x => x.OriginPlanetId,
                        principalTable: "Planets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TravelRequests_Shuttles_AssignedShuttleId",
                        column: x => x.AssignedShuttleId,
                        principalTable: "Shuttles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LifeForms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Species = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    WeightKg = table.Column<double>(type: "REAL", nullable: false),
                    TravelRequestId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LifeForms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LifeForms_TravelRequests_TravelRequestId",
                        column: x => x.TravelRequestId,
                        principalTable: "TravelRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LifeForms_TravelRequestId",
                table: "LifeForms",
                column: "TravelRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Planets_DistanceRank",
                table: "Planets",
                column: "DistanceRank",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Planets_Name",
                table: "Planets",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shuttles_CurrentPlanetId",
                table: "Shuttles",
                column: "CurrentPlanetId");

            migrationBuilder.CreateIndex(
                name: "IX_Shuttles_FlyingToPlanetId",
                table: "Shuttles",
                column: "FlyingToPlanetId");

            migrationBuilder.CreateIndex(
                name: "IX_Shuttles_PlannedDestinationId",
                table: "Shuttles",
                column: "PlannedDestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelHistory_DestinationPlanetId",
                table: "TravelHistory",
                column: "DestinationPlanetId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelHistory_OriginPlanetId",
                table: "TravelHistory",
                column: "OriginPlanetId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelHistory_RecordedAtUtc",
                table: "TravelHistory",
                column: "RecordedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TravelHistory_TravelRequestId",
                table: "TravelHistory",
                column: "TravelRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TravelRequests_AssignedShuttleId",
                table: "TravelRequests",
                column: "AssignedShuttleId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelRequests_DestinationPlanetId",
                table: "TravelRequests",
                column: "DestinationPlanetId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelRequests_OriginPlanetId",
                table: "TravelRequests",
                column: "OriginPlanetId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelRequests_Status",
                table: "TravelRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LifeForms");

            migrationBuilder.DropTable(
                name: "TravelHistory");

            migrationBuilder.DropTable(
                name: "TravelRequests");

            migrationBuilder.DropTable(
                name: "Shuttles");

            migrationBuilder.DropTable(
                name: "Planets");
        }
    }
}
