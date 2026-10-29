using Wms.Common;

namespace Wms.Domain;

public class StorageLocation
{
    private readonly List<StorageLocation> _children = [];

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool IsFolder { get; private set; }
    public bool AllowsMultipleSkus { get; private set; }
    public bool DeletionMark { get; private set; }

    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }

    public Guid? ZoneId { get; private set; }
    public Zone? Zone { get; private set; }

    public Guid? ParentId { get; private set; }
    public StorageLocation? Parent { get; private set; }
    public IReadOnlyCollection<StorageLocation> Children => _children;

    public LocationDimensions Dimensions { get; private set; } = LocationDimensions.Empty;
    public LocationCoordinates Coordinates { get; private set; } = LocationCoordinates.Empty;

    public long? PickSequence { get; private set; }
    public long OperationalRevision { get; private set; }
    public StorageLocationLock? ActiveLock { get; private set; }

    public string Barcode => Code;
    public string DisplayAddress => StorageLocationAddress.Format(Code);

    public static OperationResult<StorageLocation> Create(
        Guid id,
        Guid warehouseId,
        Guid? zoneId,
        Guid? parentId,
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

        if (parentId == id)
        {
            return OperationError.Invalid("Складская позиция не может быть родительской для самой себя.");
        }

        if (details is null)
        {
            return OperationError.Invalid("Параметры складской позиции обязательны.");
        }

        if (zoneId == Guid.Empty || (!details.IsFolder && zoneId is null))
            return OperationError.Invalid("Для места хранения выберите функциональную зону.");

        OperationResult<string> normalizedCode = StorageLocationAddress.Normalize(code);
        if (!normalizedCode.IsSuccess)
            return normalizedCode.Error!;

        var location = new StorageLocation
        {
            Id = id,
            WarehouseId = warehouseId,
            ZoneId = zoneId,
            ParentId = parentId,
            Code = normalizedCode.Value!
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

        if (!details.IsFolder && ZoneId is null)
            return OperationError.Invalid("Для места хранения выберите функциональную зону.");

        var eligibilityChanged = IsFolder != details.IsFolder
            || AllowsMultipleSkus != details.AllowsMultipleSkus;
        ApplyDetails(details);
        if (eligibilityChanged)
        {
            AdvanceOperationalRevision();
        }

        return OperationResult.Success();
    }

    public void Deactivate()
    {
        if (!DeletionMark)
        {
            DeletionMark = true;
            AdvanceOperationalRevision();
        }
    }

    public void Activate()
    {
        if (DeletionMark)
        {
            DeletionMark = false;
            AdvanceOperationalRevision();
        }
    }

    public void AdvanceOperationalRevision() => OperationalRevision++;

    private void ApplyDetails(StorageLocationDetails details)
    {
        Name = details.Name;
        IsFolder = details.IsFolder;
        AllowsMultipleSkus = details.AllowsMultipleSkus;
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
        long? pickSequence,
        bool allowsMultipleSkus)
    {
        Name = name.Trim();
        IsFolder = isFolder;
        Dimensions = dimensions;
        Coordinates = coordinates;
        PickSequence = pickSequence;
        AllowsMultipleSkus = allowsMultipleSkus;
    }

    public string Name { get; }
    public bool IsFolder { get; }
    public LocationDimensions Dimensions { get; }
    public LocationCoordinates Coordinates { get; }
    public long? PickSequence { get; }
    public bool AllowsMultipleSkus { get; }

    public static OperationResult<StorageLocationDetails> Create(
        string name,
        bool isFolder,
        LocationDimensions? dimensions,
        LocationCoordinates? coordinates,
        long? pickSequence,
        bool allowsMultipleSkus = false)
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

        if (isFolder && allowsMultipleSkus)
            return OperationError.Invalid("Структурная группа не может разрешать смешение SKU.");

        return new StorageLocationDetails(name, isFolder, dimensions, coordinates,
            pickSequence, allowsMultipleSkus);
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
