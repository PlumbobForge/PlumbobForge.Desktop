using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

public abstract partial class ConfigColumnEntry : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public abstract bool IsInEnabledColumn { get; }
    public abstract string SortName { get; }
    public abstract string SearchText { get; }
}

public sealed partial class ConfigItemCardViewModel : ConfigColumnEntry
{
    private readonly ItemThumbnail _thumbnail;

    public long ItemId { get; }
    public long RuleId { get; }
    public string FileName { get; }
    public string Location { get; }
    public bool Include { get; }

    public bool HasLocation => !string.IsNullOrEmpty(Location);
    public override bool IsInEnabledColumn => Include;
    public override string SortName => FileName;
    public override string SearchText => $"{FileName} {Location}";
    public string RuleTooltip => LocalizationManager.Instance.GetString(Include ? "configurations.item_included_tip" : "configurations.item_excluded_tip");

    public Bitmap? Thumbnail => _thumbnail.Get();

    public ConfigItemCardViewModel(long itemId, long ruleId, string fileName, string completeFileName, string location, bool include)
    {
        ItemId = itemId;
        RuleId = ruleId;
        FileName = fileName;
        Location = location;
        Include = include;
        _thumbnail = new ItemThumbnail(itemId, () => OnPropertyChanged(nameof(Thumbnail)));
    }
}

public sealed class ItemThumbnail
{
    private readonly long _id;
    private readonly Action _onLoaded;
    private bool _loading;
    private bool _none;
    // Set when a load just finished: the next miss must not start another load, or a full cache would loop.
    private bool _justLoaded;

    public ItemThumbnail(long id, Action onLoaded)
    {
        _id = id;
        _onLoaded = onLoaded;
    }

    public Bitmap? Get()
    {
        if (_none) return null;
        var bmp = ThumbnailCache.TryGet(_id);
        if (bmp != null) return bmp;
        if (_justLoaded)
        {
            // Evicted again before it was shown; wait until the card is shown anew.
            _justLoaded = false;
            return null;
        }
        if (!_loading) _ = LoadAsync();
        return null;
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var dir = ItemViewModel.GlobalThumbnailDirectory;
            if (string.IsNullOrEmpty(dir)) { _none = true; return; }
            var path = Path.Combine(dir, $"{_id}.thumb");
            if (!await Task.Run(() => File.Exists(path))) { _none = true; return; }

            if (await ThumbnailCache.GetOrLoadAsync(_id, path) != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _justLoaded = true;
                    _onLoaded();
                });
            }
            else
            {
                _none = true;
            }
        }
        catch
        {
            _none = true;
        }
        finally
        {
            _loading = false;
        }
    }
}
