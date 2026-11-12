using System.Text.Json;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public sealed class ShippingExchangeTarget
{
    public const int MaximumErrorLength = 2000;

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

    public bool IsConfirmed => Stage == ShippingExchangeStage.PostConfirmed;

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

    public OperationResult ConfirmPatch(long expectedRevision, DateTimeOffset atUtc)
    {
        var check = CheckTransition(expectedRevision, atUtc);
        if (!check.IsSuccess) return check;
        if (Stage != ShippingExchangeStage.PendingPatch)
            return OperationError.Conflict("PATCH уже подтверждён или требуется новое решение по документу 1С.");

        Stage = ShippingExchangeStage.PatchConfirmed;
        PatchConfirmedAtUtc = atUtc;
        LastAttemptAtUtc = atUtc;
        LastError = null;
        OperationalRevision++;
        return OperationResult.Success();
    }

    public OperationResult ConfirmPost(long expectedRevision, DateTimeOffset atUtc)
    {
        var check = CheckTransition(expectedRevision, atUtc);
        if (!check.IsSuccess) return check;
        if (Stage != ShippingExchangeStage.PatchConfirmed
            || PatchConfirmedAtUtc is null || atUtc < PatchConfirmedAtUtc)
            return OperationError.Conflict("Сначала подтвердите целевые строки и статус РО 1С.");

        Stage = ShippingExchangeStage.PostConfirmed;
        PostConfirmedAtUtc = atUtc;
        LastAttemptAtUtc = atUtc;
        LastError = null;
        OperationalRevision++;
        return OperationResult.Success();
    }

    public OperationResult RecordFailure(long expectedRevision,
        DateTimeOffset atUtc, string error)
    {
        var check = CheckTransition(expectedRevision, atUtc);
        if (!check.IsSuccess) return check;
        var message = ValidateError(error);
        if (!message.IsSuccess) return message.Error!;
        if (IsConfirmed)
            return OperationError.Conflict("Подтверждённый обмен РО 1С уже завершён.");

        LastAttemptAtUtc = atUtc;
        LastError = message.Value;
        OperationalRevision++;
        return OperationResult.Success();
    }

    public OperationResult RequireReview(long expectedRevision,
        DateTimeOffset atUtc, string reason)
    {
        var check = CheckTransition(expectedRevision, atUtc);
        if (!check.IsSuccess) return check;
        var message = ValidateError(reason);
        if (!message.IsSuccess) return message.Error!;
        if (IsConfirmed)
            return OperationError.Conflict("Подтверждённый обмен РО 1С уже завершён.");

        Stage = ShippingExchangeStage.NeedsReview;
        LastAttemptAtUtc = atUtc;
        LastError = message.Value;
        OperationalRevision++;
        return OperationResult.Success();
    }

    public OperationResult ApproveReviewedSource(long expectedRevision,
        DateTimeOffset atUtc, string reviewedSourceJson)
    {
        var check = CheckTransition(expectedRevision, atUtc);
        if (!check.IsSuccess) return check;
        if (Stage != ShippingExchangeStage.NeedsReview)
            return OperationError.Conflict("Новое решение требуется только для изменённого РО 1С.");
        if (!IsJsonObject(reviewedSourceJson) || DecisionRevision == long.MaxValue)
            return OperationError.Invalid("Укажите проверенное состояние РО 1С для нового решения.");

        ReviewedSourceJson = reviewedSourceJson;
        DecisionRevision++;
        Stage = ShippingExchangeStage.PendingPatch;
        PatchConfirmedAtUtc = null;
        PostConfirmedAtUtc = null;
        LastAttemptAtUtc = atUtc;
        LastError = null;
        OperationalRevision++;
        return OperationResult.Success();
    }

    private OperationResult CheckTransition(long expectedRevision,
        DateTimeOffset atUtc)
    {
        if (OperationalRevision != expectedRevision)
            return OperationError.Conflict("Состояние обмена РО 1С изменилось. Обновите документ.");
        if (atUtc == default || LastAttemptAtUtc is DateTimeOffset previous
            && atUtc < previous)
            return OperationError.Invalid("Время обмена предшествует последней попытке.");
        return OperationResult.Success();
    }

    private static OperationResult<string> ValidateError(string error)
    {
        var message = error?.Trim();
        return string.IsNullOrWhiteSpace(message) || message.Length > MaximumErrorLength
            ? OperationError.Invalid("Укажите причину ошибки обмена длиной до 2000 символов.")
            : message;
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
