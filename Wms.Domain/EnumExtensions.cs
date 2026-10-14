using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Wms.Common;

public static class EnumExtensions
{
    public static string GetDisplayName(this Enum enumValue)
    {
        var memberInfo = enumValue.GetType()
                                  .GetMember(enumValue.ToString())
                                  .FirstOrDefault();

        if (memberInfo != null)
        {
            var displayAttribute = memberInfo.GetCustomAttribute<DisplayAttribute>();
            if (displayAttribute != null && !string.IsNullOrEmpty(displayAttribute.Name))
            {
                return displayAttribute.Name;
            }
        }

        return enumValue.ToString();
    }
}
