using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyReceiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReceivingOrderParticipants_ReceivingCycleId_UserId",
                table: "ReceivingOrderParticipants");

            migrationBuilder.DropColumn(
                name: "BaseOrderId",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "BaseOrderType",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "BusinessOperation",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "Comment",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "CompletionApprovalReason",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "CompletionApprovedAtUtc",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "CompletionApprovedBy",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "CompletionCurrentPlanJson",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "CompletionDifferencesJson",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "CompletionFactJson",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "CompletionFingerprint",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "CompletionOriginalPlanJson",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "CompletionSourceCheckedAtUtc",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "DeletionMark",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "Posted",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "Queue",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "ShipperId",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "ShipperType",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "SynchronizationAcknowledgedAtUtc",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "SynchronizationAcknowledgedBy",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "SynchronizationDetectedAtUtc",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "SynchronizationFingerprint",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "SynchronizationLevel",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "WarehouseOperation",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "Comment",
                table: "ReceivingOrderItems");

            migrationBuilder.DropColumn(
                name: "LineNumber",
                table: "ReceivingOrderItems");

            migrationBuilder.DropColumn(
                name: "PlanQuantity",
                table: "ReceivingOrderItems");

            migrationBuilder.DropColumn(
                name: "ReopenedAtUtc",
                table: "LicensePlateNumbers");

            migrationBuilder.DropColumn(
                name: "ReopenedBy",
                table: "LicensePlateNumbers");

            migrationBuilder.AlterColumn<int>(
                name: "ActiveCycleNumber",
                table: "ReceivingOrders",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ReceivingCycleId",
                table: "ReceivingOrderParticipants",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrderParticipants_ReceivingCycleId_UserId",
                table: "ReceivingOrderParticipants",
                columns: new[] { "ReceivingCycleId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReceivingOrderParticipants_ReceivingCycleId_UserId",
                table: "ReceivingOrderParticipants");

            migrationBuilder.AlterColumn<int>(
                name: "ActiveCycleNumber",
                table: "ReceivingOrders",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<Guid>(
                name: "BaseOrderId",
                table: "ReceivingOrders",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "BaseOrderType",
                table: "ReceivingOrders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BusinessOperation",
                table: "ReceivingOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "ReceivingOrders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionApprovalReason",
                table: "ReceivingOrders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletionApprovedAtUtc",
                table: "ReceivingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionApprovedBy",
                table: "ReceivingOrders",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionCurrentPlanJson",
                table: "ReceivingOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionDifferencesJson",
                table: "ReceivingOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionFactJson",
                table: "ReceivingOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionFingerprint",
                table: "ReceivingOrders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionOriginalPlanJson",
                table: "ReceivingOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletionSourceCheckedAtUtc",
                table: "ReceivingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "Date",
                table: "ReceivingOrders",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "DeletionMark",
                table: "ReceivingOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "ReceivingOrders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Posted",
                table: "ReceivingOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Queue",
                table: "ReceivingOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ShipperId",
                table: "ReceivingOrders",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "ShipperType",
                table: "ReceivingOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SynchronizationAcknowledgedAtUtc",
                table: "ReceivingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SynchronizationAcknowledgedBy",
                table: "ReceivingOrders",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SynchronizationDetectedAtUtc",
                table: "ReceivingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SynchronizationFingerprint",
                table: "ReceivingOrders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SynchronizationLevel",
                table: "ReceivingOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAtUtc",
                table: "ReceivingOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseOperation",
                table: "ReceivingOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<Guid>(
                name: "ReceivingCycleId",
                table: "ReceivingOrderParticipants",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "ReceivingOrderItems",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LineNumber",
                table: "ReceivingOrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PlanQuantity",
                table: "ReceivingOrderItems",
                type: "decimal(15,3)",
                precision: 15,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReopenedAtUtc",
                table: "LicensePlateNumbers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReopenedBy",
                table: "LicensePlateNumbers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrderParticipants_ReceivingCycleId_UserId",
                table: "ReceivingOrderParticipants",
                columns: new[] { "ReceivingCycleId", "UserId" },
                unique: true,
                filter: "[ReceivingCycleId] IS NOT NULL");
        }
    }
}
