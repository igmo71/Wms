using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.StorageLocations;
using Wms.Application.Zones;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.StorageLocationPages;

public partial class GenerateStorageLocationsDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;
    [Parameter, EditorRequired] public Warehouse Warehouse { get; set; } = null!;
    [Parameter, EditorRequired] public StorageLocation Parent { get; set; } = null!;
    [Parameter] public Zone? SuggestedZone { get; set; }
    [Inject] private StorageLocationBatchService BatchService { get; set; } = null!;
    [Inject] private ZoneQueryService ZoneQueryService { get; set; } = null!;

    private Zone? _zone;
    private int _roomFrom = 1;
    private int _roomTo = 1;
    private string _rowFrom = "A";
    private string _rowTo = "A";
    private int _sectionFrom = 1;
    private int _sectionTo = 1;
    private string _cellFrom = "A";
    private string _cellTo = "A";
    private int _levelFrom = 1;
    private int _levelTo = 1;
    private StorageLocationBatchPreview? _preview;
    private string? _error;
    private bool _busy;

    protected override void OnInitialized()
    {
        _zone = SuggestedZone;
        var code = Parent.Code;
        if (code.StartsWith("FROOM", StringComparison.Ordinal) && code.Length == 6)
            SetRoom(code[5]);
        else if (code.StartsWith("FROW", StringComparison.Ordinal) && code.Length == 6)
        {
            SetRoom(code[4]);
            SetRow(code[5]);
        }
        else if (code.StartsWith("FSECTION", StringComparison.Ordinal) && code.Length == 12)
        {
            SetRoom(code[8]);
            SetRow(code[9]);
            SetSection(code[10..12]);
        }
        else if (code.StartsWith("FLEVEL", StringComparison.Ordinal) && code.Length == 12 && code[10] == 'L')
        {
            SetRoom(code[6]);
            SetRow(code[7]);
            SetSection(code[8..10]);
            SetLevel(code[11]);
        }
    }

    private void SetRoom(char room)
    {
        if (char.IsAsciiDigit(room))
            _roomFrom = _roomTo = room - '0';
    }

    private void SetRow(char row) => _rowFrom = _rowTo = row.ToString();
    private void SetSection(string section)
    {
        if (int.TryParse(section, out var number))
            _sectionFrom = _sectionTo = number;
    }
    private void SetLevel(char level)
    {
        if (char.IsAsciiDigit(level))
            _levelFrom = _levelTo = level - '0';
    }

    private async Task<IEnumerable<Zone>> SearchZonesAsync(string? searchText, CancellationToken ct)
    {
        var result = await ZoneQueryService.ListAsync(new ZoneListQuery
        {
            SearchString = searchText,
            WarehouseId = Warehouse.Id,
            ExcludeDeleted = true,
            SortBy = "Name",
            Take = 50
        }, ct);
        return result.Items;
    }

    private static string ZoneText(Zone? zone) => zone is null ? string.Empty : $"{zone.Code} · {zone.Name}";

    private StorageLocationRange Range() => new(
        Warehouse.Id, _zone?.Id ?? Guid.Empty, Parent.Id,
        _roomFrom, _roomTo, _rowFrom, _rowTo,
        _sectionFrom, _sectionTo, _cellFrom, _cellTo,
        _levelFrom, _levelTo);

    private void InvalidatePreview() => _preview = null;

    private async Task PreviewAsync()
    {
        _busy = true;
        _error = null;
        InvalidatePreview();
        try
        {
            var result = await BatchService.PreviewAsync(Range());
            if (result.IsSuccess)
                _preview = result.Value;
            else
                _error = result.Error?.Message;
        }
        catch
        {
            _error = "Не удалось проверить диапазоны адресов.";
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task CreateAsync()
    {
        if (_preview?.CanCreate != true)
            return;
        _busy = true;
        _error = null;
        try
        {
            var result = await BatchService.CreateAsync(Range(), _preview.Signature);
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
            _error = "Не удалось создать ячейки. Повторите предпросмотр.";
            InvalidatePreview();
        }
        finally
        {
            _busy = false;
        }
    }

    private void Cancel() => MudDialog.Cancel();
}
