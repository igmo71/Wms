using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Tests.Replenishment;

public sealed class ReplenishmentTaskStateTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);
    private const string Worker = "operator";

    [Fact]
    public void FullyTransferredTaskRetainsSourceUntilReturn()
    {
        var task = Order(10);
        var sourceId = Guid.NewGuid();
        var cycle = task.StartSource(Guid.NewGuid(), sourceId, Worker, StartedAt).Value!;

        var transfer = task.ConfirmTransfer(Guid.NewGuid(), cycle.Id, Guid.NewGuid(),
            new DateOnly(2027, 1, 1), 10, Guid.NewGuid(), Worker, StartedAt.AddMinutes(1));

        Assert.True(transfer.IsSuccess);
        Assert.Equal(0m, task.RemainingQuantity);
        Assert.Equal(ReplenishmentTaskStatus.InProgress, task.Status);
        Assert.Null(cycle.CompletedAtUtc);
        Assert.False(task.Cancel(Worker, StartedAt.AddMinutes(2)).IsSuccess);
        Assert.False(task.CompleteSource(cycle.Id, Guid.NewGuid(), true,
            Worker, StartedAt.AddMinutes(2)).IsSuccess);
        Assert.Null(cycle.CompletedAtUtc);

        Assert.True(task.CompleteSource(cycle.Id, sourceId, true,
            Worker, StartedAt.AddMinutes(2)).IsSuccess);
        Assert.Equal(ReplenishmentTaskStatus.Completed, task.Status);
        Assert.Equal(transfer.Value!.ExpirationDate, Assert.Single(cycle.Transfers).ExpirationDate);
    }

    [Fact]
    public void PartialTaskKeepsPostedTransfersWhileChangingSource()
    {
        var task = Order(10);
        var sourceId = Guid.NewGuid();
        var firstCycle = task.StartSource(Guid.NewGuid(), sourceId, Worker, StartedAt).Value!;
        Assert.True(task.ConfirmTransfer(Guid.NewGuid(), firstCycle.Id, Guid.NewGuid(), null,
            8, Guid.NewGuid(), Worker, StartedAt.AddMinutes(1)).IsSuccess);

        Assert.False(task.StartSource(Guid.NewGuid(), Guid.NewGuid(),
            Worker, StartedAt.AddMinutes(2)).IsSuccess);
        Assert.True(task.CompleteSource(firstCycle.Id, sourceId, true,
            Worker, StartedAt.AddMinutes(2)).IsSuccess);
        Assert.Equal(ReplenishmentTaskStatus.InProgress, task.Status);
        Assert.Equal(2m, task.RemainingQuantity);

        var secondCycle = task.StartSource(Guid.NewGuid(), Guid.NewGuid(),
            Worker, StartedAt.AddMinutes(3));
        Assert.True(secondCycle.IsSuccess);
        Assert.Equal(8m, task.TransferredQuantity);
        Assert.Null(Assert.Single(firstCycle.Transfers).ExpirationDate);
        Assert.False(task.Cancel(Worker, StartedAt.AddMinutes(4)).IsSuccess);
    }

    [Fact]
    public void RejectedOrDuplicateTransferDoesNotChangeProgress()
    {
        var task = Order(10);
        var cycle = task.StartSource(Guid.NewGuid(), Guid.NewGuid(), Worker, StartedAt).Value!;
        var transferId = Guid.NewGuid();
        var movementId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();

        Assert.False(task.ConfirmTransfer(transferId, cycle.Id, destinationId, null,
            11, movementId, Worker, StartedAt.AddMinutes(1)).IsSuccess);
        Assert.Empty(cycle.Transfers);
        Assert.Equal(0m, task.TransferredQuantity);

        Assert.True(task.ConfirmTransfer(transferId, cycle.Id, destinationId, null,
            4, movementId, Worker, StartedAt.AddMinutes(1)).IsSuccess);
        Assert.False(task.ConfirmTransfer(transferId, cycle.Id, destinationId, null,
            4, movementId, Worker, StartedAt.AddMinutes(2)).IsSuccess);
        Assert.Single(cycle.Transfers);
        Assert.Equal(4m, task.TransferredQuantity);
    }

    private static ReplenishmentTask Order(decimal quantity) =>
        ReplenishmentTask.Order(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            quantity, null, null, Worker, StartedAt).Value!;
}
