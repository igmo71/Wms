using System.Reflection;
using System.Runtime.Serialization;

namespace Wms.Integration.OneS;

internal static class ODataEnumMapper
{
    public static TEnum Parse<TEnum>(string? valueFromOData) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(valueFromOData))
        {
            return default;
        }

        Type enumType = typeof(TEnum);

        foreach (FieldInfo field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            EnumMemberAttribute? attribute = field.GetCustomAttribute<EnumMemberAttribute>();

            if (attribute is not null
                && string.Equals(attribute.Value, valueFromOData, StringComparison.OrdinalIgnoreCase))
            {
                return (TEnum)field.GetValue(null)!;
            }

            if (string.Equals(field.Name, valueFromOData, StringComparison.OrdinalIgnoreCase))
            {
                return (TEnum)field.GetValue(null)!;
            }
        }

        return default;
    }

    public static string ToODataValue<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        FieldInfo? field = typeof(TEnum).GetField(value.ToString());

        if (field is null)
        {
            return value.ToString();
        }

        EnumMemberAttribute? attribute = field.GetCustomAttribute<EnumMemberAttribute>();

        return attribute?.Value ?? field.Name;
    }
}
