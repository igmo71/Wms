/*
    Очистка прикладных данных WMS для текущей схемы базы данных.

    Не очищаются:
      - таблицы ASP.NET Core Identity (dbo.AspNet*);
      - dbo.__EFMigrationsHistory.

    Порядок DELETE соответствует внешним ключам текущей EF Core модели:
    сначала зависимые таблицы, затем родительские.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- История складских операций зависит от движений, мест хранения,
    -- номенклатуры и складов.
    DELETE FROM [dbo].[InventoryTurnovers];
    DELETE FROM [dbo].[InventoryMovements];
    DELETE FROM [dbo].[InventoryBalances];

    -- Строки документов удаляются раньше их заголовков.
    DELETE FROM [dbo].[InventoryCountItems];
    DELETE FROM [dbo].[ReceivingOrderItems];
    DELETE FROM [dbo].[ShippingOrderBaseItems];
    DELETE FROM [dbo].[ShippingOrderItems];

    -- Документы зависят от складов, мест хранения и справочников.
    DELETE FROM [dbo].[InventoryCounts];
    DELETE FROM [dbo].[InventoryTransfers];
    DELETE FROM [dbo].[ReceivingOrders];
    DELETE FROM [dbo].[ShippingOrders];

    -- Зависимые справочники и топология склада.
    DELETE FROM [dbo].[SkuBarcodes];

    -- Разрываем самоссылку StorageLocations.ParentId (ON DELETE NO ACTION).
    UPDATE [dbo].[StorageLocations]
    SET [ParentId] = NULL
    WHERE [ParentId] IS NOT NULL;

    DELETE FROM [dbo].[StorageLocations];
    DELETE FROM [dbo].[Zones];
    DELETE FROM [dbo].[StockKeepingUnits];

    -- Корневые справочники.
    DELETE FROM [dbo].[DeliveryDirections];
    DELETE FROM [dbo].[Individuals];
    DELETE FROM [dbo].[OrganizationalUnits];
    DELETE FROM [dbo].[Partners];
    DELETE FROM [dbo].[UnitsOfMeasure];
    DELETE FROM [dbo].[Warehouses];

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
