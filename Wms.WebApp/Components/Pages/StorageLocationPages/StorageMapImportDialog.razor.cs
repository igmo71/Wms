using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using Wms.Application.StorageLocations;
using Wms.Application.Zones;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.StorageLocationPages;

public partial class StorageMapImportDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;
    [Parameter, EditorRequired] public Warehouse Warehouse { get; set; } = null!;
    [Inject] private StorageMapImportService ImportService { get; set; } = null!;
    [Inject] private ZoneQueryService ZoneQueryService { get; set; } = null!;

    private readonly List<UploadState> _uploads = [];
    private readonly Dictionary<string, AreaState> _areas = new(StringComparer.Ordinal);
    private IReadOnlyList<Zone> _zones = [];
    private StorageMapImportPreview? _preview;
    private string? _error;
    private bool _acknowledgeWarnings;
    private bool _busy;

    protected override async Task OnInitializedAsync()
    {
        var result = await ZoneQueryService.ListAsync(new ZoneListQuery
        {
            WarehouseId = Warehouse.Id,
            ExcludeDeleted = true,
            SortBy = "Name",
            Take = 500
        });
        _zones = result.Items;
    }

    private async Task UploadAsync(InputFileChangeEventArgs args)
    {
        _busy = true;
        _error = null;
        InvalidatePreview();
        try
        {
            var files = args.GetMultipleFiles(StorageMapImportService.MaximumFileCount);
            var pending = new List<UploadState>();
            foreach (var file in files)
            {
                var identity = StorageMapImportService.Identify(file.Name);
                if (!identity.IsSuccess)
                {
                    _error = identity.Error?.Message;
                    return;
                }
                if (pending.Any(x => x.Name.Equals(file.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    _error = $"Карта «{file.Name}» выбрана дважды.";
                    return;
                }
                await using var stream = file.OpenReadStream(StorageMapImportService.MaximumFileBytes);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                pending.Add(new UploadState(file.Name, buffer.ToArray(), identity.Value!,
                    identity.Value!.Area != "gates"));
            }
            _uploads.Clear();
            _uploads.AddRange(pending);
            _areas.Clear();
            foreach (var upload in _uploads)
                _areas.TryAdd(upload.Identity.Area, new AreaState(upload.Identity.Area));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _error = $"Не удалось загрузить файлы. Каждый XLS должен быть не больше {StorageMapImportService.MaximumFileBytes / 1_000_000} МБ.";
        }
        finally
        {
            _busy = false;
        }
    }

    private void SetIncluded(UploadState upload, bool included)
    {
        upload.Include = included;
        InvalidatePreview();
    }

    private void InvalidatePreview()
    {
        _preview = null;
        _acknowledgeWarnings = false;
    }

    private IReadOnlyList<StorageMapImportFile> ImportFiles() => _uploads
        .Select(x => new StorageMapImportFile(x.Name, x.Content, x.Identity,
            _areas[x.Identity.Area].ZoneId, x.Include))
        .ToArray();

    private async Task PreviewAsync()
    {
        _busy = true;
        _error = null;
        InvalidatePreview();
        try
        {
            var result = await ImportService.PreviewAsync(Warehouse.Id, ImportFiles());
            if (result.IsSuccess)
                _preview = result.Value;
            else
                _error = result.Error?.Message;
        }
        catch
        {
            _error = "Не удалось проверить карты склада.";
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ImportAsync()
    {
        if (_preview is null)
            return;
        _busy = true;
        _error = null;
        try
        {
            var result = await ImportService.ImportAsync(Warehouse.Id, ImportFiles(),
                _preview.Signature, _acknowledgeWarnings);
            if (result.IsSuccess)
                MudDialog.Close(DialogResult.Ok(result.Value));
            else
            {
                _error = result.Error?.Message;
                InvalidatePreview();
            }
        }
        catch
        {
            _error = "Не удалось загрузить карты. Проверьте данные и повторите предпросмотр.";
            InvalidatePreview();
        }
        finally
        {
            _busy = false;
        }
    }

    private void Cancel() => MudDialog.Cancel();

    private int? FileEntryCount(string name) => _preview?.Files.FirstOrDefault(x => x.File == name)?.Entries;
    private static string CellPosition(StorageMapIssue issue) => issue.Row is int row && issue.Column is int column
        ? $"{row}:{column}" : "—";
    private static string KindName(StorageMapIdentity identity) => identity.Kind switch
    {
        StorageMapKind.Tier => $"Ярус {identity.Tier}",
        StorageMapKind.Overview => "Сводная",
        _ => "Отдельная"
    };
    private static string AreaName(string area) => area switch
    {
        "1" => "Фронтальные стеллажи",
        "2" => "Мезонин 1 этаж 1",
        "3" => "Мезонин 1 этаж 2",
        "4" => "Мезонин 1 этаж 3",
        "5" => "Мезонин 1 этаж 4",
        "gates" => "Ворота",
        "outside" => "Улица",
        _ => area
    };

    private sealed class UploadState(string name, byte[] content, StorageMapIdentity identity, bool include)
    {
        public string Name { get; } = name;
        public byte[] Content { get; } = content;
        public StorageMapIdentity Identity { get; } = identity;
        public bool Include { get; set; } = include;
    }

    private sealed class AreaState(string key)
    {
        public string Key { get; } = key;
        public Guid? ZoneId { get; set; }
    }
}
