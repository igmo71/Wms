using Wms.Domain;
using Wms.Domain.Enums;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Picking;

public sealed class ShippingExchangeTargetStateTests
{
    private static readonly DateTimeOffset At =
        new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Post_is_confirmed_only_after_patch_and_a_failed_post_remains_retryable()
    {
        var target = Target();
        Error(target.ConfirmPost(0, At), Wms.Common.OperationErrorType.Conflict);
        Success(target.RecordFailure(0, At, "PATCH временно недоступен"));
        Assert.Equal(ShippingExchangeStage.PendingPatch, target.Stage);
        Assert.Equal("PATCH временно недоступен", target.LastError);

        Success(target.ConfirmPatch(1, At.AddMinutes(1)));
        Success(target.RecordFailure(2, At.AddMinutes(2), "Ответ POST потерян"));
        Assert.Equal(ShippingExchangeStage.PatchConfirmed, target.Stage);
        Assert.NotNull(target.PatchConfirmedAtUtc);
        Assert.Null(target.PostConfirmedAtUtc);

        Success(target.ConfirmPost(3, At.AddMinutes(3)));
        Assert.True(target.IsConfirmed);
        Assert.Null(target.LastError);
        Assert.Equal(At.AddMinutes(3), target.PostConfirmedAtUtc);
        Error(target.RecordFailure(4, At.AddMinutes(4), "Повтор"),
            Wms.Common.OperationErrorType.Conflict);
    }

    [Fact]
    public void New_review_increments_only_its_document_decision_and_resets_patch()
    {
        var target = Target();
        Success(target.ConfirmPatch(0, At));
        Success(target.RequireReview(1, At.AddMinutes(1),
            "Источник изменился после PATCH"));
        Assert.Equal(ShippingExchangeStage.NeedsReview, target.Stage);
        Error(target.ConfirmPost(2, At.AddMinutes(2)),
            Wms.Common.OperationErrorType.Conflict);
        Error(target.ApproveReviewedSource(1, At.AddMinutes(2), "{\"version\":2}"),
            Wms.Common.OperationErrorType.Conflict);

        Success(target.ApproveReviewedSource(2, At.AddMinutes(2),
            "{\"version\":2}"));
        Assert.Equal(8, target.DecisionRevision);
        Assert.Equal(ShippingExchangeStage.PendingPatch, target.Stage);
        Assert.Null(target.PatchConfirmedAtUtc);
        Assert.Null(target.PostConfirmedAtUtc);
        Assert.Null(target.LastError);
        Assert.Equal("{\"version\":2}", target.ReviewedSourceJson);
        Assert.Equal("{\"approved\":true}", target.TargetDocumentJson);
    }

    private static ShippingExchangeTarget Target() =>
        Value(ShippingExchangeTarget.Create(Guid.NewGuid(), Guid.NewGuid(), 7,
            "{\"version\":1}", "{\"approved\":true}"));
}
