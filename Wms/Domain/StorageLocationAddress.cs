using Wms.Common;

namespace Wms.Domain;

public static class StorageLocationAddress
{
    public static OperationResult<string> Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return OperationError.Invalid("Адрес складской позиции обязателен.");

        var code = new string(value.Trim().ToUpperInvariant()
            .Where(character => character is not ('-' or '.' or '\'' or ' ' or '·'))
            .ToArray());
        if (code.Length is < 1 or > 32 || code.Any(character => character is not (>= 'A' and <= 'Z' or >= '0' and <= '9')))
            return OperationError.Invalid("Адрес должен содержать от 1 до 32 латинских букв или цифр.");

        return code;
    }

    public static string Format(string code) =>
        code.Length == 6
        && char.IsDigit(code[0])
        && char.IsLetter(code[1])
        && char.IsDigit(code[2])
        && char.IsDigit(code[3])
        && char.IsLetter(code[4])
        && char.IsDigit(code[5])
            ? $"{code[0]}-{code[1]}-{code.Substring(2, 2)}-{code[4]}-{code[5]}"
            : code;
}
