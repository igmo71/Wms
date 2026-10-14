using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations;

public partial class GeneralizeCommandReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(name: "PK_MobileCommandReceipts", table: "MobileCommandReceipts");
        migrationBuilder.RenameTable(name: "MobileCommandReceipts", newName: "CommandReceipts");
        migrationBuilder.RenameColumn(name: "ClientRequestId", table: "CommandReceipts", newName: "RequestId");
        migrationBuilder.AddPrimaryKey(name: "PK_CommandReceipts", table: "CommandReceipts",
            columns: new[] { "UserId", "CommandType", "RequestId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(name: "PK_CommandReceipts", table: "CommandReceipts");
        migrationBuilder.RenameColumn(name: "RequestId", table: "CommandReceipts", newName: "ClientRequestId");
        migrationBuilder.RenameTable(name: "CommandReceipts", newName: "MobileCommandReceipts");
        migrationBuilder.AddPrimaryKey(name: "PK_MobileCommandReceipts", table: "MobileCommandReceipts",
            columns: new[] { "UserId", "CommandType", "ClientRequestId" });
    }
}
