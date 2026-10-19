/*
    Очистка только операционных данных WMS:
      - приемки, их источники и циклы, отгрузки, инвентаризации и их строки;
      - перемещения, движения, остатки и обороты;
      - операционный аудит, квитанции команд, выпущенные LPN
        и блокировки от инвентаризаций.

    Сохраняются Identity (dbo.AspNet*), dbo.__EFMigrationsHistory,
    все справочники, штрихкоды, склады, зоны, места хранения, ручные блокировки,
    а также историю и квитанции изменения признака срока годности SKU.
    Учитывает состав LPN, историю размещения и участников приемки.
    Последовательность LicensePlateNumberSequence не сбрасывается: старые коды
    не выдаются повторно. Распечатанные до очистки этикетки больше не действуют.
    Запускать при остановленных WebApp, WebApi и обработчиках интеграции.
    Порядок DELETE соответствует внешним ключам текущей EF Core модели.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Операционный аудит ссылается на удаляемые приемки, LPN и источники.
    -- История настройки срока годности SKU остается со справочником.
    DELETE FROM [dbo].[WarehouseAuditEvents]
    WHERE [ReceivingOrderId] IS NOT NULL
       OR [LicensePlateNumberId] IS NOT NULL
       OR [ReceivingSourceDocumentId] IS NOT NULL;

    -- Квитанции сохраненной настройки SKU нужны для безопасного повтора команды.
    DELETE FROM [dbo].[CommandReceipts]
    WHERE [CommandType] <> 'sku.expiration-tracking.set';

    -- История размещения ссылается на LPN, его строки и складские движения.
    DELETE FROM [dbo].[LicensePlateNumberPutaways];
    DELETE FROM [dbo].[InventoryTurnovers];
    DELETE FROM [dbo].[InventoryMovements];
    DELETE FROM [dbo].[InventoryBalances];

    DELETE FROM [dbo].[LicensePlateNumberItems];
    DELETE FROM [dbo].[LicensePlateNumbers];
    DELETE FROM [dbo].[LicensePlateNumberBatches];

    -- StorageLocationLockOwnerType.InventoryCount = 1.
    -- Ручные блокировки (OwnerType = 0) сохраняются.
    DELETE FROM [dbo].[StorageLocationLocks]
    WHERE [OwnerType] = 1;

    -- Дочерние записи документов и снимки источников удаляются первыми.
    DELETE FROM [dbo].[InventoryCountItems];
    DELETE FROM [dbo].[ReceivingOrderItems];
    DELETE FROM [dbo].[ReceivingOrderParticipants];
    DELETE FROM [dbo].[ReceivingCycleSourceVersions];
    DELETE FROM [dbo].[ReceivingOrderSourceLinks];
    DELETE FROM [dbo].[ReceivingSourceLineProjections];
    DELETE FROM [dbo].[ShippingOrderBaseItems];
    DELETE FROM [dbo].[ShippingOrderItems];

    DELETE FROM [dbo].[InventoryCounts];
    DELETE FROM [dbo].[InventoryTransfers];
    DELETE FROM [dbo].[ReceivingCycles];
    DELETE FROM [dbo].[ReceivingOrders];
    DELETE FROM [dbo].[ReceivingSourceVersions];
    DELETE FROM [dbo].[ReceivingSourceDocuments];
    DELETE FROM [dbo].[ShippingOrders];

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
