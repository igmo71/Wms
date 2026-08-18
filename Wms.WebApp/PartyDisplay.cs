using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp;

public static class PartyDisplay
{
    public static string Format(PartyInfo? party, Guid id, PartyType type)
    {
        if (id == Guid.Empty)
            return "—";

        if (type is not (PartyType.Warehouse
            or PartyType.Partner
            or PartyType.Individual
            or PartyType.OrganizationalUnit))
        {
            return "Неизвестный тип";
        }

        if (party is null)
            return "Не найден";

        return string.IsNullOrWhiteSpace(party.Name) ? "Без наименования" : party.Name;
    }
}
