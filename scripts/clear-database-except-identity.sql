/*
    Очистка прикладных данных WMS для текущей схемы базы данных.
    Включает справочники и топологию склада.
    Учитывает источники и циклы приемки, сборки, подпитку, LPN, движения, журнал аудита,
    квитанции команд и запись разовой загрузки топологии и начальных остатков.
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

    -- Аудит ссылается на команды, приемки, сборки, LPN, источники, номенклатуру
    -- и запись разовой загрузки. Полная очистка удаляет также их историю.
    DELETE FROM [dbo].[WarehouseAuditEvents];
    DELETE FROM [dbo].[CommandReceipts];

    -- Операции Отбора и Подпитки ссылаются друг на друга, на документы 1С,
    -- ячейки и движения. Записи с FK на движения удаляются до движений.
    DELETE FROM [dbo].[ReplenishmentTransfers];
    DELETE FROM [dbo].[StockShortages];
    DELETE FROM [dbo].[PickingReservations];
    DELETE FROM [dbo].[ReplenishmentSourceCycles];
    DELETE FROM [dbo].[ReplenishmentTasks];
    DELETE FROM [dbo].[PickingEntries];
    DELETE FROM [dbo].[PickingClosures];
    DELETE FROM [dbo].[PickingAllocations];
    DELETE FROM [dbo].[ShippingExchangeTargets];
    DELETE FROM [dbo].[PickingAssemblyPlanLines];
    DELETE FROM [dbo].[PickingAssemblySourceLinks];
    DELETE FROM [dbo].[PickingAssemblies];

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
    -- Запись разовой загрузки блокирует повторный импорт в тот же склад.
    -- Связанные события аудита, квитанции и движения уже удалены выше.
    DELETE FROM [dbo].[WarehouseOpeningImports];
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
