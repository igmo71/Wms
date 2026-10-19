/*
    Очистка прикладных данных WMS для текущей схемы базы данных.
    Включает справочники и топологию склада.
    Учитывает источники и циклы приемки, LPN, движения и журнал аудита.
    Запускать при остановленных WebApp, WebApi и обработчиках интеграции.

    Не очищаются:
      - таблицы ASP.NET Core Identity (dbo.AspNet*); предпочтение склада
        у пользователей сбрасывается перед удалением складов;
      - dbo.__EFMigrationsHistory;
      - последовательность LicensePlateNumberSequence (старые коды не переиспользуются).

    Распечатанные до очистки этикетки LPN больше не действуют.

    Порядок DELETE соответствует внешним ключам текущей EF Core модели:
    сначала зависимые таблицы, затем родительские.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Аудит ссылается на команды, приемки, LPN, источники и номенклатуру.
    DELETE FROM [dbo].[WarehouseAuditEvents];
    DELETE FROM [dbo].[CommandReceipts];

    -- История размещения ссылается на LPN, его строки и складские движения.
    DELETE FROM [dbo].[LicensePlateNumberPutaways];
    DELETE FROM [dbo].[InventoryTurnovers];
    DELETE FROM [dbo].[InventoryMovements];
    DELETE FROM [dbo].[InventoryBalances];

    DELETE FROM [dbo].[LicensePlateNumberItems];
    DELETE FROM [dbo].[LicensePlateNumbers];
    DELETE FROM [dbo].[LicensePlateNumberBatches];

    -- Все блокировки удаляются вместе с топологией склада.
    DELETE FROM [dbo].[StorageLocationLocks];

    -- Дочерние записи документов и снимки источников удаляются первыми.
    DELETE FROM [dbo].[InventoryCountItems];
    DELETE FROM [dbo].[ReceivingOrderItems];
    DELETE FROM [dbo].[ReceivingOrderParticipants];
    DELETE FROM [dbo].[ReceivingCycleSourceVersions];
    DELETE FROM [dbo].[ReceivingOrderSourceLinks];
    DELETE FROM [dbo].[ReceivingSourceLineProjections];
    DELETE FROM [dbo].[ShippingOrderBaseItems];
    DELETE FROM [dbo].[ShippingOrderItems];

    -- Циклы, заказы и версии источников зависят от складов и справочников.
    DELETE FROM [dbo].[InventoryCounts];
    DELETE FROM [dbo].[InventoryTransfers];
    DELETE FROM [dbo].[ReceivingCycles];
    DELETE FROM [dbo].[ReceivingOrders];
    DELETE FROM [dbo].[ReceivingSourceVersions];
    DELETE FROM [dbo].[ReceivingSourceDocuments];
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

    -- Identity сохраняется, но пользовательское предпочтение удаляемого склада
    -- должно быть сброшено из-за внешнего ключа на Warehouses.
    UPDATE [dbo].[AspNetUsers]
    SET [DefaultWarehouseId] = NULL
    WHERE [DefaultWarehouseId] IS NOT NULL;

    DELETE FROM [dbo].[Warehouses];

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
