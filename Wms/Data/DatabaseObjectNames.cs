namespace Wms.Data;

internal static class DatabaseObjectNames
{
    public const string InventoryBalancesBusinessKeyIndex =
        "UX_InventoryBalances_BusinessKey";
    public const string InventoryMovementsTransferLineIndex =
        "UX_InventoryMovements_TransferLine";
    public const string InventoryTransfersActiveTransitLocationIndex =
        "IX_InventoryTransfers_TransitStorageLocationId";
}
