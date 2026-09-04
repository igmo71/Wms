using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateInitialWmsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "AspNetUsers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "DeliveryDirections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeletionMark = table.Column<bool>(type: "bit", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsFolder = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryDirections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Individuals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DeletionMark = table.Column<bool>(type: "bit", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsFolder = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Individuals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MobileCommandReceipts",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    CommandType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ResultResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileCommandReceipts", x => new { x.UserId, x.CommandType, x.ClientRequestId });
                });

            migrationBuilder.CreateTable(
                name: "OrganizationalUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DeletionMark = table.Column<bool>(type: "bit", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationalUnits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Partners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DeletionMark = table.Column<bool>(type: "bit", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UnitsOfMeasure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Abbreviation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    MeasurementType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DeletionMark = table.Column<bool>(type: "bit", nullable: false),
                    Numerator = table.Column<double>(type: "float", nullable: true),
                    Denominator = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitsOfMeasure", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DeletionMark = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockKeepingUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DeletionMark = table.Column<bool>(type: "bit", nullable: false),
                    BaseUnitOfMeasureId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WeightKg = table.Column<double>(type: "float", nullable: true),
                    VolumeM3 = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockKeepingUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockKeepingUnits_UnitsOfMeasure_BaseUnitOfMeasureId",
                        column: x => x.BaseUnitOfMeasureId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Zones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DeletionMark = table.Column<bool>(type: "bit", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Zones_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SkuBarcodes",
                columns: table => new
                {
                    SkuId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkuBarcodes", x => new { x.SkuId, x.Value });
                    table.ForeignKey(
                        name: "FK_SkuBarcodes_StockKeepingUnits_SkuId",
                        column: x => x.SkuId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StorageLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsFolder = table.Column<bool>(type: "bit", nullable: false),
                    DeletionMark = table.Column<bool>(type: "bit", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Dimensions_Length = table.Column<double>(type: "float", nullable: true),
                    Dimensions_Width = table.Column<double>(type: "float", nullable: true),
                    Dimensions_Height = table.Column<double>(type: "float", nullable: true),
                    Dimensions_Volume = table.Column<double>(type: "float", nullable: true),
                    Dimensions_VolumeFactor = table.Column<double>(type: "float", nullable: true),
                    Dimensions_MaxWeight = table.Column<double>(type: "float", nullable: true),
                    Coordinates_X = table.Column<double>(type: "float", nullable: true),
                    Coordinates_Y = table.Column<double>(type: "float", nullable: true),
                    Coordinates_Z = table.Column<double>(type: "float", nullable: true),
                    PickSequence = table.Column<long>(type: "bigint", nullable: true),
                    OperationalRevision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageLocations_StorageLocations_ParentId",
                        column: x => x.ParentId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorageLocations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorageLocations_Zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "Zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                name: "InventoryCounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryCounts_StorageLocations_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCounts_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceStorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DestinationStorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    RecorderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecorderLineNumber = table.Column<int>(type: "int", nullable: true),
                    RecorderType = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_StorageLocations_DestinationStorageLocationId",
                        column: x => x.DestinationStorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_StorageLocations_SourceStorageLocationId",
                        column: x => x.SourceStorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransitStorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    StartedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    CompletedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_StorageLocations_TransitStorageLocationId",
                        column: x => x.TransitStorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_Warehouses_WarehouseId",
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
                    OperationalRevision = table.Column<long>(type: "bigint", nullable: false),
                    DeletionMark = table.Column<bool>(type: "bit", nullable: false),
                    Posted = table.Column<bool>(type: "bit", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivingLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Queue = table.Column<int>(type: "int", nullable: false),
                    WarehouseOperation = table.Column<int>(type: "int", nullable: false),
                    BusinessOperation = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    CompletedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    PutawayStatus = table.Column<int>(type: "int", nullable: false),
                    PutawayStartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PutawayCompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PutawayStartedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    PutawayCompletedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    SynchronizationLevel = table.Column<int>(type: "int", nullable: false),
                    SynchronizationDetectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SynchronizationFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SynchronizationAcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SynchronizationAcknowledgedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    ShipperId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipperType = table.Column<int>(type: "int", nullable: false),
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
                name: "ShippingOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationalRevision = table.Column<long>(type: "bigint", nullable: false),
                    DeletionMark = table.Column<bool>(type: "bit", nullable: false),
                    Posted = table.Column<bool>(type: "bit", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Queue = table.Column<int>(type: "int", nullable: false),
                    PlannedShippingDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveryDirectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WarehouseOperation = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PickingStartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReadyForShipmentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ShippedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RolledBackAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PickingStartedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    ReadyForShipmentBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    ShippedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    RolledBackBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    RollbackReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SynchronizationLevel = table.Column<int>(type: "int", nullable: false),
                    SynchronizationDetectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SynchronizationFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SynchronizationAcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SynchronizationAcknowledgedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    ReceiverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiverType = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShippingOrders_DeliveryDirections_DeliveryDirectionId",
                        column: x => x.DeliveryDirectionId,
                        principalTable: "DeliveryDirections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShippingOrders_StorageLocations_ShippingLocationId",
                        column: x => x.ShippingLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShippingOrders_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorageLocationLocks",
                columns: table => new
                {
                    StorageLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerType = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    LockedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LockedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageLocationLocks", x => x.StorageLocationId);
                    table.ForeignKey(
                        name: "FK_StorageLocationLocks_StorageLocations_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryCountItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryCountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedQuantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCountItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryCountItems_InventoryCounts_InventoryCountId",
                        column: x => x.InventoryCountId,
                        principalTable: "InventoryCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryCountItems_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
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
                    QuantityDelta = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    BalanceBefore = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InventoryMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTurnovers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryTurnovers_InventoryMovements_InventoryMovementId",
                        column: x => x.InventoryMovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "ReceivingOrderItems",
                columns: table => new
                {
                    ReceivingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanQuantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    FactQuantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: true),
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
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShippingOrderBaseItems",
                columns: table => new
                {
                    ShippingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanQuantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    BaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseOrderType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingOrderBaseItems", x => new { x.ShippingOrderId, x.LineNumber });
                    table.ForeignKey(
                        name: "FK_ShippingOrderBaseItems_ShippingOrders_ShippingOrderId",
                        column: x => x.ShippingOrderId,
                        principalTable: "ShippingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShippingOrderBaseItems_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShippingOrderItems",
                columns: table => new
                {
                    ShippingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanQuantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    FactQuantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingOrderItems", x => new { x.ShippingOrderId, x.LineNumber });
                    table.ForeignKey(
                        name: "FK_ShippingOrderItems_ShippingOrders_ShippingOrderId",
                        column: x => x.ShippingOrderId,
                        principalTable: "ShippingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShippingOrderItems_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "UX_InventoryBalances_BusinessKey",
                table: "InventoryBalances",
                columns: new[] { "WarehouseId", "StorageLocationId", "StockKeepingUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountItems_InventoryCountId_LineNumber",
                table: "InventoryCountItems",
                columns: new[] { "InventoryCountId", "LineNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountItems_StockKeepingUnitId",
                table: "InventoryCountItems",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryCountItems_InventoryCountId_StockKeepingUnitId",
                table: "InventoryCountItems",
                columns: new[] { "InventoryCountId", "StockKeepingUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_Number",
                table: "InventoryCounts",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_StorageLocationId",
                table: "InventoryCounts",
                column: "StorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_WarehouseId_Date",
                table: "InventoryCounts",
                columns: new[] { "WarehouseId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_DestinationStorageLocationId",
                table: "InventoryMovements",
                column: "DestinationStorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SourceStorageLocationId",
                table: "InventoryMovements",
                column: "SourceStorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_StockKeepingUnitId",
                table: "InventoryMovements",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_WarehouseId",
                table: "InventoryMovements",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryMovements_TransferLine",
                table: "InventoryMovements",
                columns: new[] { "RecorderType", "RecorderId", "RecorderLineNumber" },
                unique: true,
                filter: "[RecorderType] = 4");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_Number",
                table: "InventoryTransfers",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_TransitStorageLocationId",
                table: "InventoryTransfers",
                column: "TransitStorageLocationId",
                unique: true,
                filter: "[TransitStorageLocationId] IS NOT NULL AND [Status] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_WarehouseId_Date",
                table: "InventoryTransfers",
                columns: new[] { "WarehouseId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTurnovers_InventoryMovementId",
                table: "InventoryTurnovers",
                column: "InventoryMovementId");

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

            migrationBuilder.CreateIndex(
                name: "IX_ShippingOrderBaseItems_StockKeepingUnitId",
                table: "ShippingOrderBaseItems",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingOrderItems_StockKeepingUnitId",
                table: "ShippingOrderItems",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingOrders_DeliveryDirectionId",
                table: "ShippingOrders",
                column: "DeliveryDirectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingOrders_ShippingLocationId",
                table: "ShippingOrders",
                column: "ShippingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingOrders_WarehouseId",
                table: "ShippingOrders",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockKeepingUnits_BaseUnitOfMeasureId",
                table: "StockKeepingUnits",
                column: "BaseUnitOfMeasureId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageLocations_ParentId",
                table: "StorageLocations",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageLocations_WarehouseId",
                table: "StorageLocations",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageLocations_ZoneId_Code",
                table: "StorageLocations",
                columns: new[] { "ZoneId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Zones_WarehouseId_Code",
                table: "Zones",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Individuals");

            migrationBuilder.DropTable(
                name: "InventoryBalances");

            migrationBuilder.DropTable(
                name: "InventoryCountItems");

            migrationBuilder.DropTable(
                name: "InventoryTransfers");

            migrationBuilder.DropTable(
                name: "InventoryTurnovers");

            migrationBuilder.DropTable(
                name: "MobileCommandReceipts");

            migrationBuilder.DropTable(
                name: "OrganizationalUnits");

            migrationBuilder.DropTable(
                name: "Partners");

            migrationBuilder.DropTable(
                name: "ReceivingOrderItems");

            migrationBuilder.DropTable(
                name: "ShippingOrderBaseItems");

            migrationBuilder.DropTable(
                name: "ShippingOrderItems");

            migrationBuilder.DropTable(
                name: "SkuBarcodes");

            migrationBuilder.DropTable(
                name: "StorageLocationLocks");

            migrationBuilder.DropTable(
                name: "InventoryCounts");

            migrationBuilder.DropTable(
                name: "InventoryMovements");

            migrationBuilder.DropTable(
                name: "ReceivingOrders");

            migrationBuilder.DropTable(
                name: "ShippingOrders");

            migrationBuilder.DropTable(
                name: "StockKeepingUnits");

            migrationBuilder.DropTable(
                name: "DeliveryDirections");

            migrationBuilder.DropTable(
                name: "StorageLocations");

            migrationBuilder.DropTable(
                name: "UnitsOfMeasure");

            migrationBuilder.DropTable(
                name: "Zones");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "AspNetUsers");
        }
    }
}
