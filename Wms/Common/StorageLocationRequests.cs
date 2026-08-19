using Wms.Domain;

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
}

public enum CoordinateAxis
{
    X,
    Y,
    Z
}
