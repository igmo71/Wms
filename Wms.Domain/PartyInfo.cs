using Wms.Domain.Enums;

namespace Wms.Domain;

public sealed record PartyInfo(Guid Id, PartyType Type, string Name);
