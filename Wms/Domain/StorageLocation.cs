using System.Globalization;
using Wms.Common;

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

    public static OperationResult<string> BuildCode(
        string? parentCode,
        int number,
        int segmentWidth,
        int maximumLength)
    {
        if (number <= 0)
        {
            return OperationError.Invalid<StorageLocation>("Location number must be positive.");
        }

        if (segmentWidth is < 1 or > 8)
        {
            return OperationError.Invalid<StorageLocation>("Segment width must be between 1 and 8.");
        }

        if (maximumLength <= 0)
        {
            return OperationError.Invalid<StorageLocation>("Maximum code length must be positive.");
        }

        var segment = number.ToString($"D{segmentWidth}", CultureInfo.InvariantCulture);
        var code = string.IsNullOrEmpty(parentCode) ? segment : $"{parentCode}-{segment}";

        if (code.Length > maximumLength)
        {
            return OperationError.Invalid<StorageLocation>(
                $"Location code must not exceed {maximumLength} characters.");
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
            return OperationError.Invalid<StorageLocation>("Location identifier is required.");
        }

        if (warehouseId == Guid.Empty)
        {
            return OperationError.Invalid<Warehouse>("Warehouse identifier is required.");
        }

        if (zoneId == Guid.Empty)
        {
            return OperationError.Invalid<Zone>("Zone identifier is required.");
        }

        if (parentId == id)
        {
            return OperationError.Invalid<StorageLocation>("A location cannot be its own parent.");
        }

        if (number <= 0)
        {
            return OperationError.Invalid<StorageLocation>("Location number must be positive.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return OperationError.Invalid<StorageLocation>("Location code is required.");
        }

        if (details is null)
        {
            return OperationError.Invalid<StorageLocation>("Location details are required.");
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
            return OperationError.Invalid<StorageLocation>("Location details are required.");
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
            return OperationError.Invalid<StorageLocation>("Location name is required.");
        }

        if (dimensions is null)
        {
            return OperationError.Invalid<StorageLocation>("Location dimensions are required.");
        }

        if (coordinates is null)
        {
            return OperationError.Invalid<StorageLocation>("Location coordinates are required.");
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
            return OperationError.Invalid<LocationDimensions>(
                "Dimensions and capacity must be finite numbers.");
        }

        if (length < 0 || width < 0 || height < 0 || volume < 0 || maxWeight < 0)
        {
            return OperationError.Invalid<LocationDimensions>(
                "Dimensions and capacity cannot be negative.");
        }

        if (volumeFactor is <= 0 or > 1)
        {
            return OperationError.Invalid<LocationDimensions>(
                "Volume factor must be greater than zero and at most one.");
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
            return OperationError.Invalid<LocationCoordinates>("Coordinates must be finite numbers.");
        }

        return new LocationCoordinates(x, y, z);
    }

    public LocationCoordinates Copy() => new(X, Y, Z);

    private static bool AreFinite(params double?[] values) =>
        values.All(value => !value.HasValue || double.IsFinite(value.Value));
}
