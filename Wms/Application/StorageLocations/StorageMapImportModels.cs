namespace Wms.Application.StorageLocations;

public enum StorageMapKind
{
    Overview,
    Tier,
    Individual
}

public sealed record StorageMapIdentity(string Area, StorageMapKind Kind, int? Tier);

public sealed record StorageMapImportFile(
    string Name,
    byte[] Content,
    StorageMapIdentity Identity,
    Guid? ZoneId,
    bool Include);

public sealed record StorageMapIssue(
    string File,
    int? Row,
    int? Column,
    string? Address,
    string Message,
    bool BlocksImport);

public sealed record StorageMapFileSummary(
    string File,
    string Area,
    StorageMapKind Kind,
    int Entries,
    int UniqueAddresses);

public sealed record StorageMapLocationPreview(
    string Code,
    string DisplayAddress,
    string Area,
    string Zone,
    bool AlreadyExists);

public sealed record StorageMapImportPreview(
    string Signature,
    IReadOnlyList<StorageMapFileSummary> Files,
    IReadOnlyList<StorageMapLocationPreview> Locations,
    IReadOnlyList<StorageMapIssue> Issues,
    int FolderCount,
    int NewFolderCount)
{
    public int NewLocationCount => Locations.Count(x => !x.AlreadyExists);
    public int ExistingLocationCount => Locations.Count(x => x.AlreadyExists);
    public bool HasBlockingIssues => Issues.Any(x => x.BlocksImport);
    public bool HasWarnings => Issues.Any(x => !x.BlocksImport);
}

public sealed record StorageMapImportResult(int CreatedLocations, int CreatedFolders, int ExistingLocations);
