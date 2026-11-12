using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wms.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingExchangeStageConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_ShippingExchangeTargets_Stage",
                table: "ShippingExchangeTargets",
                sql: "[DecisionRevision] >= 0 AND [OperationalRevision] >= 0 AND (([Stage] = 0 AND [PatchConfirmedAtUtc] IS NULL AND [PostConfirmedAtUtc] IS NULL) OR ([Stage] = 1 AND [PatchConfirmedAtUtc] IS NOT NULL AND [PostConfirmedAtUtc] IS NULL AND [LastAttemptAtUtc] IS NOT NULL) OR ([Stage] = 2 AND [PatchConfirmedAtUtc] IS NOT NULL AND [PostConfirmedAtUtc] IS NOT NULL AND [PostConfirmedAtUtc] >= [PatchConfirmedAtUtc] AND [LastError] IS NULL) OR ([Stage] = 3 AND [PostConfirmedAtUtc] IS NULL AND [LastAttemptAtUtc] IS NOT NULL AND [LastError] IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ShippingExchangeTargets_Stage",
                table: "ShippingExchangeTargets");
        }
    }
}
