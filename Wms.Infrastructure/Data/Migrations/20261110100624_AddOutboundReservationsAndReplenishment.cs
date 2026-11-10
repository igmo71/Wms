using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundReservationsAndReplenishment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PickingReservations",
                columns: table => new
                {
                    PickingEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PickingAssemblyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingReservations", x => x.PickingEntryId);
                    table.CheckConstraint("CK_PickingReservations_Quantity", "([CompletedAtUtc] IS NULL AND [Quantity] > 0) OR ([CompletedAtUtc] IS NOT NULL AND [Quantity] = 0)");
                    table.ForeignKey(
                        name: "FK_PickingReservations_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingReservations_StorageLocations_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingReservations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReplenishmentTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PickingAssemblyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    TransferredQuantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    RecommendedSourceStorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecommendedDestinationStorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OrderedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    AssignedTo = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    OrderedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplenishmentTasks", x => x.Id);
                    table.CheckConstraint("CK_ReplenishmentTasks_Quantity", "[OrderedQuantity] > 0 AND [TransferredQuantity] >= 0 AND [TransferredQuantity] <= [OrderedQuantity]");
                    table.ForeignKey(
                        name: "FK_ReplenishmentTasks_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReplenishmentTasks_StorageLocations_RecommendedDestinationStorageLocationId",
                        column: x => x.RecommendedDestinationStorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReplenishmentTasks_StorageLocations_RecommendedSourceStorageLocationId",
                        column: x => x.RecommendedSourceStorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReplenishmentTasks_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockShortages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    OriginId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedQuantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    FoundQuantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReportedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ReportedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolutionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorrectionMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockShortages", x => x.Id);
                    table.CheckConstraint("CK_StockShortages_Quantity", "[ExpectedQuantity] > [FoundQuantity] AND [FoundQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_StockShortages_InventoryMovements_CorrectionMovementId",
                        column: x => x.CorrectionMovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockShortages_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockShortages_StorageLocations_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockShortages_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReplenishmentSourceCycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReplenishmentTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceStorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SourceWasEmpty = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplenishmentSourceCycles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplenishmentSourceCycles_ReplenishmentTasks_ReplenishmentTaskId",
                        column: x => x.ReplenishmentTaskId,
                        principalTable: "ReplenishmentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReplenishmentSourceCycles_StorageLocations_SourceStorageLocationId",
                        column: x => x.SourceStorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReplenishmentTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReplenishmentSourceCycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationStorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    InventoryMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplenishmentTransfers", x => x.Id);
                    table.CheckConstraint("CK_ReplenishmentTransfers_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_ReplenishmentTransfers_InventoryMovements_InventoryMovementId",
                        column: x => x.InventoryMovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReplenishmentTransfers_ReplenishmentSourceCycles_ReplenishmentSourceCycleId",
                        column: x => x.ReplenishmentSourceCycleId,
                        principalTable: "ReplenishmentSourceCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReplenishmentTransfers_StorageLocations_DestinationStorageLocationId",
                        column: x => x.DestinationStorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PickingReservations_PickingAssemblyId",
                table: "PickingReservations",
                column: "PickingAssemblyId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingReservations_StockKeepingUnitId",
                table: "PickingReservations",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingReservations_StorageLocationId",
                table: "PickingReservations",
                column: "StorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingReservations_WarehouseId_StorageLocationId_StockKeepingUnitId_ExpirationDate",
                table: "PickingReservations",
                columns: new[] { "WarehouseId", "StorageLocationId", "StockKeepingUnitId", "ExpirationDate" },
                filter: "[CompletedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentSourceCycles_SourceStorageLocationId",
                table: "ReplenishmentSourceCycles",
                column: "SourceStorageLocationId");

            migrationBuilder.CreateIndex(
                name: "UX_ReplenishmentSourceCycles_ActiveTask",
                table: "ReplenishmentSourceCycles",
                column: "ReplenishmentTaskId",
                unique: true,
                filter: "[CompletedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentTasks_PickingAssemblyId",
                table: "ReplenishmentTasks",
                column: "PickingAssemblyId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentTasks_RecommendedDestinationStorageLocationId",
                table: "ReplenishmentTasks",
                column: "RecommendedDestinationStorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentTasks_RecommendedSourceStorageLocationId",
                table: "ReplenishmentTasks",
                column: "RecommendedSourceStorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentTasks_StockKeepingUnitId",
                table: "ReplenishmentTasks",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentTasks_WarehouseId_Status_OrderedAtUtc",
                table: "ReplenishmentTasks",
                columns: new[] { "WarehouseId", "Status", "OrderedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentTransfers_DestinationStorageLocationId",
                table: "ReplenishmentTransfers",
                column: "DestinationStorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentTransfers_ReplenishmentSourceCycleId",
                table: "ReplenishmentTransfers",
                column: "ReplenishmentSourceCycleId");

            migrationBuilder.CreateIndex(
                name: "UX_ReplenishmentTransfers_Movement",
                table: "ReplenishmentTransfers",
                column: "InventoryMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockShortages_CorrectionMovementId",
                table: "StockShortages",
                column: "CorrectionMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_StockShortages_Origin_OriginId",
                table: "StockShortages",
                columns: new[] { "Origin", "OriginId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockShortages_StockKeepingUnitId",
                table: "StockShortages",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockShortages_StorageLocationId",
                table: "StockShortages",
                column: "StorageLocationId");

            migrationBuilder.CreateIndex(
                name: "UX_StockShortages_ActiveGroup",
                table: "StockShortages",
                columns: new[] { "WarehouseId", "StorageLocationId", "StockKeepingUnitId", "ExpirationDate" },
                unique: true,
                filter: "[ResolvedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PickingReservations");

            migrationBuilder.DropTable(
                name: "ReplenishmentTransfers");

            migrationBuilder.DropTable(
                name: "StockShortages");

            migrationBuilder.DropTable(
                name: "ReplenishmentSourceCycles");

            migrationBuilder.DropTable(
                name: "ReplenishmentTasks");
        }
    }
}
