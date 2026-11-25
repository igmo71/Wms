# Задачи по закреплению остатка

**Документы** [spec.md](spec.md) · [plan.md](plan.md) · [quickstart.md](quickstart.md)

## Схема и доступность

- [x] T001 Добавить модель закрепления в `Wms.Domain/PickingRouteReservation.cs`, EF-конфигурацию в `Wms.Infrastructure/Data/Configurations/` и чтение группы через `Wms.Infrastructure/Data/StockAvailabilityStore.cs`. Учесть оба вида резерва в проводке, инвентаризации, удержании ячейки и подборе источника.
- [x] T002 Создать миграцию в `Wms.Infrastructure/Data/Migrations/` по команде из quickstart. Разработчик запускает команду, агент проверяет миграцию и снимок модели.

## Сценарий 1. Выдать остаток при старте

Проверка — две готовые сборки с потребностью 8 + 8 получают 8 и 2 из остатка 10 в порядке приоритета.

- [x] T003 [US1] Вынести общий приоритет РО из `Wms.Application/Replenishment/GetReplenishmentTasks.cs`. В `Wms.Application/Picking/StartPickingAssembly.cs` завершать выдачу с блокировкой склада, квитанцией и переходом в `InProgress`. Добавить запрос готовых сборок в `Wms.Infrastructure/Data/PickingAssemblyStore.cs`.
- [x] T004 [US1] Проверить конкурирующий старт и повторную выдачу после неопределённого ответа в `Wms.Tests/Picking/PickingAssemblySafetyTests.cs`.

## Сценарий 2. Использовать закрепление в отборе

Проверка — оба экрана показывают одинаковую доступность, смена адреса освобождает остаток, а частичная подпитка добавляет доступное количество.

- [x] T005 [US2] Строить маршрут и доступность SKU из одних закреплений в `Wms.Application/Picking/GetPickingWork.cs` и `Wms.Application/Replenishment/GetPickingReplenishments.cs`. Заказ подпитки в `Wms.Application/Replenishment/OrderReplenishment.cs` использует тот же непокрытый план.
- [x] T006 [US2] Обновлять факт и закрепления одной командой в `Wms.Application/Picking/ConfirmPick.cs` и `Wms.Application/Picking/ReportPickingShortage.cs`. Сохранить ручной выбор источника и оперировать всей затронутой группой при недостаче.
- [x] T007 [US2] Зачислять подходящую часть подпитки одновременно с проводкой в `Wms.Application/Replenishment/ConfirmReplenishmentTransfer.cs`. Проверить атомарность подпитки, повтор команды и свободный излишек в `Wms.Tests/Replenishment/ReplenishmentPostingSafetyTests.cs`. Проверки смены адреса и общей недостачи находятся в `Wms.Tests/Picking/PickingRouteReservationTests.cs`.

## Сценарий 3. Освободить остаток

Проверка — принятая отмена освобождает неотобранное количество до завершения обмена или подпитки.

- [x] T008 [US3] Обновлять закрепление при изменении плана, возврате, закрытии и принятии отмены в `Wms.Application/Picking/ChangePickingPlan.cs`, `Wms.Application/Picking/ReducePick.cs`, `Wms.Application/Picking/ClosePickingAssembly.cs` и `Wms.Application/Picking/CancelPickingAssembly.cs`. Проверки остаются в тестах закреплений, возврата, закрытия и жизненного цикла подпитки.

## Документация и проверка

- [x] T009 Описать реализованные правила в `.specify/memory/current/inventory.md`, отображение на ТСД в `.specify/memory/current/clients.md` и техническую структуру в `.specify/memory/architecture.md`. Разработчик выполняет проверку по `specs/019-picking-route-reservations/quickstart.md`.

## Порядок

T001 предшествует миграции и складским сценариям. Сценарий 1 создаёт исходное закрепление для сценариев 2 и 3. Чтения ТСД в T005 и команды жизненного цикла в T008 можно подготовить независимо после T003. Выдачу включать в работу вместе с изменениями чтения, использования и освобождения остатка.
