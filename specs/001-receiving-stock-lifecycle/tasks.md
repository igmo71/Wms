# Задачи: приемка и размещение при изменяемом плане 1С

**Основание**: [spec.md](spec.md), [plan.md](plan.md), [модель](data-model.md),
[контракты](contracts/operations.md), [сценарии проверки](quickstart.md).

**Порядок**: каждый пункт — один проверяемый инкремент. Старые приемка и
размещение могут не работать между инкрементами; к завершению остается один
действующий путь проведения каждого события. Разработчик запускает сборки,
тесты, приложение и команды создания/применения EF-миграций по `AGENTS.md`.
Агент дает точные команды после готовности модели и проверяет созданную
миграцию и снимок до ее применения. UI, устройство и живую 1С проверяет
разработчик по [quickstart.md](quickstart.md).

## Формат

`[P]` означает возможность параллельной работы после выполнения названных
предшественников. `[USn]` связывает пункт с пользовательским сценарием.
Пути в пунктах указаны относительно корня репозитория. Новые имена файлов
служат местом реализации, а существующие файлы сохраняют свои текущие имена.

## Phase 1: Setup

**Цель**: подключить новые проекты к точкам композиции без зависимости новых
сборок от легаси `Wms`.

- [x] T001 Подключить `Wms.Application`, `Wms.Infrastructure` и `Wms.Integration` к новым сценариям в `Wms.WebApi/Wms.WebApi.csproj`, `Wms.WebApp/Wms.WebApp.csproj`, `Wms.WebApi/Program.cs` и `Wms.WebApp/Program.cs`; сохранить направление зависимостей из `plan.md`.

## Phase 2: Foundation

**Цель**: общая схема и единый механизм проведения до реализации пользовательских сценариев.

- [x] T002 Описать источники 1С, неизменяемые версии, проекции строк, связи с ордером, собственный ID ордера, уникальную строку `(ордер, SKU)`, циклы и участников в новых файлах `Wms.Domain/ReceivingSourceDocument.cs`, `Wms.Domain/ReceivingSourceVersion.cs`, `Wms.Domain/ReceivingOrderSourceLink.cs`, `Wms.Domain/ReceivingOrder.cs`, `Wms.Domain/ReceivingOrderItem.cs`, `Wms.Domain/ReceivingCycle.cs` и `Wms.Infrastructure/Data/ApplicationDbContext.cs`; `Ref_Key` непустой и уникальный, одновременно действующая связь источника только одна, `decimal?` плана различает неизвестное значение и известный `0`, `decimal?` факта различает непроверенное значение и подтвержденный `0`.
- [x] T003 Описать приемную LPN, единственную активную позицию `(SKU, DateOnly?, decimal(15,3))`, отмену открытой LPN, неизменяемое происхождение размещений и ревизии в `Wms.Domain/LicensePlateNumber.cs`, `Wms.Domain/LicensePlateNumberItem.cs`, `Wms.Domain/LicensePlateNumberPutaway.cs`, `Wms.Infrastructure/Data/Configurations/LicensePlateNumberItemConfiguration.cs` и `Wms.Infrastructure/Data/ApplicationDbContext.cs`; ограничение единственной позиции относится только к приемной LPN.
- [x] T004 Добавить локальный флаг SKU, `DateOnly?` как SQL `date` в движение, оборот и остаток, подписанный остаток и ссылку обратного движения на исходное в `Wms.Domain/StockKeepingUnit.cs`, `Wms.Domain/InventoryMovement.cs`, `Wms.Domain/InventoryTurnover.cs`, `Wms.Domain/InventoryBalance.cs` и их конфигурациях `Wms.Infrastructure/Data/Configurations/`; уникальность `(склад, ячейка, SKU, дата)` должна работать и при дате `null`, исходное движение обращается не более одного раза.
- [x] T005 Ввести компактное неизменяемое событие аудита и квитанцию идемпотентной команды в `Wms.Domain/WarehouseAuditEvent.cs`, `Wms.Infrastructure/Data/Configurations/WarehouseAuditEventConfiguration.cs`, `Wms.Infrastructure/Data/CommandReceipt.cs` и `Wms.Infrastructure/Data/ApplicationDbContext.cs`; хранить исполнителя/источник, время, IDs, значения до/после и ссылки на команду и движения без копирования полного снимка 1С.
- [x] T006 После T002–T005 сформулировать разработчику точную штатную команду EF для миграции в `Wms.Infrastructure/Data/Migrations/`, получить созданные файлы и проверить миграцию и `ApplicationDbContextModelSnapshot.cs`: сохранность данных, уникальность `null`-даты и действующей связи источника, ограничение повторного обращения, типы и индексы; только затем дать команду ее применения.
- [x] T007 Добавить минимальную проверку риска повторной команды и раздельного коммита складских данных в `Wms.Tests/Infrastructure/CommandExecutorTests.cs`, используя существующую инфраструктуру SQL-тестов; проверка должна выявлять дублирование движений либо отсутствие согласованной квитанции/аудита.
- [x] T008 Перенести применимый механизм исполнения команды из `Wms/Application/Commands/CommandExecutor.cs` в `Wms.Application/Commands/CommandExecutor.cs` с реализацией хранения в `Wms.Infrastructure/Data/CommandExecutionStore.cs`; результат, квитанция и событие аудита сохраняются атомарно, повтор `request ID` не создает второго действия, Application не ссылается на `Wms`.
- [x] T009 Реализовать единое проведение новой приемки/размещения в `Wms.Application/Inventory/PostInventoryMovement.cs` и `Wms.Infrastructure/Data/InventoryPostingStore.cs`: положительная величина с направлением, дата и LPN в происхождении, баланс по четырем измерениям без LPN, обороты до/после, допустимый отрицательный итог обратного движения и транзакционное сохранение.
- [x] T010 Проверять явную роль `Manager` в исполнении менеджерских команд и ручной синхронизации импортируемых справочников через `Wms.Application/Authorization/WarehousePermission.cs` и `Wms.Infrastructure/Data/DependencyInjection.cs`; разделить просмотр/синхронизацию от конфигурации в Web-страницах справочников, включая `Wms.WebApp/Components/Pages/StockKeepingUnitPages/Index.razor` и `Wms.WebApp/Components/Pages/SkuBarcodePages/Index.razor`, чтобы менеджер мог запускать синхронизацию без `Administrator`, а одна роль `Administrator` не давала менеджерских полномочий.

**Граница**: фундамент готов для одного нового пути проведения. T006 —
обязательный контроль созданной разработчиком миграции; команды не запускаются агентом.

## Phase 3: US1 — приемка нескольких документов и размещение (P1, MVP)

**Цель**: один WMS-ордер из нескольких источников, приход при закрытии
монопалеты и размещение до завершения ордера.

**Независимая проверка**: два документа с общим SKU дают один суммарный план;
закрытие первой положительной LPN создает один приход, повтор команды его не
дублирует; часть LPN размещается при незавершенном ордере.

- [x] T011 [US1] Добавить адресную SQL-проверку риска удвоения вклада документа и потери корректных строк при ошибке другой строки в `Wms.Tests/Receiving/ReceivingSynchronizationTests.cs`: повтор `Ref_Key`, повторяющиеся SKU, неизвестный SKU и неподдерживаемое количество различают числовой и неизвестный план.
- [x] T012 [US1] Перенести чтение приходного документа из `Wms/Integration/OneS/Services/Document_ПриходныйОрдерНаТовары_InboundService.cs` за контракт `Wms.Application/Receiving/IReceivingSourceReader.cs` в `Wms.Integration/OneS/ReceivingSourceReader.cs`; сохранить исходный ответ и принимать уведомление `ПриЗаписи` как повод читать текущее состояние, без записи в 1С.
- [ ] T013 [US1] Реализовать сохранение последней успешной версии источника, строк без склейки, времени сверки и отдельной ошибки попытки в `Wms.Application/Receiving/SynchronizeReceivingSource.cs` и `Wms.Infrastructure/Data/ReceivingSourceStore.cs`: сбой/нечитаемый ответ не заменяет успешную версию; версия добавляется только при изменении содержимого; корректные строки участвуют в плане даже при проблемной строке другого SKU.
- [ ] T014 [US1] Реализовать просмотр непривязанных источников, создание ордера из нескольких `Ref_Key`, начальную привязку и суммарный план в `Wms.Application/Receiving/CreateReceivingOrder.cs`, `Wms.Application/Receiving/GetReceivingSources.cs` и `Wms.Infrastructure/Data/ReceivingOrderStore.cs`; склад совпадает при привязке, источник занят не более чем одним ордером, повтор не удваивает вклад, строки ордера не исчезают.
- [ ] T015 [US1] Добавить команду выбора SKU из состава или всего синхронизированного справочника с идемпотентным добавлением строки ордера в `Wms.Application/Receiving/AddReceivingOrderSku.cs` и `Wms.Application/Receiving/SearchReceivingSku.cs`: вне источников план `0`, факт непроверен, сканирование ищет глобально, отсутствие штрихкода не мешает ручному поиску.
- [ ] T016 [US1] Реализовать новый цикл, вступление участников, место приемки, открытие/изменение/отмену собственной LPN и действие «Проверено: 0» в `Wms.Application/Receiving/JoinReceivingCycle.cs`, `Wms.Application/Receiving/EditReceivingLpn.cs`, `Wms.Application/Receiving/CancelReceivingLpn.cs` и `Wms.Application/Receiving/ConfirmZeroReceived.cs`; пустая LPN не закрывается, отмена не проводит товар, нулевая проверка не создает движения и недоступна при действующей LPN SKU.
- [ ] T017 [US1] Добавить SQL-проверку риска двойного прихода и конфликта изменившегося плана/флага SKU в `Wms.Tests/Receiving/ReceivingSafetyTests.cs`: повтор закрытия и встречное изменение ревизии дают один согласованный набор движения, остатка, оборота, аудита и квитанции либо конфликт.
- [ ] T018 [US1] Реализовать `Wms.Application/Receiving/CloseReceivingLpn.cs`: перед закрытием попытаться сверить все источники, при сбое показать последнее число и его неподтвержденность; закрыть только положительную монопалету; оператору разрешить суммарный факт не выше известного плана, превышение/неизвестный план — только `Manager`, причина необязательна; сохранить использованный план, версии источников, закрывшего, предупреждение о прошедшей дате и единственный приход атомарно. Убрать старое проведение при завершении в `Wms/Application/ReceivingOrders/ReceivingOrderCommandService.cs` до открытия новых точек входа.
- [ ] T019 [US1] Реализовать частичное размещение закрытой LPN при открытом ордере в `Wms.Application/Receiving/PutAwayReceivingLpn.cs`: брать SKU и дату из LPN, сохранять ее ссылку на каждом собственном движении, не размещать больше действующего доступного количества, конфликтовать при встречной поправке/удалении LPN.
- [ ] T020 [P] [US1] Подготовить новые транспортные контракты и обработчики приемки и размещения в `Wms.Contracts/Mobile/V1/MobileReceivingOrderContracts.cs`, `Wms.WebApi/Mobile/MobileReceivingOrderEndpoints.cs` и `Wms.WebApi/Mobile/MobileLicensePlateNumberPutawayEndpoints.cs` с серверной проверкой прав; временно отключить регистрацию старых endpoint-групп в `Wms.WebApi/Program.cs`, новые включить в T022. Зависит от T014–T019.
- [ ] T021 [P] [US1] Заменить Web-страницы создания ордера из нескольких источников, списка/деталей, добавления SKU и исключительного закрытия LPN менеджером в `Wms.WebApp/Components/Pages/ReceivingOrderPages/Index.razor`, `Wms.WebApp/Components/Pages/ReceivingOrderPages/Details.razor` и их `.razor.cs`; показать текущий план, источники и факт, удалить с этих страниц вызовы старого проведения. Зависит от T014–T019.
- [ ] T022 [US1] Заменить Mobile-приемку в `Wms.Mobile/Services/ReceivingOrderReceivingProcess.cs`, `Wms.Mobile/Services/MobileReceivingOrderClient.cs`, `Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderReceivingPage.xaml` и `Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderLicensePlateNumberQuantityPage.xaml`: поиск по ордеру/справочнику, глобальный сканер, нулевая проверка, отмена открытой LPN и размещение до завершения ордера. Подключить новые маршруты T020 в `Wms.WebApi/Program.cs` и новые зависимости T021 в `Wms.WebApp/Program.cs`; подтвердить, что старое проведение уже недоступно и каждый приход/размещение имеет один вызываемый обработчик.

**Граница MVP**: T001–T022. Следующие истории дополняют этот путь датой,
корректировками и изменениями плана; старый путь удаляется в завершающей фазе.

## Phase 4: US2 — срок годности в остатках (P1)

**Цель**: менеджер включает локальный флаг, Mobile требует дату при приемке,
остатки одной ячейки по разным датам видны отдельно.

**Независимая проверка**: два срока одного SKU и `null` в одной ячейке дают
три группы; выключение флага не меняет уже проведенные даты и группы.

- [ ] T023 [US2] Добавить SQL-проверку риска слияния датированных групп и дублирования группы с датой `null` в `Wms.Tests/Receiving/ReceivingSafetyTests.cs`; две LPN одного SKU размещаются в одну ячейку с разными датами, неизвестная дата имеет единственный ключ остатка.
- [ ] T024 [US2] Реализовать изменение местного флага срока только менеджером и его сохранение при импорте SKU в `Wms.Application/StockKeepingUnits/SetExpirationTracking.cs`, `Wms.Integration/OneS/StockKeepingUnitReader.cs` и `Wms.WebApp/Components/Pages/StockKeepingUnitPages/Index.razor`; показывать историю переключений, не менять даты старых движений и остатков.
- [ ] T025 [US2] Реализовать дату в закрытии и изменении открытой LPN в `Wms.Application/Receiving/CloseReceivingLpn.cs`, `Wms.Application/Receiving/EditReceivingLpn.cs` и `Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderLicensePlateNumberQuantityPage.xaml`: при актуальном включенном флаге сервер требует дату положительного количества, сохраненная дата видна при выключенном флаге, истекшая дата требует явного подтверждения, равная сегодняшней — нет.
- [ ] T026 [P] [US2] Показать дату либо «Не указан» вместо состояния позиции на вкладке «Палеты» в `Wms.WebApp/Components/Pages/ReceivingOrderPages/ReceivingOrderContents.razor` и группы остатка по дате в `Wms.WebApp/Components/Pages/InventoryBalancePages/Index.razor`; статус удаления остается у самой LPN. Зависит от T023–T025.
- [ ] T027 [P] [US2] Добавить отдельный отчет SKU с включенным флагом и ненулевым остатком без даты по складам и ячейкам, включая возможный минус, в `Wms.Application/Reports/GetUndatedExpirationStock.cs` и `Wms.WebApp/Components/Pages/ReportPages/UndatedExpirationStock/Index.razor`. Зависит от T023–T025.

## Phase 5: US3 — исправление проведенной приемки (P1)

**Цель**: менеджер меняет количество только еще не размещенной LPN либо
удаляет закрытую LPN обращением ее собственных движений.

**Независимая проверка**: поправка до размещения меняет приемочный факт;
после первого размещения отклоняется; удаление размещенной LPN создает
однократные обратные движения по исходным адресам и допускает минус.

- [ ] T028 [US3] Добавить SQL-проверку риска повторного обращения, неверного адреса и частичного коммита в `Wms.Tests/Receiving/ReceivingSafetyTests.cs`: приемка, поправка и два размещения обращаются один раз по исходным адресам/датам; встречное размещение или изменение остатков после предпросмотра дает конфликт, разрешенный минус сохраняется.
- [ ] T029 [US3] Реализовать `Wms.Application/Receiving/CorrectReceivingLpnQuantity.cs`: только `Manager`, закрытая LPN без единого проведенного размещения, новое количество неотрицательное, SKU/дата неизменны; разница в исходном месте приемки добавляет движение, оборот и аудит и обновляет действующий факт.
- [ ] T030 [US3] Реализовать предпросмотр и подтверждение удаления в `Wms.Application/Receiving/PreviewReceivingLpnDeletion.cs` и `Wms.Application/Receiving/DeleteReceivingLpn.cs`: обратить каждый собственный приход, поправку и размещение по историческим адресам, показать прогноз и явно подтвердить минусы; при изменении LPN/движений/затронутых остатков вернуть конфликт, повтор не создает новых движений, чужие операции остаются в силе; факт строки вернется к `0` только после ранее подтвержденной нулевой проверки, иначе станет непроверенным.
- [ ] T031 [US3] Отобразить поправку количества, предпросмотр обратных движений, возможные минусы и историю удаления для менеджера в `Wms.WebApp/Components/Pages/ReceivingOrderPages/ReceivingOrderContents.razor` и `Wms.WebApp/Components/Pages/ReceivingOrderPages/Details.razor.cs`; исключить исправление после первого размещения.

## Phase 6: US4 — изменяемый план и завершение работы (P1)

**Цель**: текущий план 1С изменяется независимо от проведенного факта,
завершение фиксирует снимок решения и допускает следующий цикл.

**Независимая проверка**: завершенный без расхождения ордер после изменения
одного источника сохраняет статус и факт, получает новое расхождение;
старый снимок содержит прежние план/факт/версии источников.

- [ ] T032 [US4] Добавить SQL-проверку риска перезаписи факта и решения старого цикла в `Wms.Tests/Receiving/ReceivingSynchronizationTests.cs`: снятие проведения/пометка удаления/исчезновение строки обнуляют только вклад этого источника, сбой сверки сохраняет прежний план с отметкой ошибки, повтор/запаздывающее уведомление не удваивает вклад или приход.
- [ ] T033 [US4] Развить проекцию плана в `Wms.Application/Receiving/SynchronizeReceivingSource.cs` и `Wms.Infrastructure/Data/ReceivingSourceStore.cs`: изменение, восстановление, снятие проведения, пометка удаления и несовпадение склада обновляют вклад источника; старые SKU остаются с нулем, новые добавляются, ошибки количества/переполнения дают неизвестный план лишь затронутого SKU, пересборка после обновления справочника использует сохраненный снимок.
- [ ] T034 [US4] Реализовать добавление/открепление источника и ручную сверку всех источников ордера в `Wms.Application/Receiving/ChangeReceivingOrderSources.cs` и `Wms.Application/Receiving/ResynchronizeReceivingOrder.cs`: до первой проведенной LPN оператор может открепить, позднее и после завершения — только `Manager`; план меняется атомарно, факт и статус нет, отсутствие источников и ошибка свежей сверки видны явно.
- [ ] T035 [US4] Реализовать завершение ордера без движения и менеджерское повторное открытие в `Wms.Application/Receiving/CompleteReceivingOrder.cs` и `Wms.Application/Receiving/ReopenReceivingOrder.cs`: запрет при открытой LPN, отдельный неизменяемый снимок каждого цикла с планом/фактом/версиями источников/сбоем сверки; новое место приемки и новые участники действуют только в следующем цикле.
- [ ] T036 [US4] Показать актуальное расхождение отдельно от снимков завершений, причины неизвестного плана и несверенных источников, историю циклов и менеджерскую повторную сверку в `Wms.WebApp/Components/Pages/ReceivingOrderPages/ReceivingOrderDiscrepancies.razor`, `Wms.WebApp/Components/Pages/ReceivingOrderPages/ReceivingOrderDetailsView.razor` и `Wms.WebApp/Components/Pages/ReceivingOrderPages/Index.razor`; выделять завершенные ордера с новым расхождением.

## Final Phase: завершение замены

- [ ] T037 Удалить оставшийся невызваемый код прежнего проведения при завершении ордера и возобновления закрытой LPN из `Wms/Application/ReceivingOrders/ReceivingOrderCommandService.cs`, `Wms/Application/ReceivingOrders/ReceivingCommands.cs`, `Wms/Application/LicensePlateNumbers/LicensePlateNumberService.cs` и `Wms/Application/LicensePlateNumbers/LicensePlateNumberPutawayService.cs`; удалить устаревшие элементы из `Wms.Contracts/Mobile/V1/MobileReceivingOrderContracts.cs` и `Wms.WebApp/Components/Pages/ReceivingOrderPages/ReceivingInProgressView.razor`, сохраняя методы, которые еще нужны другим устаревшим процессам.
- [ ] T038 Сверить итоговый код с `spec.md` и `quickstart.md`, актуализировать только реализованные правила в `.specify/memory/current/receiving.md`, `.specify/memory/current/inventory.md`, `.specify/memory/current/integration.md` и `.specify/memory/architecture.md`, а оставшиеся зависимости отбора, отгрузки, перемещений и инвентаризации оставить в `.specify/memory/backlog.md`; не объявлять готовым промышленное использование старых процессов.
- [ ] T039 Передать разработчику минимальные команды адресных тестов и сборки для `Wms.Tests/Wms.Tests.csproj` и `Wms.slnx`, инструкции ручной проверки Mobile/Web/1С из `specs/001-receiving-stock-lifecycle/quickstart.md`; проверить сообщенные результаты и исправить только выявленные дефекты.

## Dependencies

```text
T001 → T002–T005 → T006 → T007–T010 → US1 → US2 → US3 → US4 → Final
```

US2–US4 опираются на новую приемку US1. US3 использует учет даты из US2;
US4 проверяет уже проведенный факт и историю всех предшествующих действий.
T018 отключает прежнее проведение, T020 временно убирает старые Mobile
маршруты, T021 заменяет старые Web-страницы, T022 подключает новые Mobile
маршруты; T037 удаляет оставшийся недоступный легаси-код. Между этими
шагами старый сценарий может не работать. Внутри каждого сценария сначала идут проверки
существенного риска, затем реализация. Разработчик запускает эти проверки по
переданным командам; агент не запускает их самостоятельно.

## Parallel opportunities

- **US1**: T020 (Mobile API и контракты) и T021 (Web) выполняются параллельно после T014–T019; T022 зависит от T020.
- **US2**: T026 (представление остатков и палеты) и T027 (отчет) выполняются параллельно после T023–T025.
- **US3/US4**: операции изменения одного ордера/LPN и синхронизации источника делят схему и ревизии; их порядок сохраняется последовательным для проверки конфликтов.

## Implementation strategy

Сначала T001–T022 дают MVP: суммарный план, монопалета, одно проведение при
закрытии и размещение до завершения. Затем добавляются дата, менеджерские
обратные операции и изменяемый план со снимками циклов. Каждая завершенная
история проверяется по своему независимому критерию выше; старые процессы
отбора, отгрузки, перемещения и инвентаризации не входят в этот epic.
