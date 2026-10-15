using Wms.Common;

namespace Wms.Domain;

public sealed class ReceivingOrderSourceLink
{
    private ReceivingOrderSourceLink() { }

    public Guid Id { get; private set; }
    public Guid ReceivingOrderId { get; private set; }
    public ReceivingOrder? ReceivingOrder { get; private set; }
    public Guid ReceivingSourceDocumentId { get; private set; }
    public ReceivingSourceDocument? ReceivingSourceDocument { get; private set; }
    public DateTimeOffset AttachedAtUtc { get; private set; }
    public DateTimeOffset? DetachedAtUtc { get; private set; }

    public static OperationResult<ReceivingOrderSourceLink> Create(
        Guid orderId, Guid sourceId, DateTimeOffset attachedAtUtc)
    {
        if (orderId == Guid.Empty || sourceId == Guid.Empty || attachedAtUtc == default)
            return OperationError.Invalid("Ордер, источник и время привязки обязательны.");

        return new ReceivingOrderSourceLink
        {
            Id = Guid.NewGuid(), ReceivingOrderId = orderId,
            ReceivingSourceDocumentId = sourceId, AttachedAtUtc = attachedAtUtc
        };
    }

    public OperationResult Detach(DateTimeOffset detachedAtUtc)
    {
        if (DetachedAtUtc is not null)
            return OperationError.Conflict("Источник уже откреплён от ордера.");
        if (detachedAtUtc < AttachedAtUtc)
            return OperationError.Invalid("Время открепления не может предшествовать привязке.");

        DetachedAtUtc = detachedAtUtc;
        return OperationResult.Success();
    }
}
