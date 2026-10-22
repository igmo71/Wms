# Tasks: Навигация и карточки мобильной приёмки

**Input**: [spec.md](spec.md) и [plan.md](plan.md)

**Execution rule**: Один пункт ниже — один законченный инкремент с короткой передачей разработчику. Приступать после завершения предыдущей функции с экраном документов 1С. Сборку, запуск и проверку на устройстве выполняет разработчик.

## Phase 1: Разделы ордера — MVP

**Goal**: Три отдельных экрана существующего ордера, понятный текущий раздел, один шаг между разделами, правильный возврат и прежнее сканирование.

**Independent check**: Открыть ордер, десять раз переключить разделы, вернуться одним «Назад» к списку; проверить одинаковый стандартный переход, номер и общие переходы после первого скана документа, отсутствие «Готово» и подписи «Начать»/«Присоединиться»; присоединиться из каждого из трёх разделов с возвратом в исходный, сканировать LPN с экранов SKU и LPN; проверить повтор неподтверждённой команды.

- [x] T001 [US1] Переименовать Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderReceivingPage.xaml и Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderReceivingPage.xaml.cs в ReceivingOrderSkuPage, создать Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderLpnListPage.xaml и Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderLpnListPage.xaml.cs; связать экраны с Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderSourcesPage.xaml и Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderSourcesPage.xaml.cs и Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderPage.xaml.cs через переходы без накопления стека, убрать «Готово» у документов и показывать общую навигацию сразу после создания ордера, сохранив пустой режим до первого скана, создать общий экран присоединения Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderJoinPage.xaml и Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderJoinPage.xaml.cs с доступом из всех трёх разделов и возвратом в исходный, сохранить общий ReceivingOrderReceivingProcess, сканирование по US5 и возврат из дочерних страниц; обновить Wms.Mobile/MauiProgram.cs, .specify/memory/current/clients.md и .specify/memory/architecture.md.

## Phase 2: Карточки SKU ордера

**Goal**: Алфавитный список ордера с читаемым планом, фактом и состоянием.

**Independent check**: Проверить порядок названий и фон карточек для неизвестного факта, равенства, нуля при положительном плане, превышения и неизвестного плана в обеих темах.

- [x] T002 [US2] Обновить Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderSkuPage.xaml и Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderSkuPage.xaml.cs: убрать номера, сортировать по названию и вывести название слева, план и факт справа с согласованным цветом текста и фоном; при необходимости добавить цвета в Wms.Mobile/Resources/Styles/Colors.xaml; обновить .specify/memory/current/clients.md.

## Phase 3: Карточки подбора SKU

**Goal**: Поиск SKU с тем же порядком и компоновкой, но с цветом рамки вместо фона.

**Independent check**: Открыть подбор SKU в LPN; проверить порядок, правый блок количеств и рамки при неизвестном факте, равенстве, нуле, превышении и неизвестном плане.

- [x] T003 [US3] Обновить Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderLicensePlateNumberPage.xaml и Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderLicensePlateNumberPage.xaml.cs: сортировать кандидатов по названию, показывать доступные план и факт справа и окрашивать рамку по состоянию; привести карточки SKU внутри LPN к той же компоновке и цветам фона, вывести количество в LPN третьей строкой справа, убрать размещение и показывать срок только при требовании; для закрытого LPN скрыть подсказку о сканировании, а статус показать значком у номера с доступным текстовым описанием; использовать общие цвета из Wms.Mobile/Resources/Styles/Colors.xaml и обновить .specify/memory/current/clients.md.

## Phase 4: Короткие действия и карточки LPN

**Goal**: Вся карточка открывает LPN, доступные действия компактны и соответствуют состоянию.

**Independent check**: Открыть LPN тапом, проверить одну строку действий для открытого LPN, отсутствие переходов к размещению из приёмки и ясное подтверждение проводки.

- [x] T004 [US4] Убрать кнопку «Открыть» и сделать карточку LPN нажимаемой в Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderLpnListPage.xaml и Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderLpnListPage.xaml.cs; разместить «🔍 SKU», «Отменить», «Закрыть» в одной строке Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderLicensePlateNumberPage.xaml, сохранить условия доступности и подтверждение в Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderLicensePlateNumberPage.xaml.cs; обновить .specify/memory/current/clients.md.
  В этом же инкременте удалить все действия размещения из экранов приёмки;
  самостоятельный экран размещения сохранить.

## Phase 5: Единые значки состояний приёмки

**Goal**: Повторяющиеся состояния и обозначения объектов в мобильной приёмке читаются одинаково на списках и детальных экранах.

**Independent check**: Сравнить значки ордера, LPN, сверки SKU и документа на списках и деталях; проверить понятность на ТСД в обеих темах и доступные текстовые описания.

- [x] T005 Согласовать словарь значков состояний и часто повторяющихся обозначений ордера, LPN, SKU и документа 1С в мобильной приёмке; применить его к спискам и деталям там, где значок однозначен. Сохранить понятность действий, номера объектов и текстовые описания для специальных возможностей; не менять бизнес-значение статусов. Обновить .specify/memory/current/clients.md и оставить остальные мобильные процессы для их отдельных обновлений.

## Dependencies

T001 → T002 → T003 → T004 → T005. Все пункты зависят от завершённого экрана документов 1С предыдущей функции. Требования US5 о сканировании выполняются внутри T001, чтобы разделение экранов сразу сохраняло рабочий маршрут.

## Developer verification

После каждого пункта разработчик собирает и открывает мобильное приложение на поддерживаемом устройстве по обычному рабочему процессу и сообщает результат соответствующей independent check. Агент рассматривает результат и исправляет выявленные проблемы перед переходом к следующему пункту. Новые автоматические тесты добавляются только при обнаружении материального риска, который не покрыт существующими проверками.
