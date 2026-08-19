using System.Globalization;

namespace Wms.Domain;

public class StorageLocation
{
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

    public string Barcode => $"WMSL:{Id:N}";

    public static string BuildCode(
        string? parentCode,
        int number,
        int segmentWidth,
        int maximumLength)
    {
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Location number must be positive.");
        }

        if (segmentWidth is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(segmentWidth),
                "Segment width must be between 1 and 8.");
        }

        if (maximumLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumLength),
                "Maximum code length must be positive.");
        }

        var segment = number.ToString($"D{segmentWidth}", CultureInfo.InvariantCulture);
        var code = string.IsNullOrEmpty(parentCode) ? segment : $"{parentCode}-{segment}";

        if (code.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Location code must not exceed {maximumLength} characters.",
                nameof(parentCode));
        }

        return code;
    }

    public static StorageLocation Create(
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
            throw new ArgumentException("Location identifier is required.", nameof(id));
        }

        if (warehouseId == Guid.Empty)
        {
            throw new ArgumentException("Warehouse identifier is required.", nameof(warehouseId));
        }

        if (zoneId == Guid.Empty)
        {
            throw new ArgumentException("Zone identifier is required.", nameof(zoneId));
        }

        if (parentId == id)
        {
            throw new ArgumentException("A location cannot be its own parent.", nameof(parentId));
        }

        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Location number must be positive.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Location code is required.", nameof(code));
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

        location.UpdateDetails(details);
        return location;
    }

    public void UpdateDetails(StorageLocationDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        Name = details.Name;
        IsFolder = details.IsFolder;
        Dimensions = details.Dimensions.Copy();
        Coordinates = details.Coordinates.Copy();
        PickSequence = details.PickSequence;
    }

    public void Deactivate() => DeletionMark = true;

    public void Activate() => DeletionMark = false;
}

public sealed class StorageLocationDetails
{
    public StorageLocationDetails(
        string name,
        bool isFolder,
        LocationDimensions dimensions,
        LocationCoordinates coordinates,
        long? pickSequence)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Location name is required.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(coordinates);

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
}

public sealed class LocationDimensions
{
    private LocationDimensions()
    {
    }

    public LocationDimensions(
        double? length,
        double? width,
        double? height,
        double? volume,
        double? volumeFactor,
        double? maxWeight)
    {
        ValidateFinite(length, nameof(length));
        ValidateFinite(width, nameof(width));
        ValidateFinite(height, nameof(height));
        ValidateFinite(volume, nameof(volume));
        ValidateFinite(volumeFactor, nameof(volumeFactor));
        ValidateFinite(maxWeight, nameof(maxWeight));
        ValidateNonNegative(length, nameof(length));
        ValidateNonNegative(width, nameof(width));
        ValidateNonNegative(height, nameof(height));
        ValidateNonNegative(volume, nameof(volume));
        ValidateNonNegative(maxWeight, nameof(maxWeight));

        if (volumeFactor is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(volumeFactor),
                "Volume factor must be greater than zero and at most one.");
        }

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

    public LocationDimensions Copy() => new(
        Length,
        Width,
        Height,
        Volume,
        VolumeFactor,
        MaxWeight);

    private static void ValidateFinite(double? value, string parameterName)
    {
        if (value.HasValue && !double.IsFinite(value.Value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Dimensions and capacity must be finite numbers.");
        }
    }

    private static void ValidateNonNegative(double? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Dimensions and capacity cannot be negative.");
        }
    }
}

public sealed class LocationCoordinates
{
    private LocationCoordinates()
    {
    }

    public LocationCoordinates(double? x, double? y, double? z)
    {
        ValidateFinite(x, nameof(x));
        ValidateFinite(y, nameof(y));
        ValidateFinite(z, nameof(z));

        X = x;
        Y = y;
        Z = z;
    }

    public static LocationCoordinates Empty => new(null, null, null);

    public double? X { get; private set; }
    public double? Y { get; private set; }
    public double? Z { get; private set; }

    public LocationCoordinates Copy() => new(X, Y, Z);

    private static void ValidateFinite(double? value, string parameterName)
    {
        if (value.HasValue && !double.IsFinite(value.Value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Coordinates must be finite numbers.");
        }
    }
}
