using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingCancellationReturnProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastReturnAttemptAtUtc",
                table: "PickingAssemblySourceLinks",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastReturnError",
                table: "PickingAssemblySourceLinks",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReturnPatchConfirmedAtUtc",
                table: "PickingAssemblySourceLinks",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReturnPatchRequestedAtUtc",
                table: "PickingAssemblySourceLinks",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReturnPostConfirmedAtUtc",
                table: "PickingAssemblySourceLinks",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastReturnAttemptAtUtc",
                table: "PickingAssemblySourceLinks");

            migrationBuilder.DropColumn(
                name: "LastReturnError",
                table: "PickingAssemblySourceLinks");

            migrationBuilder.DropColumn(
                name: "ReturnPatchConfirmedAtUtc",
                table: "PickingAssemblySourceLinks");

            migrationBuilder.DropColumn(
                name: "ReturnPatchRequestedAtUtc",
                table: "PickingAssemblySourceLinks");

            migrationBuilder.DropColumn(
                name: "ReturnPostConfirmedAtUtc",
                table: "PickingAssemblySourceLinks");
        }
    }
}
