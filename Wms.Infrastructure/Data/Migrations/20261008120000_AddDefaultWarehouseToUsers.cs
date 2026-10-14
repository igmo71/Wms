using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    public partial class AddDefaultWarehouseToUsers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultWarehouseId",
                table: "AspNetUsers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DefaultWarehouseId",
                table: "AspNetUsers",
                column: "DefaultWarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Warehouses_DefaultWarehouseId",
                table: "AspNetUsers",
                column: "DefaultWarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Warehouses_DefaultWarehouseId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_DefaultWarehouseId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DefaultWarehouseId",
                table: "AspNetUsers");
        }
    }
}
