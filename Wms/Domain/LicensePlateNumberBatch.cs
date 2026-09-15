using Wms.Common;

namespace Wms.Domain;

public sealed class LicensePlateNumberBatch
{
    public const int MaximumQuantity = 500;
    private LicensePlateNumberBatch() { }
    public Guid Id { get; private set; }
    public DateTimeOffset IssuedAtUtc { get; private set; }
    public string IssuedBy { get; private set; } = null!;
    public IReadOnlyCollection<LicensePlateNumber> Labels => _labels;
    private readonly List<LicensePlateNumber> _labels = [];

    public static OperationResult<LicensePlateNumberBatch> Issue(int quantity, string userId, DateTimeOffset now)
    {
        if (quantity is < 1 or > MaximumQuantity)
            return OperationError.Invalid($"Количество этикеток должно быть от 1 до {MaximumQuantity}.");
        if (string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Пользователь выпуска не определён.");
        var batch = new LicensePlateNumberBatch { Id = Guid.NewGuid(), IssuedAtUtc = now, IssuedBy = userId };
        for (var i = 0; i < quantity; i++)
            batch._labels.Add(new LicensePlateNumber(batch.Id));
        return batch;
    }
}
