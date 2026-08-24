namespace Wms.Mobile.Scanning;

public sealed class KeyboardWedgeBarcodeScanner : IKeyboardWedgeBarcodeScanner
{
    private static readonly IReadOnlyDictionary<string, string> TechnicalDetails =
        new Dictionary<string, string>
        {
            ["terminator"] = "entry-completed"
        };

    public BarcodeScanSource Source => BarcodeScanSource.KeyboardWedge;

    public event EventHandler<BarcodeScanEvent>? ScanReceived;

    public bool TryAccept(string? rawValue)
    {
        var value = rawValue?.TrimEnd('\r', '\n');
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        ScanReceived?.Invoke(
            this,
            new BarcodeScanEvent(
                value,
                Symbology: null,
                DateTimeOffset.UtcNow,
                Source,
                TechnicalDetails));

        return true;
    }
}
