using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReceivingStockLifecycleFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These legacy rows cannot be mapped to the new single-SKU keys without
            // changing recorded facts. Stop before altering the schema if they exist.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [ReceivingOrderItems]
                    GROUP BY [ReceivingOrderId], [StockKeepingUnitId]
                    HAVING COUNT_BIG(*) > 1)
                BEGIN
                    ;THROW 51001, 'Receiving order contains repeated SKU rows; reconcile them before this migration.', 1;
                END;

                IF EXISTS (
                    SELECT 1 FROM [LicensePlateNumberItems]
                    WHERE [DeletedAtUtc] IS NULL
                    GROUP BY [LicensePlateNumberId]
                    HAVING COUNT_BIG(*) > 1)
                BEGIN
                    ;THROW 51002, 'LPN contains multiple active items; reconcile it before this migration.', 1;
                END;

                IF EXISTS (
                    SELECT 1
                    FROM [LicensePlateNumberPutaways] AS p
                    LEFT JOIN [InventoryMovements] AS m ON m.[Id] = p.[InventoryMovementId]
                    WHERE m.[SourceStorageLocationId] IS NULL)
                BEGIN
                    ;THROW 51003, 'Putaway has no recorded source location; reconcile it before this migration.', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "IX_ReceivingOrderParticipants_ReceivingOrderId_UserId",
                table: "ReceivingOrderParticipants");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReceivingOrderItems",
                table: "ReceivingOrderItems");

            migrationBuilder.DropIndex(
                name: "UX_LicensePlateNumberItems_ActiveSku",
                table: "LicensePlateNumberItems");

            migrationBuilder.DropIndex(
                name: "UX_InventoryBalances_BusinessKey",
                table: "InventoryBalances");

            migrationBuilder.AddColumn<bool>(
                name: "TracksExpirationDate",
                table: "StockKeepingUnits",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ActiveCycleNumber",
                table: "ReceivingOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivingCycleId",
                table: "ReceivingOrderParticipants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentPlanQuantity",
                table: "ReceivingOrderItems",
                type: "decimal(15,3)",
                precision: 15,
                scale: 3,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE [ReceivingOrderItems]
                SET [CurrentPlanQuantity] = [PlanQuantity];
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalReceivingLocationId",
                table: "LicensePlateNumbers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE l
                SET [OriginalReceivingLocationId] = r.[ReceivingLocationId]
                FROM [LicensePlateNumbers] AS l
                INNER JOIN [ReceivingOrders] AS r ON r.[Id] = l.[ReceivingOrderId]
                WHERE r.[ReceivingLocationId] IS NOT NULL;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivingCycleId",
                table: "LicensePlateNumbers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpirationDate",
                table: "LicensePlateNumberPutaways",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceStorageLocationId",
                table: "LicensePlateNumberPutaways",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE p
                SET [SourceStorageLocationId] = m.[SourceStorageLocationId]
                FROM [LicensePlateNumberPutaways] AS p
                INNER JOIN [InventoryMovements] AS m ON m.[Id] = p.[InventoryMovementId];
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceStorageLocationId",
                table: "LicensePlateNumberPutaways",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpirationDate",
                table: "LicensePlateNumberItems",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpirationDate",
                table: "InventoryTurnovers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpirationDate",
                table: "InventoryMovements",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversedMovementId",
                table: "InventoryMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpirationDate",
                table: "InventoryBalances",
                type: "date",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReceivingOrderItems",
                table: "ReceivingOrderItems",
                columns: new[] { "ReceivingOrderId", "StockKeepingUnitId" });

            migrationBuilder.CreateTable(
                name: "ReceivingCycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    ReceivingLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    PlanSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FactSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceCheckFailed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingCycles", x => x.Id);
                    table.UniqueConstraint("AK_ReceivingCycles_Id_ReceivingOrderId", x => new { x.Id, x.ReceivingOrderId });
                    table.ForeignKey(
                        name: "FK_ReceivingCycles_ReceivingOrders_ReceivingOrderId",
                        column: x => x.ReceivingOrderId,
                        principalTable: "ReceivingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingCycles_StorageLocations_ReceivingLocationId",
                        column: x => x.ReceivingLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingSourceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastSuccessfulVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastSuccessfulCheckAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastAttemptError = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingSourceDocuments", x => x.Id);
                    table.CheckConstraint("CK_ReceivingSourceDocuments_RefKey_NotEmpty", "[RefKey] <> '00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateTable(
                name: "ReceivingOrderSourceLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivingSourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttachedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DetachedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingOrderSourceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivingOrderSourceLinks_ReceivingOrders_ReceivingOrderId",
                        column: x => x.ReceivingOrderId,
                        principalTable: "ReceivingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingOrderSourceLinks_ReceivingSourceDocuments_ReceivingSourceDocumentId",
                        column: x => x.ReceivingSourceDocumentId,
                        principalTable: "ReceivingSourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingSourceVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivingSourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ContentFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingSourceVersions", x => x.Id);
                    table.UniqueConstraint("AK_ReceivingSourceVersions_Id_ReceivingSourceDocumentId", x => new { x.Id, x.ReceivingSourceDocumentId });
                    table.ForeignKey(
                        name: "FK_ReceivingSourceVersions_ReceivingSourceDocuments_ReceivingSourceDocumentId",
                        column: x => x.ReceivingSourceDocumentId,
                        principalTable: "ReceivingSourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    ActionSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReceivingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LicensePlateNumberId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceivingSourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BeforeValuesJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AfterValuesJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    MovementIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceVersionIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CommandUserId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    CommandType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseAuditEvents_CommandReceipts_CommandUserId_CommandType_RequestId",
                        columns: x => new { x.CommandUserId, x.CommandType, x.RequestId },
                        principalTable: "CommandReceipts",
                        principalColumns: new[] { "UserId", "CommandType", "RequestId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseAuditEvents_LicensePlateNumbers_LicensePlateNumberId",
                        column: x => x.LicensePlateNumberId,
                        principalTable: "LicensePlateNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseAuditEvents_ReceivingOrders_ReceivingOrderId",
                        column: x => x.ReceivingOrderId,
                        principalTable: "ReceivingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseAuditEvents_ReceivingSourceDocuments_ReceivingSourceDocumentId",
                        column: x => x.ReceivingSourceDocumentId,
                        principalTable: "ReceivingSourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseAuditEvents_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingCycleSourceVersions",
                columns: table => new
                {
                    ReceivingCycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivingSourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivingSourceVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingCycleSourceVersions", x => new { x.ReceivingCycleId, x.ReceivingSourceDocumentId });
                    table.ForeignKey(
                        name: "FK_ReceivingCycleSourceVersions_ReceivingCycles_ReceivingCycleId",
                        column: x => x.ReceivingCycleId,
                        principalTable: "ReceivingCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingCycleSourceVersions_ReceivingSourceDocuments_ReceivingSourceDocumentId",
                        column: x => x.ReceivingSourceDocumentId,
                        principalTable: "ReceivingSourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingCycleSourceVersions_ReceivingSourceVersions_ReceivingSourceVersionId_ReceivingSourceDocumentId",
                        columns: x => new { x.ReceivingSourceVersionId, x.ReceivingSourceDocumentId },
                        principalTable: "ReceivingSourceVersions",
                        principalColumns: new[] { "Id", "ReceivingSourceDocumentId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingSourceLineProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivingSourceVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    SourceSkuRefKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RawQuantity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: true),
                    ProjectionError = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingSourceLineProjections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivingSourceLineProjections_ReceivingSourceVersions_ReceivingSourceVersionId",
                        column: x => x.ReceivingSourceVersionId,
                        principalTable: "ReceivingSourceVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReceivingSourceLineProjections_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrderParticipants_ReceivingCycleId_UserId",
                table: "ReceivingOrderParticipants",
                columns: new[] { "ReceivingCycleId", "UserId" },
                unique: true,
                filter: "[ReceivingCycleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrderParticipants_ReceivingOrderId",
                table: "ReceivingOrderParticipants",
                column: "ReceivingOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateNumbers_OriginalReceivingLocationId",
                table: "LicensePlateNumbers",
                column: "OriginalReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateNumbers_ReceivingCycleId_ReceivingOrderId",
                table: "LicensePlateNumbers",
                columns: new[] { "ReceivingCycleId", "ReceivingOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateNumberPutaways_SourceStorageLocationId",
                table: "LicensePlateNumberPutaways",
                column: "SourceStorageLocationId");

            migrationBuilder.CreateIndex(
                name: "UX_LicensePlateNumberItems_ActiveReceivingItem",
                table: "LicensePlateNumberItems",
                column: "LicensePlateNumberId",
                unique: true,
                filter: "[DeletedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryMovements_ReversedMovementId",
                table: "InventoryMovements",
                column: "ReversedMovementId",
                unique: true,
                filter: "[ReversedMovementId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryBalances_BusinessKey",
                table: "InventoryBalances",
                columns: new[] { "WarehouseId", "StorageLocationId", "StockKeepingUnitId", "ExpirationDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingCycles_ReceivingLocationId",
                table: "ReceivingCycles",
                column: "ReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingCycles_ReceivingOrderId_Number",
                table: "ReceivingCycles",
                columns: new[] { "ReceivingOrderId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingCycleSourceVersions_ReceivingSourceDocumentId",
                table: "ReceivingCycleSourceVersions",
                column: "ReceivingSourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingCycleSourceVersions_ReceivingSourceVersionId_ReceivingSourceDocumentId",
                table: "ReceivingCycleSourceVersions",
                columns: new[] { "ReceivingSourceVersionId", "ReceivingSourceDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrderSourceLinks_ReceivingOrderId",
                table: "ReceivingOrderSourceLinks",
                column: "ReceivingOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrderSourceLinks_ReceivingSourceDocumentId",
                table: "ReceivingOrderSourceLinks",
                column: "ReceivingSourceDocumentId",
                unique: true,
                filter: "[DetachedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSourceDocuments_LastSuccessfulVersionId",
                table: "ReceivingSourceDocuments",
                column: "LastSuccessfulVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSourceDocuments_RefKey",
                table: "ReceivingSourceDocuments",
                column: "RefKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSourceLineProjections_ReceivingSourceVersionId_LineNumber",
                table: "ReceivingSourceLineProjections",
                columns: new[] { "ReceivingSourceVersionId", "LineNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSourceLineProjections_StockKeepingUnitId",
                table: "ReceivingSourceLineProjections",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingSourceVersions_ReceivingSourceDocumentId_ReceivedAtUtc",
                table: "ReceivingSourceVersions",
                columns: new[] { "ReceivingSourceDocumentId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseAuditEvents_CommandUserId_CommandType_RequestId",
                table: "WarehouseAuditEvents",
                columns: new[] { "CommandUserId", "CommandType", "RequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseAuditEvents_LicensePlateNumberId_OccurredAtUtc",
                table: "WarehouseAuditEvents",
                columns: new[] { "LicensePlateNumberId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseAuditEvents_ReceivingOrderId_OccurredAtUtc",
                table: "WarehouseAuditEvents",
                columns: new[] { "ReceivingOrderId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseAuditEvents_ReceivingSourceDocumentId_OccurredAtUtc",
                table: "WarehouseAuditEvents",
                columns: new[] { "ReceivingSourceDocumentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseAuditEvents_StockKeepingUnitId_OccurredAtUtc",
                table: "WarehouseAuditEvents",
                columns: new[] { "StockKeepingUnitId", "OccurredAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_InventoryMovements_ReversedMovementId",
                table: "InventoryMovements",
                column: "ReversedMovementId",
                principalTable: "InventoryMovements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LicensePlateNumberPutaways_StorageLocations_SourceStorageLocationId",
                table: "LicensePlateNumberPutaways",
                column: "SourceStorageLocationId",
                principalTable: "StorageLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LicensePlateNumbers_ReceivingCycles_ReceivingCycleId_ReceivingOrderId",
                table: "LicensePlateNumbers",
                columns: new[] { "ReceivingCycleId", "ReceivingOrderId" },
                principalTable: "ReceivingCycles",
                principalColumns: new[] { "Id", "ReceivingOrderId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LicensePlateNumbers_StorageLocations_OriginalReceivingLocationId",
                table: "LicensePlateNumbers",
                column: "OriginalReceivingLocationId",
                principalTable: "StorageLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReceivingOrderParticipants_ReceivingCycles_ReceivingCycleId",
                table: "ReceivingOrderParticipants",
                column: "ReceivingCycleId",
                principalTable: "ReceivingCycles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_InventoryMovements_ReversedMovementId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_LicensePlateNumberPutaways_StorageLocations_SourceStorageLocationId",
                table: "LicensePlateNumberPutaways");

            migrationBuilder.DropForeignKey(
                name: "FK_LicensePlateNumbers_ReceivingCycles_ReceivingCycleId_ReceivingOrderId",
                table: "LicensePlateNumbers");

            migrationBuilder.DropForeignKey(
                name: "FK_LicensePlateNumbers_StorageLocations_OriginalReceivingLocationId",
                table: "LicensePlateNumbers");

            migrationBuilder.DropForeignKey(
                name: "FK_ReceivingOrderParticipants_ReceivingCycles_ReceivingCycleId",
                table: "ReceivingOrderParticipants");

            migrationBuilder.DropTable(
                name: "ReceivingCycleSourceVersions");

            migrationBuilder.DropTable(
                name: "ReceivingOrderSourceLinks");

            migrationBuilder.DropTable(
                name: "ReceivingSourceLineProjections");

            migrationBuilder.DropTable(
                name: "WarehouseAuditEvents");

            migrationBuilder.DropTable(
                name: "ReceivingCycles");

            migrationBuilder.DropTable(
                name: "ReceivingSourceVersions");

            migrationBuilder.DropTable(
                name: "ReceivingSourceDocuments");

            migrationBuilder.DropIndex(
                name: "IX_ReceivingOrderParticipants_ReceivingCycleId_UserId",
                table: "ReceivingOrderParticipants");

            migrationBuilder.DropIndex(
                name: "IX_ReceivingOrderParticipants_ReceivingOrderId",
                table: "ReceivingOrderParticipants");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReceivingOrderItems",
                table: "ReceivingOrderItems");

            migrationBuilder.DropIndex(
                name: "IX_LicensePlateNumbers_OriginalReceivingLocationId",
                table: "LicensePlateNumbers");

            migrationBuilder.DropIndex(
                name: "IX_LicensePlateNumbers_ReceivingCycleId_ReceivingOrderId",
                table: "LicensePlateNumbers");

            migrationBuilder.DropIndex(
                name: "IX_LicensePlateNumberPutaways_SourceStorageLocationId",
                table: "LicensePlateNumberPutaways");

            migrationBuilder.DropIndex(
                name: "UX_LicensePlateNumberItems_ActiveReceivingItem",
                table: "LicensePlateNumberItems");

            migrationBuilder.DropIndex(
                name: "UX_InventoryMovements_ReversedMovementId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "UX_InventoryBalances_BusinessKey",
                table: "InventoryBalances");

            migrationBuilder.DropColumn(
                name: "TracksExpirationDate",
                table: "StockKeepingUnits");

            migrationBuilder.DropColumn(
                name: "ActiveCycleNumber",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "ReceivingCycleId",
                table: "ReceivingOrderParticipants");

            migrationBuilder.DropColumn(
                name: "CurrentPlanQuantity",
                table: "ReceivingOrderItems");

            migrationBuilder.DropColumn(
                name: "OriginalReceivingLocationId",
                table: "LicensePlateNumbers");

            migrationBuilder.DropColumn(
                name: "ReceivingCycleId",
                table: "LicensePlateNumbers");

            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                table: "LicensePlateNumberPutaways");

            migrationBuilder.DropColumn(
                name: "SourceStorageLocationId",
                table: "LicensePlateNumberPutaways");

            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                table: "LicensePlateNumberItems");

            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                table: "InventoryTurnovers");

            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "ReversedMovementId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                table: "InventoryBalances");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReceivingOrderItems",
                table: "ReceivingOrderItems",
                columns: new[] { "ReceivingOrderId", "LineNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrderParticipants_ReceivingOrderId_UserId",
                table: "ReceivingOrderParticipants",
                columns: new[] { "ReceivingOrderId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_LicensePlateNumberItems_ActiveSku",
                table: "LicensePlateNumberItems",
                columns: new[] { "LicensePlateNumberId", "StockKeepingUnitId" },
                unique: true,
                filter: "[DeletedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryBalances_BusinessKey",
                table: "InventoryBalances",
                columns: new[] { "WarehouseId", "StorageLocationId", "StockKeepingUnitId" },
                unique: true);
        }
    }
}
