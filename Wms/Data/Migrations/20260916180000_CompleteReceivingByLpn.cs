using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260916180000_CompleteReceivingByLpn")]
public partial class CompleteReceivingByLpn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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
            name: "CompletionApprovalReason",
            table: "ReceivingOrders",
            type: "nvarchar(1000)",
            maxLength: 1000,
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
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CompletionApprovedAtUtc", table: "ReceivingOrders");
        migrationBuilder.DropColumn(name: "CompletionApprovedBy", table: "ReceivingOrders");
        migrationBuilder.DropColumn(name: "CompletionApprovalReason", table: "ReceivingOrders");
        migrationBuilder.DropColumn(name: "CompletionCurrentPlanJson", table: "ReceivingOrders");
        migrationBuilder.DropColumn(name: "CompletionDifferencesJson", table: "ReceivingOrders");
        migrationBuilder.DropColumn(name: "CompletionFactJson", table: "ReceivingOrders");
        migrationBuilder.DropColumn(name: "CompletionFingerprint", table: "ReceivingOrders");
        migrationBuilder.DropColumn(name: "CompletionOriginalPlanJson", table: "ReceivingOrders");
        migrationBuilder.DropColumn(name: "CompletionSourceCheckedAtUtc", table: "ReceivingOrders");
    }
}
