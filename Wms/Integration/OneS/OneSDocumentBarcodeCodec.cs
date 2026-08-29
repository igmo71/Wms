using System.Globalization;
using System.Numerics;
using Wms.Common;

namespace Wms.Integration.OneS;

public static class OneSDocumentBarcodeCodec
{
    private static readonly BigInteger MaximumGuidValue = (BigInteger.One << 128) - 1;

    public static OperationResult<Guid> Decode(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return OperationError.Invalid("Штрихкод документа 1С не указан.");
        }

        if (payload.Any(character => character is < '0' or > '9'))
        {
            return OperationError.Invalid(
                "Штрихкод документа 1С должен содержать допустимое десятичное представление Ref_Key.");
        }

        var normalizedPayload = payload.TrimStart('0');
        if (normalizedPayload.Length == 0)
        {
            normalizedPayload = "0";
        }

        if (!BigInteger.TryParse(
            normalizedPayload,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var numericValue)
            || numericValue < 0
            || numericValue > MaximumGuidValue)
        {
            return OperationError.Invalid(
                "Штрихкод документа 1С должен содержать допустимое десятичное представление Ref_Key.");
        }

        var hexadecimalValue = numericValue.ToString("x", CultureInfo.InvariantCulture)
            .PadLeft(32, '0');
        if (!Guid.TryParseExact(hexadecimalValue, "N", out var documentId)
            || Encode(documentId) != normalizedPayload)
        {
            return OperationError.Invalid(
                "Штрихкод документа 1С имеет некорректный формат.");
        }

        return documentId;
    }

    public static string Encode(Guid documentId)
    {
        var numericValue = BigInteger.Zero;
        foreach (var character in documentId.ToString("N"))
        {
            numericValue = numericValue * 16 + Convert.ToInt32(character.ToString(), 16);
        }

        return numericValue.ToString(CultureInfo.InvariantCulture);
    }
}
