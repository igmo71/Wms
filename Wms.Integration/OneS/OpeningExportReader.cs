using System.Globalization;
using ExcelDataReader;
using Wms.Application.OpeningImports;

namespace Wms.Integration.OneS;

public sealed class OpeningExportReader : IOpeningExportReader
{
    public const int MaximumFileBytes = 20_000_000;
    public const int MaximumDataRows = 100_000;

    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly string[] Headers =
    [
        "Помещение", "Группа ячеек", "Ячейка", "Вид ячейки", "Номенклатура",
        "GUID", "Единица измерения", "Количество", "Дата", "Требуется указания даты"
    ];

    public OpeningExportReadResult Read(ReadOnlyMemory<byte> workbook, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (workbook.IsEmpty)
            return Failure("Файл XLSX пуст.");
        if (workbook.Length > MaximumFileBytes)
            return Failure($"Файл XLSX превышает допустимый размер {MaximumFileBytes / 1_000_000} МБ.");

        try
        {
            using var stream = new MemoryStream(workbook.ToArray(), writable: false);
            using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
            return ReadWorksheet(reader, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure("Файл XLSX не удалось прочитать.");
        }
    }

    private static OpeningExportReadResult ReadWorksheet(IExcelDataReader reader, CancellationToken ct)
    {
        var rows = new List<OpeningExportRow>();
        var issues = new List<OpeningExportParseIssue>();

        if (!reader.Read())
            return Failure("В файле XLSX отсутствует строка заголовков.");

        var columns = ReadHeaders(reader, issues);
        if (issues.Count > 0)
            return new OpeningExportReadResult(rows, issues);

        var rowNumber = 1;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            rowNumber++;
            if (rows.Count == MaximumDataRows)
            {
                issues.Add(new OpeningExportParseIssue(rowNumber, null, null,
                    $"В файле больше {MaximumDataRows} строк данных."));
                break;
            }

            string? Field(string header) => SourceText(reader.GetValue(columns[header]));

            var sourceSkuGuid = Field("GUID");
            var sourceQuantity = Field("Количество");
            var sourceDate = Field("Дата");
            var sourceDateRequirement = Field("Требуется указания даты");

            rows.Add(new OpeningExportRow
            {
                RowNumber = rowNumber,
                Area = Field("Помещение"),
                CellGroup = Field("Группа ячеек"),
                Address = Field("Ячейка"),
                CellKind = Field("Вид ячейки"),
                SkuName = Field("Номенклатура"),
                SourceSkuGuid = sourceSkuGuid,
                SkuId = ParseSkuId(sourceSkuGuid, rowNumber, issues),
                UnitOfMeasure = Field("Единица измерения"),
                SourceQuantity = sourceQuantity,
                Quantity = ParseQuantity(reader.GetValue(columns["Количество"]), sourceQuantity,
                    rowNumber, issues),
                SourceDate = sourceDate,
                ExpirationDate = ParseDate(reader.GetValue(columns["Дата"]), sourceDate,
                    rowNumber, issues),
                SourceDateRequirement = sourceDateRequirement,
                RequiresExpirationDate = ParseDateRequirement(sourceDateRequirement, rowNumber, issues)
            });
        }

        if (reader.NextResult())
            issues.Add(new OpeningExportParseIssue(null, null, null,
                "Файл должен содержать только один лист с выгрузкой."));

        return new OpeningExportReadResult(rows, issues);
    }

    private static Dictionary<string, int> ReadHeaders(
        IExcelDataReader reader, List<OpeningExportParseIssue> issues)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (reader.FieldCount != Headers.Length)
            issues.Add(new OpeningExportParseIssue(1, null, null,
                $"Ожидается {Headers.Length} столбцов выгрузки, найдено {reader.FieldCount}."));
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var header = SourceText(reader.GetValue(i))?.Trim();
            if (header is null)
                continue;
            if (!Headers.Contains(header, StringComparer.OrdinalIgnoreCase))
                issues.Add(new OpeningExportParseIssue(1, header, header,
                    "Неизвестный столбец выгрузки."));
            else if (!columns.TryAdd(header, i))
                issues.Add(new OpeningExportParseIssue(1, header, header,
                    "Столбец выгрузки указан несколько раз."));
        }

        foreach (var header in Headers)
        {
            if (!columns.ContainsKey(header))
                issues.Add(new OpeningExportParseIssue(1, header, null,
                    "В файле отсутствует обязательный столбец."));
        }

        return columns;
    }

    private static Guid? ParseSkuId(
        string? source, int rowNumber, List<OpeningExportParseIssue> issues)
    {
        if (source is null)
            return null;
        if (Guid.TryParse(source.Trim(), out var id) && id != Guid.Empty)
            return id;
        issues.Add(new OpeningExportParseIssue(rowNumber, "GUID", source,
            "Некорректный GUID номенклатуры."));
        return null;
    }

    private static decimal? ParseQuantity(
        object? value, string? source, int rowNumber, List<OpeningExportParseIssue> issues)
    {
        if (source is null)
            return null;

        decimal quantity;
        if (value is string text)
        {
            if (decimal.TryParse(text.Trim(), NumberStyles.Number, RussianCulture, out quantity)
                || decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture,
                    out quantity))
                return quantity;
        }
        else if (value is byte or sbyte or short or ushort or int or uint or long
                 or ulong or float or double or decimal)
        {
            try
            {
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                // The source value remains available in the issue below.
            }
        }

        issues.Add(new OpeningExportParseIssue(rowNumber, "Количество", source,
            "Количество не является числом."));
        return null;
    }

    private static DateOnly? ParseDate(
        object? value, string? source, int rowNumber, List<OpeningExportParseIssue> issues)
    {
        if (source is null)
            return null;
        if (value is DateTime dateTime && dateTime.TimeOfDay == TimeSpan.Zero)
            return DateOnly.FromDateTime(dateTime);
        if (value is string text && DateOnly.TryParseExact(text.Trim(),
                ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return date;

        issues.Add(new OpeningExportParseIssue(rowNumber, "Дата", source,
            "Дата имеет неподдерживаемый формат."));
        return null;
    }

    private static bool? ParseDateRequirement(
        string? source, int rowNumber, List<OpeningExportParseIssue> issues)
    {
        if (string.Equals(source?.Trim(), "Да", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(source?.Trim(), "Нет", StringComparison.OrdinalIgnoreCase))
            return false;
        issues.Add(new OpeningExportParseIssue(rowNumber, "Требуется указания даты", source,
            "Ожидается «Да» или «Нет»."));
        return null;
    }

    private static string? SourceText(object? value) => value switch
    {
        null => null,
        string text when string.IsNullOrWhiteSpace(text) => null,
        string text => text,
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static OpeningExportReadResult Failure(string message) =>
        new([], [new OpeningExportParseIssue(null, null, null, message)]);
}
