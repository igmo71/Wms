# License plate number checks (I01)

Run from the repository root on Windows with .NET 10 and SQL Server LocalDB:

```powershell
dotnet build tests/LicensePlateNumbers/LicensePlateNumbers.csproj /m:1
dotnet run --project tests/LicensePlateNumbers/LicensePlateNumbers.csproj --no-build
```

The executable creates/migrates a unique `WmsLpn_<GUID>` LocalDB database and
removes only that database in `finally`. It does not load application connection
strings. A failure prints its exception and sets a nonzero exit code.

Checks cover input limits (1–500), caller/request validation, audit, sequential
codes, SQL uniqueness, model drift, search/pagination, single/batch reprint,
concurrent independent batches, concurrent same-id/same-input and different-input
requests, rollback on a failed receipt insert, response loss after commit,
maintenance without sequence reset, and sequence exhaustion without wraparound.
There are no receiving or inventory effects. The A4 HTML is written to ignored
`artifacts/lpn-check/preview.html`; the test rasterizes the actual SVG rectangles
and decodes each barcode back to the visible code using ZXing.

## Browser verification

```powershell
dotnet run --project tests/LicensePlateNumbers/LicensePlateNumbers.csproj --no-build -- --serve
```

This runs the checks except maintenance/exhaustion, then starts the real WebApp
on `http://localhost:5189` with the disposable database. It creates a confirmed
test-only Operator account `lpn-test@example.invalid`, password `Lpn-Test-2026!`.
The credentials belong only to this temporary local test. Ctrl+C stops the
server and removes its database.

## Ручная проверка разработчиком

1. Запустить временный WebApp командой `--serve` выше. До входа открыть
   `/lpn-labels` и прямую ссылку печати: обе требуют авторизации. Войти тестовым
   Operator. Для проверки Administrator назначить роль тестовому пользователю
   только в этой временной базе и войти заново.
2. Ввести с клавиатуры 11, выпустить пакет. Проверить 11 новых кодов, дату и
   автора; открыть весь пакет: первый лист содержит 10 этикеток, второй — одну.
   Пакет из 10 должен занимать ровно один лист без пустой дополнительной страницы.
3. Найти один из кодов через поиск, пройти постраничность; открыть печать одной
   этикетки и всего исходного пакета. Перезагрузить страницы: коды не меняются,
   число выпущенных записей не растёт. Несуществующий id печати даёт 404.
4. В браузере вызвать «Печать»: A4, книжная ориентация, масштаб 100%, без
   колонтитулов. Проверить число страниц в системном предпросмотре, отсутствие
   обрезанных полос, подписей, лишних листов и элементов интерфейса WMS.
5. Распечатать и прочитать несколько кодов целевым Urovo/ScanWedge (в том числе
   на краях листа). Полученная строка должна буквально совпасть с подписью
   `LPN` + 12 цифр. Проверить повторную распечатку той же этикетки. Эти тестовые
   распечатки не использовать в рабочей базе.
6. Проверить неопределённый результат в живом Web: в отладочном запуске временно
   выбросить исключение **один раз** сразу после успешного `await Labels.IssueAsync`
   в `Index.ExecuteAsync`, до сброса `_pending`. Проверить сообщение об
   неизвестном результате, блокировку количества и нового выпуска. Убрать
   искусственный сбой, нажать «Повторить выпуск»: request id, количество и
   пользователь прежние; возвращается один уже сохранённый пакет. Сверить
   количество пакетов/этикеток/receipts в временной базе. Отладочное изменение
   удалить. Серверный аналог потери ответа уже покрыт автоматическим тестом.
7. Пересоздать страницу после выпуска: список читается заново, выпуск сам не
   запускается. Pending-намерение между пересозданиями не восстанавливается.
8. Остановить временный сервер Ctrl+C; его база удаляется. Для основной
   тестовой среды применить `AddLicensePlateNumbers` обычной командой миграции,
   используя явно выбранную строку подключения.

Физическая печать, системный диалог и чтение ТСД остаются неподтверждёнными до
этого прохода. Программное чтение SVG и экранный предпросмотр их не заменяют.
