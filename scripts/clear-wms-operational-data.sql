/*
    Очистка только операционных данных WMS:
      - приемки, отгрузки, инвентаризации и их строки;
      - перемещения, движения, остатки и обороты;
      - квитанции команд, выпущенные LPN и блокировки от инвентаризаций.

    Сохраняются Identity (dbo.AspNet*), dbo.__EFMigrationsHistory,
    все справочники, штрихкоды, склады, зоны, места хранения и ручные блокировки.
    Схема: AddLicensePlateNumbers.
    Последовательность LicensePlateNumberSequence не сбрасывается: старые коды
    не выдаются повторно. Распечатанные до очистки этикетки больше не действуют.
    Запускать при остановленных WebApp, WebApi и обработчиках интеграции.
    Порядок DELETE соответствует внешним ключам текущей EF Core модели.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Старые результаты команд не должны ссылаться на удаленные операции.
    DELETE FROM [dbo].[CommandReceipts];

    DELETE FROM [dbo].[LicensePlateNumbers];
    DELETE FROM [dbo].[LicensePlateNumberBatches];

    -- StorageLocationLockOwnerType.InventoryCount = 1.
    -- Ручные блокировки (OwnerType = 0) сохраняются.
    DELETE FROM [dbo].[StorageLocationLocks]
    WHERE [OwnerType] = 1;

    -- Обороты ссылаются на движения.
    DELETE FROM [dbo].[InventoryTurnovers];
    DELETE FROM [dbo].[InventoryMovements];
    DELETE FROM [dbo].[InventoryBalances];

    -- Строки документов удаляются раньше их заголовков.
    DELETE FROM [dbo].[InventoryCountItems];
    DELETE FROM [dbo].[ReceivingOrderItems];
    DELETE FROM [dbo].[ShippingOrderBaseItems];
    DELETE FROM [dbo].[ShippingOrderItems];

    DELETE FROM [dbo].[InventoryCounts];
    DELETE FROM [dbo].[InventoryTransfers];
    DELETE FROM [dbo].[ReceivingOrders];
    DELETE FROM [dbo].[ShippingOrders];

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
