namespace Wms.Domain;

public class StorageLocation
{
    public Guid Id { get; private set; }
    public int Number { get; private set; }
    public string? Code { get; private set; }
    public string? Name { get; private set; }
    public bool IsFolder { get; private set; }
    public bool DeletionMark { get; private set; }

    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; set; }

    public Guid ZoneId { get; private set; }
    public Zone? Zone { get; set; }

    public Guid? ParentId { get; private set; }
    public StorageLocation? Parent { get; set; }
    public List<StorageLocation> Children { get; set; } = [];

    public LocationDimensions Dimensions { get; private set; } = new();
    public LocationCoordinates Coordinates { get; private set; } = new();

    public long? PickSequence { get; private set; }

    public string Barcode => $"WMSL:{Id:N}";

    public static StorageLocation Create(
        Guid id,
        Guid warehouseId,
        Guid zoneId,
        Guid? parentId,
        int number,
        string code,
        string name,
        bool isFolder,
        LocationDimensions dimensions,
        LocationCoordinates coordinates,
        long? pickSequence)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Location identifier is required.", nameof(id));
        if (warehouseId == Guid.Empty)
            throw new ArgumentException("Warehouse identifier is required.", nameof(warehouseId));
        if (zoneId == Guid.Empty)
            throw new ArgumentException("Zone identifier is required.", nameof(zoneId));
        if (parentId == id)
            throw new ArgumentException("A location cannot be its own parent.", nameof(parentId));
        if (number <= 0)
            throw new ArgumentOutOfRangeException(nameof(number), "Location number must be positive.");

        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Location code is required.", nameof(code));

        var location = new StorageLocation
        {
            Id = id,
            WarehouseId = warehouseId,
            ZoneId = zoneId,
            ParentId = parentId,
            Number = number,
            Code = code
        };

        location.UpdateDetails(name, isFolder, dimensions, coordinates, pickSequence);
        return location;
    }

    public void UpdateDetails(
        string name,
        bool isFolder,
        LocationDimensions dimensions,
        LocationCoordinates coordinates,
        long? pickSequence)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Location name is required.", nameof(name));

        dimensions.Validate();
        coordinates.Validate();

        Name = name.Trim();
        IsFolder = isFolder;
        Dimensions = dimensions;
        Coordinates = coordinates;
        PickSequence = pickSequence;
    }

    public void Deactivate() => DeletionMark = true;

    public void Activate() => DeletionMark = false;
}

public class LocationDimensions
{
    public double? Length { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public double? Volume { get; set; }
    public double? VolumeFactor { get; set; }
    public double? MaxWeight { get; set; }

    public double? UsableVolume => Volume * (VolumeFactor ?? 1d);

    public void Validate()
    {
        if (new[] { Length, Width, Height, Volume, VolumeFactor, MaxWeight }
            .Any(x => x.HasValue && !double.IsFinite(x.Value)))
        {
            throw new ArgumentOutOfRangeException(nameof(LocationDimensions), "Dimensions and capacity must be finite numbers.");
        }

        if (Length < 0 || Width < 0 || Height < 0 || Volume < 0 || MaxWeight < 0)
            throw new ArgumentOutOfRangeException(nameof(LocationDimensions), "Dimensions and capacity cannot be negative.");

        if (VolumeFactor is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(VolumeFactor), "Volume factor must be greater than zero and at most one.");
    }
}

public class LocationCoordinates
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Z { get; set; }

    public void Validate()
    {
        if (new[] { X, Y, Z }.Any(x => x.HasValue && !double.IsFinite(x.Value)))
            throw new ArgumentOutOfRangeException(nameof(LocationCoordinates), "Coordinates must be finite numbers.");
    }
}
