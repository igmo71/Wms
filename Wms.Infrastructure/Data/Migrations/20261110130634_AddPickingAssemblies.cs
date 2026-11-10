using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPickingAssemblies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PickingAssemblyId",
                table: "WarehouseAuditEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PickingAssemblies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OperationalRevision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ResponsibleUserId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FirstStartAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingAssemblies", x => x.Id);
                    table.CheckConstraint("CK_PickingAssemblies_NumberFormat", "LEN([Number]) = 13 AND [Number] LIKE '[0-9][0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9]'");
                    table.ForeignKey(
                        name: "FK_PickingAssemblies_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PickingAssemblyPlanLines",
                columns: table => new
                {
                    PickingAssemblyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockKeepingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(15,3)", precision: 15, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingAssemblyPlanLines", x => new { x.PickingAssemblyId, x.ShippingOrderId, x.StockKeepingUnitId });
                    table.CheckConstraint("CK_PickingAssemblyPlanLines_Quantity", "[Quantity] >= 0");
                    table.ForeignKey(
                        name: "FK_PickingAssemblyPlanLines_PickingAssemblies_PickingAssemblyId",
                        column: x => x.PickingAssemblyId,
                        principalTable: "PickingAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PickingAssemblyPlanLines_ShippingOrders_ShippingOrderId",
                        column: x => x.ShippingOrderId,
                        principalTable: "ShippingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingAssemblyPlanLines_StockKeepingUnits_StockKeepingUnitId",
                        column: x => x.StockKeepingUnitId,
                        principalTable: "StockKeepingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PickingAssemblySourceLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PickingAssemblyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanSourceSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttachedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AttachedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    DetachedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DetachedBy = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    StartConfirmedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastStartAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastStartError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingAssemblySourceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PickingAssemblySourceLinks_PickingAssemblies_PickingAssemblyId",
                        column: x => x.PickingAssemblyId,
                        principalTable: "PickingAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingAssemblySourceLinks_ShippingOrders_ShippingOrderId",
                        column: x => x.ShippingOrderId,
                        principalTable: "ShippingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseAuditEvents_PickingAssemblyId_OccurredAtUtc",
                table: "WarehouseAuditEvents",
                columns: new[] { "PickingAssemblyId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PickingAssemblies_Number",
                table: "PickingAssemblies",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingAssemblies_ResponsibleUserId_Status",
                table: "PickingAssemblies",
                columns: new[] { "ResponsibleUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PickingAssemblies_WarehouseId",
                table: "PickingAssemblies",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingAssemblyPlanLines_ShippingOrderId",
                table: "PickingAssemblyPlanLines",
                column: "ShippingOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingAssemblyPlanLines_StockKeepingUnitId",
                table: "PickingAssemblyPlanLines",
                column: "StockKeepingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingAssemblySourceLinks_PickingAssemblyId",
                table: "PickingAssemblySourceLinks",
                column: "PickingAssemblyId");

            migrationBuilder.CreateIndex(
                name: "UX_PickingAssemblySourceLinks_ActiveSource",
                table: "PickingAssemblySourceLinks",
                column: "ShippingOrderId",
                unique: true,
                filter: "[DetachedAtUtc] IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PickingReservations_PickingAssemblies_PickingAssemblyId",
                table: "PickingReservations",
                column: "PickingAssemblyId",
                principalTable: "PickingAssemblies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReplenishmentTasks_PickingAssemblies_PickingAssemblyId",
                table: "ReplenishmentTasks",
                column: "PickingAssemblyId",
                principalTable: "PickingAssemblies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WarehouseAuditEvents_PickingAssemblies_PickingAssemblyId",
                table: "WarehouseAuditEvents",
                column: "PickingAssemblyId",
                principalTable: "PickingAssemblies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PickingReservations_PickingAssemblies_PickingAssemblyId",
                table: "PickingReservations");

            migrationBuilder.DropForeignKey(
                name: "FK_ReplenishmentTasks_PickingAssemblies_PickingAssemblyId",
                table: "ReplenishmentTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_WarehouseAuditEvents_PickingAssemblies_PickingAssemblyId",
                table: "WarehouseAuditEvents");

            migrationBuilder.DropTable(
                name: "PickingAssemblyPlanLines");

            migrationBuilder.DropTable(
                name: "PickingAssemblySourceLinks");

            migrationBuilder.DropTable(
                name: "PickingAssemblies");

            migrationBuilder.DropIndex(
                name: "IX_WarehouseAuditEvents_PickingAssemblyId_OccurredAtUtc",
                table: "WarehouseAuditEvents");

            migrationBuilder.DropColumn(
                name: "PickingAssemblyId",
                table: "WarehouseAuditEvents");
        }
    }
}
