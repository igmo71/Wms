using System.Reflection;
using System.Runtime.Serialization;

namespace Wms.Common;

public static class ODataEnumMapper
{
    public static TEnum Parse<TEnum>(string? valueFromOData) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(valueFromOData))
            return default;

        var enumType = typeof(TEnum);

        foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var attribute = field.GetCustomAttribute<EnumMemberAttribute>();

            if (attribute != null && string.Equals(attribute.Value, valueFromOData, StringComparison.OrdinalIgnoreCase))
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
}