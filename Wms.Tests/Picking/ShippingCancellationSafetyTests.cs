using System.Text.Json.Nodes;
using Wms.Domain;
using Wms.Integration.OneS;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Picking;

public sealed class ShippingCancellationSafetyTests
{
    private static readonly DateTimeOffset At =
        new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Changed_1c_requisites_block_automatic_status_return()
    {
        var sourceId = Guid.NewGuid();
        var original = Document(sourceId, "Подготовлен", true, "Получатель A", 3);
        var parser = new ShippingStoredSnapshotParser();
        var afterOwnStatusChange = Value(parser.Parse(sourceId,
            Document(sourceId, "КОтбору", false, "Получатель A", 3)));
        Assert.True(Value(parser.UnchangedApartFromStatusAndPosting(
            sourceId, original, afterOwnStatusChange)));

        var changedRecipient = Value(parser.Parse(sourceId,
            Document(sourceId, "КОтбору", true, "Получатель B", 3)));
        Assert.False(Value(parser.UnchangedApartFromStatusAndPosting(
            sourceId, original, changedRecipient)));
        var changedLine = Value(parser.Parse(sourceId,
            Document(sourceId, "КОтбору", true, "Получатель A", 4)));
        Assert.False(Value(parser.UnchangedApartFromStatusAndPosting(
            sourceId, original, changedLine)));
    }

    [Fact]
    public void Confirmed_start_keeps_source_link_until_return_patch_is_confirmed()
    {
        var sourceId = Guid.NewGuid();
        var assembly = Value(Wms.Domain.PickingAssembly.CreateNew(Guid.NewGuid(),
            "operator", At));
        Value(assembly.AttachSource(sourceId, "{}",
            new Dictionary<Guid, decimal>(), "operator", At));
        Success(assembly.FreezeForStart("operator", At.AddMinutes(1)));
        var leaseId = Guid.NewGuid();
        Success(assembly.ClaimStart(leaseId, At.AddMinutes(2),
            At.AddMinutes(12)));
        Success(assembly.RecordStartPatchIntent(leaseId, sourceId,
            At.AddMinutes(3)));
        Success(assembly.RecordSourceStartResult(leaseId, sourceId,
            At.AddMinutes(4), null));
        Success(assembly.ReleaseStart(leaseId));
        Success(assembly.RequestCancellation("manager",
            assembly.OperationalRevision, At.AddMinutes(5)));

        Error(assembly.ReleaseCancelledSource(sourceId, "manager",
            assembly.OperationalRevision, At.AddMinutes(6)),
            Wms.Common.OperationErrorType.Conflict);
        Success(assembly.RecordReturnPatchIntent(sourceId,
            assembly.OperationalRevision, At.AddMinutes(6)));
        Success(assembly.ConfirmReturnPatch(sourceId,
            assembly.OperationalRevision, At.AddMinutes(7)));
        Success(assembly.ReleaseCancelledSource(sourceId, "manager",
            assembly.OperationalRevision, At.AddMinutes(8)));
        var link = Assert.Single(assembly.SourceLinks);
        Assert.NotNull(link.ReturnPostConfirmedAtUtc);
        Assert.Empty(assembly.ActiveSources);
    }

    private static string Document(Guid sourceId, string status,
        bool posted, string recipient, int quantity) => new JsonObject
    {
        ["Ref_Key"] = sourceId.ToString(),
        ["Статус"] = status,
        ["Posted"] = posted,
        ["Получатель"] = recipient,
        ["ОтгружаемыеТовары"] = new JsonArray
        {
            new JsonObject { ["LineNumber"] = 1, ["Количество"] = quantity }
        },
        ["ТоварыПоРаспоряжениям"] = new JsonArray()
    }.ToJsonString();
}
