using System.Globalization;
using System.Numerics;
using Wms.Common;

namespace Wms.Integration.OneS;

public static class ShippingDocumentBarcodeCodec
{
    private static readonly BigInteger MaximumGuidValue = (BigInteger.One << 128) - 1;

    public static OperationResult<Guid> Decode(string? barcode)
    {
        if (string.IsNullOrEmpty(barcode)
            || barcode.Any(character => character is < '0' or > '9'))
            return OperationError.Invalid("Штрихкод РО 1С должен содержать десятичный Ref_Key.");

        var normalized = barcode.TrimStart('0');
        if (normalized.Length == 0)
            return OperationError.Invalid("Штрихкод РО 1С не может быть нулевым.");
        if (!BigInteger.TryParse(normalized, NumberStyles.None,
                CultureInfo.InvariantCulture, out var value)
            || value > MaximumGuidValue)
            return OperationError.Invalid("Штрихкод РО 1С содержит недопустимый Ref_Key.");

        var hex = value.ToString("x", CultureInfo.InvariantCulture)
            .TrimStart('0').PadLeft(32, '0');
        return Guid.TryParseExact(hex, "N", out var refKey) && refKey != Guid.Empty
            ? refKey
            : OperationError.Invalid("Штрихкод РО 1С содержит недопустимый Ref_Key.");
    }
}
