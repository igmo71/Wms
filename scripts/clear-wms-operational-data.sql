/*
    Очистка только операционных данных WMS:
      - приемки, их источники и циклы, сборки, подпитка, отгрузки,
        инвентаризации и их строки;
      - перемещения, движения, остатки и обороты;
      - операционный аудит, квитанции команд, выпущенные LPN
        и блокировки от инвентаризаций, Отбора и Подпитки.

    Сохраняются Identity (dbo.AspNet*), dbo.__EFMigrationsHistory,
    все справочники, штрихкоды, склады, зоны, места хранения, ручные блокировки,
    запись загрузки топологии, ее аудит и квитанции, а также историю изменений
    справочника SKU. Этап начальных остатков возвращается в ожидание загрузки.
    Флаги SKU, включенные при прежней загрузке остатков, остаются в справочнике.
    Учитывает состав LPN, историю размещения, участников приемки,
    резервы и недостачи Отбора и Подпитки.
    Последовательность LicensePlateNumberSequence не сбрасывается: старые коды
    не выдаются повторно. Распечатанные до очистки этикетки больше не действуют.
    Запускать при остановленных WebApp, WebApi и обработчиках интеграции.
    Порядок DELETE соответствует внешним ключам текущей EF Core модели.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Сохраняем только аудит топологии и изменений справочника SKU.
    -- Остальной аудит может ссылаться на удаляемые сборки, приемки и команды.
    DELETE FROM [dbo].[WarehouseAuditEvents]
    WHERE [CommandType] IS NULL OR [CommandType] NOT IN (
        'sku.expiration-tracking.set',
        'sku.barcode.remove-local',
        'warehouse.opening.topology.import'
    );

    -- Квитанции сохраненных изменений и топологии нужны для безопасного
    -- повтора команд. Их аудит остается выше и ссылается на квитанции по FK.
    DELETE FROM [dbo].[CommandReceipts]
    WHERE [CommandType] NOT IN (
        'sku.expiration-tracking.set',
        'sku.barcode.remove-local',
        'warehouse.opening.topology.import'
    );

    -- Связанные с движениями записи удаляются до самих движений.
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

    -- InventoryCount = 1, PickingAssembly = 2, ReplenishmentTask = 3.
    -- Ручные блокировки (OwnerType = 0) сохраняются.
    DELETE FROM [dbo].[StorageLocationLocks]
    WHERE [OwnerType] IN (1, 2, 3);

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

    -- Протокол топологии остается; протокол остатков больше не должен описывать
    -- удаленные движения. Повторная загрузка возможна после полного сброса работы.
    UPDATE [dbo].[WarehouseOpeningImports]
    SET [Status] = 1,
        [DeclaredSnapshotAt] = NULL,
        [StockImportedAtUtc] = NULL,
        [StockImportedBy] = NULL,
        [StockSummary] = NULL,
        [Revision] = [Revision] + 1
    WHERE [Status] = 2;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
