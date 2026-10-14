using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLicensePlateNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "LicensePlateNumberSequence",
                minValue: 1L,
                maxValue: 999999999999L);

            migrationBuilder.CreateTable(
                name: "LicensePlateNumberBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IssuedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensePlateNumberBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LicensePlateNumbers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false, defaultValueSql: "'LPN' + RIGHT('000000000000' + CONVERT(varchar(12), NEXT VALUE FOR [LicensePlateNumberSequence]), 12)")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensePlateNumbers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicensePlateNumbers_LicensePlateNumberBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "LicensePlateNumberBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateNumberBatches_IssuedAtUtc",
                table: "LicensePlateNumberBatches",
                column: "IssuedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateNumbers_BatchId",
                table: "LicensePlateNumbers",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlateNumbers_Code",
                table: "LicensePlateNumbers",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicensePlateNumbers");

            migrationBuilder.DropTable(
                name: "LicensePlateNumberBatches");

            migrationBuilder.DropSequence(
                name: "LicensePlateNumberSequence");
        }
    }
}
