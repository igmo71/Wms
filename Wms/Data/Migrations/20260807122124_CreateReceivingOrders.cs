using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateReceivingOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFolder",
                table: "StockKeepingUnits");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "StockKeepingUnits");

            migrationBuilder.AlterColumn<Guid>(
                name: "WarehouseId",
                table: "Zones",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "Numerator",
                table: "UnitsOfMeasure",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<long>(
                name: "Denominator",
                table: "UnitsOfMeasure",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<Guid>(
                name: "ZoneId",
                table: "StorageLocations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "WarehouseId",
                table: "StorageLocations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<double>(type: "float(18)", precision: 18, scale: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_StorageLocations_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryTurnovers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityDelta = table.Column<double>(type: "float(18)", precision: 18, scale: 3, nullable: false),
                    BalanceBefore = table.Column<double>(type: "float(18)", precision: 18, scale: 3, nullable: false),
                    BalanceAfter = table.Column<double>(type: "float(18)", precision: 18, scale: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecorderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecorderLineNumber = table.Column<int>(type: "int", nullable: false),
                    RecorderType = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTurnovers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryTurnovers_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTurnovers_StorageLocations_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTurnovers_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Posted = table.Column<bool>(type: "bit", nullable: false),
                    DeletionMark = table.Column<bool>(type: "bit", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivingLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Queue = table.Column<int>(type: "int", nullable: false),
                    WarehouseOperation = table.Column<int>(type: "int", nullable: false),
                    BusinessOperation = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExternalChangeDetected = table.Column<bool>(type: "bit", nullable: false),
                    StartedBy = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CompletedBy = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SenderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseOrderType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivingOrders_StorageLocations_ReceivingLocationId",
                        column: x => x.ReceivingLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingOrders_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingOrderItems",
                columns: table => new
                {
                    ReceivingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanQuantity = table.Column<double>(type: "float", nullable: false),
                    FactQuantity = table.Column<double>(type: "float", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingOrderItems", x => new { x.ReceivingOrderId, x.LineNumber });
                    table.ForeignKey(
                        name: "FK_ReceivingOrderItems_ReceivingOrders_ReceivingOrderId",
                        column: x => x.ReceivingOrderId,
                        principalTable: "ReceivingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReceivingOrderItems_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_StockKeepingUnitId",
                table: "InventoryBalances",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_StorageLocationId",
                table: "InventoryBalances",
                column: "StorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_WarehouseId_StorageLocationId_StockKeepingUnitId",
                table: "InventoryBalances",
                columns: new[] { "WarehouseId", "StorageLocationId", "StockKeepingUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTurnovers_RecorderType_RecorderId_RecorderLineNumber",
                table: "InventoryTurnovers",
                columns: new[] { "RecorderType", "RecorderId", "RecorderLineNumber" },
                unique: true,
                filter: "[RecorderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTurnovers_StockKeepingUnitId",
                table: "InventoryTurnovers",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTurnovers_StorageLocationId",
                table: "InventoryTurnovers",
                column: "StorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTurnovers_WarehouseId",
                table: "InventoryTurnovers",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrderItems_StockKeepingUnitId",
                table: "ReceivingOrderItems",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrders_ReceivingLocationId",
                table: "ReceivingOrders",
                column: "ReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrders_WarehouseId",
                table: "ReceivingOrders",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryBalances");

            migrationBuilder.DropTable(
                name: "InventoryTurnovers");

            migrationBuilder.DropTable(
                name: "ReceivingOrderItems");

            migrationBuilder.DropTable(
                name: "ReceivingOrders");

            migrationBuilder.AlterColumn<Guid>(
                name: "WarehouseId",
                table: "Zones",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<double>(
                name: "Numerator",
                table: "UnitsOfMeasure",
                type: "float",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<double>(
                name: "Denominator",
                table: "UnitsOfMeasure",
                type: "float",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<Guid>(
                name: "ZoneId",
                table: "StorageLocations",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "WarehouseId",
                table: "StorageLocations",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<bool>(
                name: "IsFolder",
                table: "StockKeepingUnits",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "StockKeepingUnits",
                type: "uniqueidentifier",
                nullable: true);
        }
    }
}
