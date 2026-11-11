using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPickingFactsAndExchangeTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PickingAllocations",
                columns: table => new
                {
                    PickingAssemblyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingAllocations", x => new { x.PickingAssemblyId, x.ShippingOrderId, x.StockKeepingUnitId });
                    table.CheckConstraint("CK_PickingAllocations_Quantity", "[Quantity] >= 0");
                    table.ForeignKey(
                        name: "FK_PickingAllocations_PickingAssemblies_PickingAssemblyId",
                        column: x => x.PickingAssemblyId,
                        principalTable: "PickingAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingAllocations_ShippingOrders_ShippingOrderId",
                        column: x => x.ShippingOrderId,
                        principalTable: "ShippingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingAllocations_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PickingClosures",
                columns: table => new
                {
                    PickingAssemblyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ManagerOverride = table.Column<bool>(type: "bit", nullable: false),
                    DecisionReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReviewedSourcesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MovementIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingClosures", x => x.PickingAssemblyId);
                    table.ForeignKey(
                        name: "FK_PickingClosures_PickingAssemblies_PickingAssemblyId",
                        column: x => x.PickingAssemblyId,
                        principalTable: "PickingAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PickingEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PickingAssemblyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    OperationalRevision = table.Column<long>(type: "bigint", nullable: false),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastChangedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    LastChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingEntries", x => x.Id);
                    table.CheckConstraint("CK_PickingEntries_Quantity", "[Quantity] >= 0");
                    table.ForeignKey(
                        name: "FK_PickingEntries_PickingAssemblies_PickingAssemblyId",
                        column: x => x.PickingAssemblyId,
                        principalTable: "PickingAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingEntries_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingEntries_StorageLocations_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingEntries_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShippingExchangeTargets",
                columns: table => new
                {
                    PickingAssemblyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecisionRevision = table.Column<long>(type: "bigint", nullable: false),
                    ReviewedSourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetDocumentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetStatus = table.Column<int>(type: "int", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    PatchConfirmedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PostConfirmedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OperationalRevision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingExchangeTargets", x => new { x.PickingAssemblyId, x.ShippingOrderId });
                    table.ForeignKey(
                        name: "FK_ShippingExchangeTargets_PickingAssemblies_PickingAssemblyId",
                        column: x => x.PickingAssemblyId,
                        principalTable: "PickingAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShippingExchangeTargets_ShippingOrders_ShippingOrderId",
                        column: x => x.ShippingOrderId,
                        principalTable: "ShippingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PickingAllocations_ShippingOrderId",
                table: "PickingAllocations",
                column: "ShippingOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingAllocations_StockKeepingUnitId",
                table: "PickingAllocations",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingEntries_PickingAssemblyId_StockKeepingUnitId",
                table: "PickingEntries",
                columns: new[] { "PickingAssemblyId", "StockKeepingUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_PickingEntries_StockKeepingUnitId",
                table: "PickingEntries",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingEntries_StorageLocationId",
                table: "PickingEntries",
                column: "StorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingEntries_WarehouseId",
                table: "PickingEntries",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingExchangeTargets_ShippingOrderId",
                table: "ShippingExchangeTargets",
                column: "ShippingOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingExchangeTargets_Stage_LastAttemptAtUtc",
                table: "ShippingExchangeTargets",
                columns: new[] { "Stage", "LastAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PickingAllocations");

            migrationBuilder.DropTable(
                name: "PickingClosures");

            migrationBuilder.DropTable(
                name: "PickingEntries");

            migrationBuilder.DropTable(
                name: "ShippingExchangeTargets");
        }
    }
}
