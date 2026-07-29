using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Data.Migrations;

/// <inheritdoc />
public partial class CreateUnitsOfMeasure : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UnitsOfMeasure",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                Abbreviation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                DeletionMark = table.Column<bool>(type: "bit", nullable: false),
                Numerator = table.Column<double>(type: "float", nullable: false),
                Denominator = table.Column<double>(type: "float", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UnitsOfMeasure", x => x.Id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "UnitsOfMeasure");
    }
}
