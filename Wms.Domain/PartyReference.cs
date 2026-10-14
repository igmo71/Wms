using Wms.Domain.Enums;

namespace Wms.Domain;

public readonly record struct PartyReference(Guid Id, PartyType Type);
