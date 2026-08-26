using System.Globalization;
using Wms.Common;

namespace Wms.Domain;

public class StorageLocation
{
    private const string BarcodePrefix = "WMSL:";

    private readonly List<StorageLocation> _children = [];

    public Guid Id { get; private set; }
    public int Number { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool IsFolder { get; private set; }
    public bool DeletionMark { get; private set; }

    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }

    public Guid ZoneId { get; private set; }
    public Zone? Zone { get; private set; }

    public Guid? ParentId { get; private set; }
    public StorageLocation? Parent { get; private set; }
    public IReadOnlyCollection<StorageLocation> Children => _children;

    public LocationDimensions Dimensions { get; private set; } = LocationDimensions.Empty;
    public LocationCoordinates Coordinates { get; private set; } = LocationCoordinates.Empty;

    public long? PickSequence { get; private set; }

    public string Barcode => $"{BarcodePrefix}{Id:N}";

    public static bool TryParseBarcode(string? value, out Guid id)
    {
        id = Guid.Empty;
        return value is not null
            && value.StartsWith(BarcodePrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(value[BarcodePrefix.Length..], "N", out id);
    }

    public static OperationResult<string> BuildCode(
        string? parentCode,
        int number,
        int segmentWidth,
        int maximumLength)
    {
        if (number <= 0)
        {
            return OperationError.Invalid("Номер складской позиции должен быть положительным.");
        }

        if (segmentWidth is < 1 or > 8)
        {
            return OperationError.Invalid("Ширина сегмента должна быть от 1 до 8.");
        }

        if (maximumLength <= 0)
        {
            return OperationError.Invalid("Максимальная длина кода должна быть положительной.");
        }

        var segment = number.ToString($"D{segmentWidth}", CultureInfo.InvariantCulture);
        var code = string.IsNullOrEmpty(parentCode) ? segment : $"{parentCode}-{segment}";

        if (code.Length > maximumLength)
        {
            return OperationError.Invalid(
                $"Длина кода складской позиции не должна превышать {maximumLength} символов.");
        }

        return code;
    }

    public static OperationResult<StorageLocation> Create(
        Guid id,
        Guid warehouseId,
        Guid zoneId,
        Guid? parentId,
        int number,
        string code,
        StorageLocationDetails details)
    {
        if (id == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор складской позиции обязателен.");
        }

        if (warehouseId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор склада обязателен.");
        }

        if (zoneId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор зоны обязателен.");
        }

        if (parentId == id)
        {
            return OperationError.Invalid("Складская позиция не может быть родительской для самой себя.");
        }

        if (number <= 0)
        {
            return OperationError.Invalid("Номер складской позиции должен быть положительным.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return OperationError.Invalid("Код складской позиции обязателен.");
        }

        if (details is null)
        {
            return OperationError.Invalid("Параметры складской позиции обязательны.");
        }

        var location = new StorageLocation
        {
            Id = id,
            WarehouseId = warehouseId,
            ZoneId = zoneId,
            ParentId = parentId,
            Number = number,
            Code = code
        };

        location.ApplyDetails(details);
        return location;
    }

    public OperationResult UpdateDetails(StorageLocationDetails? details)
    {
        if (details is null)
        {
            return OperationError.Invalid("Параметры складской позиции обязательны.");
        }

        ApplyDetails(details);
        return OperationResult.Success();
    }

    public void Deactivate() => DeletionMark = true;

    public void Activate() => DeletionMark = false;

    private void ApplyDetails(StorageLocationDetails details)
    {
        Name = details.Name;
        IsFolder = details.IsFolder;
        Dimensions = details.Dimensions.Copy();
        Coordinates = details.Coordinates.Copy();
        PickSequence = details.PickSequence;
    }
}

public sealed class StorageLocationDetails
{
    private StorageLocationDetails(
        string name,
        bool isFolder,
        LocationDimensions dimensions,
        LocationCoordinates coordinates,
        long? pickSequence)
    {
        Name = name.Trim();
        IsFolder = isFolder;
        Dimensions = dimensions;
        Coordinates = coordinates;
        PickSequence = pickSequence;
    }

    public string Name { get; }
    public bool IsFolder { get; }
    public LocationDimensions Dimensions { get; }
    public LocationCoordinates Coordinates { get; }
    public long? PickSequence { get; }

    public static OperationResult<StorageLocationDetails> Create(
        string name,
        bool isFolder,
        LocationDimensions? dimensions,
        LocationCoordinates? coordinates,
        long? pickSequence)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationError.Invalid("Наименование складской позиции обязательно.");
        }

        if (dimensions is null)
        {
            return OperationError.Invalid("Размеры складской позиции обязательны.");
        }

        if (coordinates is null)
        {
            return OperationError.Invalid("Координаты складской позиции обязательны.");
        }

        return new StorageLocationDetails(name, isFolder, dimensions, coordinates, pickSequence);
    }
}

public sealed class LocationDimensions
{
    private LocationDimensions()
    {
    }

    private LocationDimensions(
        double? length,
        double? width,
        double? height,
        double? volume,
        double? volumeFactor,
        double? maxWeight)
    {
        Length = length;
        Width = width;
        Height = height;
        Volume = volume;
        VolumeFactor = volumeFactor;
        MaxWeight = maxWeight;
    }

    public static LocationDimensions Empty => new(null, null, null, null, null, null);

    public double? Length { get; private set; }
    public double? Width { get; private set; }
    public double? Height { get; private set; }
    public double? Volume { get; private set; }
    public double? VolumeFactor { get; private set; }
    public double? MaxWeight { get; private set; }

    public double? UsableVolume => Volume * (VolumeFactor ?? 1d);

    public static OperationResult<LocationDimensions> Create(
        double? length,
        double? width,
        double? height,
        double? volume,
        double? volumeFactor,
        double? maxWeight)
    {
        if (!AreFinite(length, width, height, volume, volumeFactor, maxWeight))
        {
            return OperationError.Invalid(
                "Размеры и вместимость должны быть конечными числами.");
        }

        if (length < 0 || width < 0 || height < 0 || volume < 0 || maxWeight < 0)
        {
            return OperationError.Invalid(
                "Размеры и вместимость не могут быть отрицательными.");
        }

        if (volumeFactor is <= 0 or > 1)
        {
            return OperationError.Invalid(
                "Коэффициент заполнения должен быть больше нуля и не превышать единицу.");
        }

        return new LocationDimensions(length, width, height, volume, volumeFactor, maxWeight);
    }

    public LocationDimensions Copy() => new(
        Length,
        Width,
        Height,
        Volume,
        VolumeFactor,
        MaxWeight);

    private static bool AreFinite(params double?[] values) =>
        values.All(value => !value.HasValue || double.IsFinite(value.Value));
}

public sealed class LocationCoordinates
{
    private LocationCoordinates()
    {
    }

    private LocationCoordinates(double? x, double? y, double? z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static LocationCoordinates Empty => new(null, null, null);

    public double? X { get; private set; }
    public double? Y { get; private set; }
    public double? Z { get; private set; }

    public static OperationResult<LocationCoordinates> Create(double? x, double? y, double? z)
    {
        if (!AreFinite(x, y, z))
        {
            return OperationError.Invalid("Координаты должны быть конечными числами.");
        }

        return new LocationCoordinates(x, y, z);
    }

    public LocationCoordinates Copy() => new(X, Y, Z);

    private static bool AreFinite(params double?[] values) =>
        values.All(value => !value.HasValue || double.IsFinite(value.Value));
}
