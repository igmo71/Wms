namespace Wms.Mobile.Scanning;

public sealed record BarcodeScanEvent(
    string Value,
    string? Symbology,
    DateTimeOffset ReceivedAt,
    BarcodeScanSource Source,
    IReadOnlyDictionary<string, string> TechnicalDetails);
