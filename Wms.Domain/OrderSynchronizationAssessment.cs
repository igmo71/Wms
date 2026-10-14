using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public sealed record OrderSynchronizationDifference(
    string FieldCode,
    string FieldName,
    string? WmsValue,
    string? OneCValue,
    OrderSynchronizationLevel Level);

public sealed record OrderSynchronizationAssessment(
    string Fingerprint,
    IReadOnlyList<OrderSynchronizationDifference> Differences)
{
    public OrderSynchronizationLevel Level => Differences.Count == 0
        ? OrderSynchronizationLevel.Synchronized
        : Differences.Max(x => x.Level);
}

internal sealed class OrderSynchronizationComparisonBuilder(string fingerprint)
{
    private readonly List<OrderSynchronizationDifference> _differences = [];

    public void AddIfDifferent<T>(
        string fieldCode,
        string fieldName,
        T wmsValue,
        T oneCValue,
        OrderSynchronizationLevel level)
    {
        if (EqualityComparer<T>.Default.Equals(wmsValue, oneCValue))
        {
            return;
        }

        Add(fieldCode, fieldName, wmsValue, oneCValue, level);
    }

    public void Add(
        string fieldCode,
        string fieldName,
        object? wmsValue,
        object? oneCValue,
        OrderSynchronizationLevel level) =>
        _differences.Add(new OrderSynchronizationDifference(
            fieldCode,
            fieldName,
            FormatValue(wmsValue),
            FormatValue(oneCValue),
            level));

    public OrderSynchronizationAssessment Build() =>
        new(fingerprint, _differences);

    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        bool boolean => boolean ? "Да" : "Нет",
        DateTime dateTime => dateTime.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("dd.MM.yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture),
        decimal number => number.ToString("0.###", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString("D"),
        Enum enumValue => enumValue.GetDisplayName(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };
}

internal sealed class OrderSynchronizationFingerprintBuilder
{
    private readonly StringBuilder _content = new();

    public void Add(string fieldCode, object? value)
    {
        string normalizedValue = Normalize(value);
        _content
            .Append(fieldCode.Length)
            .Append(':')
            .Append(fieldCode)
            .Append('=')
            .Append(normalizedValue.Length)
            .Append(':')
            .Append(normalizedValue)
            .Append(';');
    }

    public string Build()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(_content.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string Normalize(object? value) => value switch
    {
        null => "<null>",
        bool boolean => boolean ? "1" : "0",
        DateTime dateTime => $"{dateTime.Ticks}:{(int)dateTime.Kind}",
        DateTimeOffset dateTimeOffset => $"{dateTimeOffset.UtcTicks}:{dateTimeOffset.Offset.Ticks}",
        decimal number => number.ToString("G29", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString("N"),
        Enum enumValue => Convert.ToInt64(enumValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };
}
