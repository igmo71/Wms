using System.Text.Json;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public sealed class ShippingExchangeTarget
{
    private ShippingExchangeTarget() { }

    public Guid PickingAssemblyId { get; private set; }
    public Guid ShippingOrderId { get; private set; }
    public long DecisionRevision { get; private set; }
    public string ReviewedSourceJson { get; private set; } = null!;
    // Immutable WMS decision (approved plan and allocated fact). The 1C adapter
    // derives concrete OData rows from it and the reviewed source snapshot.
    public string TargetDocumentJson { get; private set; } = null!;
    public ShippingOrderStatus TargetStatus { get; private set; }
    public ShippingExchangeStage Stage { get; private set; }
    public DateTimeOffset? PatchConfirmedAtUtc { get; private set; }
    public DateTimeOffset? PostConfirmedAtUtc { get; private set; }
    public DateTimeOffset? LastAttemptAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public long OperationalRevision { get; private set; }

    public static OperationResult<ShippingExchangeTarget> Create(Guid assemblyId,
        Guid shippingOrderId, long decisionRevision,
        string reviewedSourceJson, string targetDocumentJson)
    {
        if (assemblyId == Guid.Empty || shippingOrderId == Guid.Empty
            || decisionRevision < 0 || !IsJsonObject(reviewedSourceJson)
            || !IsJsonObject(targetDocumentJson))
            return OperationError.Invalid("Цель обмена требует документ и проверенные данные 1С.");
        return new ShippingExchangeTarget
        {
            PickingAssemblyId = assemblyId, ShippingOrderId = shippingOrderId,
            DecisionRevision = decisionRevision,
            ReviewedSourceJson = reviewedSourceJson,
            TargetDocumentJson = targetDocumentJson,
            TargetStatus = ShippingOrderStatus.Shipped,
            Stage = ShippingExchangeStage.PendingPatch
        };
    }

    private static bool IsJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var parsed = JsonDocument.Parse(json);
            return parsed.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; }
    }
}
