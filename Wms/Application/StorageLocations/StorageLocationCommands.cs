using Wms.Common;
using Wms.Domain;
using CoordinateAxisValue = Wms.Application.StorageLocations.CoordinateAxis;

namespace Wms.Application.StorageLocations;

public sealed class CreateStorageLocationCommand
{
    public required Guid WarehouseId { get; init; }
    public required Guid ZoneId { get; init; }
    public required Guid? ParentId { get; init; }
    public int Number { get; init; } = 1;
    public int SegmentWidth { get; init; } = 2;
    public required StorageLocationDetails Details { get; init; }
}

public sealed class GenerateStorageLocationsCommand
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

    public OperationResult Validate()
    {
        if (WarehouseId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор склада обязателен.");
        }

        if (ZoneId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор зоны обязателен.");
        }

        if (Count is <= 0 or > MaximumCount)
        {
            return OperationError.Invalid($"Количество должно быть от 1 до {MaximumCount}.");
        }

        if (StartNumber <= 0)
        {
            return OperationError.Invalid("Начальный номер должен быть положительным.");
        }

        if (NumberStep <= 0)
        {
            return OperationError.Invalid("Шаг нумерации должен быть положительным.");
        }

        if (SegmentWidth is < 1 or > 8)
        {
            return OperationError.Invalid("Ширина сегмента должна быть от 1 до 8.");
        }

        if (string.IsNullOrWhiteSpace(NamePrefix))
        {
            return OperationError.Invalid("Префикс наименования обязателен.");
        }

        if (Dimensions is null)
        {
            return OperationError.Invalid("Параметры размеров обязательны.");
        }

        if (StartCoordinates is null)
        {
            return OperationError.Invalid("Начальные координаты обязательны.");
        }

        if (!double.IsFinite(CoordinateStep) || CoordinateStep < 0)
        {
            return OperationError.Invalid("Шаг координат должен быть конечным неотрицательным числом.");
        }

        if (CoordinateStep > 0 && CoordinateAxis is null)
        {
            return OperationError.Invalid("Для ненулевого шага выберите направление координат.");
        }

        return ValidateGeneratedRanges();
    }

    private OperationResult ValidateGeneratedRanges()
    {
        var lastNumber = (long)StartNumber + ((Count - 1L) * NumberStep);
        if (lastNumber > int.MaxValue)
        {
            return OperationError.Invalid("Диапазон номеров слишком велик.");
        }

        if (StartPickSequence.HasValue)
        {
            var lastPickSequence = (decimal)StartPickSequence.Value
                + ((Count - 1m) * PickSequenceStep);
            if (lastPickSequence is < long.MinValue or > long.MaxValue)
            {
                return OperationError.Invalid("Диапазон порядка отбора слишком велик.");
            }
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
            return OperationError.Invalid("Диапазон координат слишком велик.");
        }

        return OperationResult.Success();
    }
}

public enum CoordinateAxis
{
    X,
    Y,
    Z
}
