namespace Wms.Domain;

// Immutable evidence of the quantities and source reviewed at completion.
public sealed record ReceivingCompletionDecision(
    string Reason,
    string UserId,
    DateTimeOffset CompletedAtUtc,
    ReceivingOrderImportSnapshot Source,
    IReadOnlyList<ReceivingCompletionDecisionLine> Lines,
    IReadOnlyList<OrderSynchronizationDifference> Differences);

public sealed record ReceivingCompletionDecisionLine(
    int LineNumber, Guid StockKeepingUnitId, decimal PlanQuantity, decimal FactQuantity);
