using Wms.Domain;
using CoordinateAxisValue = Wms.Common.CoordinateAxis;

namespace Wms.Common;

public class CreateStorageLocationRequest
{
    public Guid WarehouseId { get; set; }
    public Guid ZoneId { get; set; }
    public Guid? ParentId { get; set; }
    public int Number { get; set; } = 1;
    public int SegmentWidth { get; set; } = 2;
    public string Name { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public LocationDimensions Dimensions { get; set; } = new();
    public LocationCoordinates Coordinates { get; set; } = new();
    public long? PickSequence { get; set; }
}

public class UpdateStorageLocationRequest
{
    public string Name { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public LocationDimensions Dimensions { get; set; } = new();
    public LocationCoordinates Coordinates { get; set; } = new();
    public long? PickSequence { get; set; }
}

public class GenerateStorageLocationsRequest
{
    public const int MaximumCount = 1000;

    public Guid WarehouseId { get; set; }
    public Guid ZoneId { get; set; }
    public Guid? ParentId { get; set; }
    public int Count { get; set; } = 1;
    public int StartNumber { get; set; } = 1;
    public int NumberStep { get; set; } = 1;
    public int SegmentWidth { get; set; } = 2;
    public string NamePrefix { get; set; } = "Позиция";
    public bool IsFolder { get; set; }
    public LocationDimensions Dimensions { get; set; } = new();
    public LocationCoordinates StartCoordinates { get; set; } = new();
    public CoordinateAxis? CoordinateAxis { get; set; }
    public double CoordinateStep { get; set; }
    public long? StartPickSequence { get; set; }
    public long PickSequenceStep { get; set; } = 1;

    public void Validate()
    {
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

        ArgumentNullException.ThrowIfNull(Dimensions);
        ArgumentNullException.ThrowIfNull(StartCoordinates);
        Dimensions.Validate();
        StartCoordinates.Validate();
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
