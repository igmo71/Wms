using Wms.Domain;
using CoordinateAxisValue = Wms.Application.StorageLocations.CoordinateAxis;

namespace Wms.Application.StorageLocations;

public sealed class CreateStorageLocationRequest
{
    public required Guid WarehouseId { get; init; }
    public required Guid ZoneId { get; init; }
    public required Guid? ParentId { get; init; }
    public int Number { get; init; } = 1;
    public int SegmentWidth { get; init; } = 2;
    public required StorageLocationDetails Details { get; init; }
}

public sealed class GenerateStorageLocationsRequest
{
    public const int MaximumCount = 1000;

    public required Guid WarehouseId { get; init; }
    public required Guid ZoneId { get; init; }
    public required Guid? ParentId { get; init; }
    public int Count { get; init; } = 1;
    public int StartNumber { get; init; } = 1;
    public int NumberStep { get; init; } = 1;
    public int SegmentWidth { get; init; } = 2;
    public string NamePrefix { get; init; } = "Позиция";
    public bool IsFolder { get; init; }
    public required LocationDimensions Dimensions { get; init; }
    public required LocationCoordinates StartCoordinates { get; init; }
    public CoordinateAxis? CoordinateAxis { get; init; }
    public double CoordinateStep { get; init; }
    public long? StartPickSequence { get; init; }
    public long PickSequenceStep { get; init; } = 1;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Dimensions);
        ArgumentNullException.ThrowIfNull(StartCoordinates);

        if (WarehouseId == Guid.Empty)
        {
            throw new ArgumentException("Идентификатор склада обязателен.", nameof(WarehouseId));
        }

        if (ZoneId == Guid.Empty)
        {
            throw new ArgumentException("Идентификатор зоны обязателен.", nameof(ZoneId));
        }

        if (Count is <= 0 or > MaximumCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Count),
                $"Количество должно быть от 1 до {MaximumCount}.");
        }

        if (StartNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(StartNumber), "Начальный номер должен быть положительным.");
        }

        if (NumberStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(NumberStep), "Шаг нумерации должен быть положительным.");
        }

        if (SegmentWidth is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SegmentWidth),
                "Ширина сегмента должна быть от 1 до 8.");
        }

        if (string.IsNullOrWhiteSpace(NamePrefix))
        {
            throw new ArgumentException("Префикс наименования обязателен.", nameof(NamePrefix));
        }

        if (!double.IsFinite(CoordinateStep) || CoordinateStep < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CoordinateStep),
                "Шаг координат должен быть конечным неотрицательным числом.");
        }

        if (CoordinateStep > 0 && CoordinateAxis is null)
        {
            throw new ArgumentException(
                "Для ненулевого шага выберите направление координат.",
                nameof(CoordinateAxis));
        }

        ValidateGeneratedRanges();
    }

    private void ValidateGeneratedRanges()
    {
        try
        {
            _ = checked(StartNumber + ((Count - 1) * NumberStep));

            if (StartPickSequence.HasValue)
            {
                _ = checked(StartPickSequence.Value + ((Count - 1L) * PickSequenceStep));
            }
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Count),
                "Диапазон номеров или порядка отбора слишком велик.");
        }

        var selectedStart = CoordinateAxis switch
        {
            CoordinateAxisValue.X => StartCoordinates.X,
            CoordinateAxisValue.Y => StartCoordinates.Y,
            CoordinateAxisValue.Z => StartCoordinates.Z,
            _ => null
        };

        var lastCoordinate = selectedStart + (CoordinateStep * (Count - 1));
        if (lastCoordinate.HasValue && !double.IsFinite(lastCoordinate.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(CoordinateStep),
                "Диапазон координат слишком велик.");
        }
    }
}

public enum CoordinateAxis
{
    X,
    Y,
    Z
}
