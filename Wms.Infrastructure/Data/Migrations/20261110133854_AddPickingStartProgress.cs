using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPickingStartProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartPatchRequestedAtUtc",
                table: "PickingAssemblySourceLinks",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StartLeaseId",
                table: "PickingAssemblies",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartLeaseUntilUtc",
                table: "PickingAssemblies",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartPatchRequestedAtUtc",
                table: "PickingAssemblySourceLinks");

            migrationBuilder.DropColumn(
                name: "StartLeaseId",
                table: "PickingAssemblies");

            migrationBuilder.DropColumn(
                name: "StartLeaseUntilUtc",
                table: "PickingAssemblies");
        }
    }
}
