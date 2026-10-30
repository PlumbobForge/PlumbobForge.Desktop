using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services.Localization;
using S3ForgeTools.GameFiles.Resources;

namespace PlumbobForge.Desktop.ViewModels;

public partial class CasFlagsBatchViewModel : ObservableObject, IDisposable
{
    private readonly IReadOnlyList<long> _itemIds;
    private readonly CancellationTokenSource _cts = new();

    private static string L(string key, params object[] args) => LocalizationManager.Instance.GetString(key, args);

    public bool IsVault { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(CanApply))]
    private bool _isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(CanApply))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    private bool _isDone;

    public bool ChangesSaved { get; private set; }
    public bool HasCasParts { get; private set; }
    public bool HasNoCasParts => !IsLoading && !HasCasParts;
    public bool IsReady => !IsLoading && HasCasParts;
    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public bool HasMixed => Options.Any(o => o.IsMixed);
    public string NoteText => L(IsVault ? "dialogs.cas_flags.note_vault" : "dialogs.cas_flags.note_library");

    public List<CasFlagBatchOption> OutfitOptions { get; } = new();
    public CasFlagBatchOption RandomOption { get; }
    private IEnumerable<CasFlagBatchOption> Options => OutfitOptions.Append(RandomOption);

    public bool CanApply => CanEditFlags && Options.Any(o => o.IsChanged);
    public bool CanEditFlags => IsReady && !IsSaving && !IsDone;
    public string CloseButtonText => L(IsDone ? "common.close" : "common.cancel");

    partial void OnIsSavingChanged(bool value) => OnPropertyChanged(nameof(CanEditFlags));

    partial void OnIsDoneChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditFlags));
        OnPropertyChanged(nameof(CloseButtonText));
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanEditFlags));

    public CasFlagsBatchViewModel(IReadOnlyList<long> itemIds, bool isVault)
    {
        _itemIds = itemIds;
        IsVault = isVault;
        foreach (var (key, bit) in CasFlagsService.OutfitBits)
        {
            OutfitOptions.Add(new CasFlagBatchOption(LocalizationManager.Instance.GetString($"filters.outfits.{key}"), bit));
        }
        RandomOption = new CasFlagBatchOption(L("dialogs.cas_flags.valid_for_random"), CasCategoryFlags.ValidForRandom);
        foreach (var option in Options) option.Changed += () => OnPropertyChanged(nameof(CanApply));
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            using var scope = App.Services?.CreateScope();
            var service = scope?.ServiceProvider.GetRequiredService<CasFlagsService>()
                ?? throw new InvalidOperationException("Services are not available");
            var summary = await Task.Run(() => service.SummarizeAsync(_itemIds, _cts.Token));

            HasCasParts = summary.PartCount > 0;
            SummaryText = L("dialogs.cas_flags.summary", summary.PartCount, summary.CasItemCount);
            foreach (var option in Options)
            {
                option.Initialize(summary.PartsWithBit.GetValueOrDefault(option.Bit), summary.PartCount);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Could not read CAS flags of the selected items", ex, "CasFlagsBatchViewModel");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasCasParts));
            OnPropertyChanged(nameof(HasNoCasParts));
            OnPropertyChanged(nameof(IsReady));
            OnPropertyChanged(nameof(HasMixed));
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public async Task ApplyAsync()
    {
        if (!CanApply) return;
        if (Sims3GameProcess.IsGameRunning())
        {
            StatusMessage = L("dialogs.cas_flags.game_running");
            return;
        }

        uint set = 0, clear = 0;
        foreach (var option in Options.Where(o => o.IsChanged))
        {
            if (option.State == true) set |= option.Bit;
            else if (option.State == false) clear |= option.Bit;
        }

        IsSaving = true;
        var progress = new Progress<(int Done, int Total)>(p => StatusMessage = L("dialogs.cas_flags.saving", p.Done, p.Total));
        StatusMessage = L("dialogs.cas_flags.saving", 0, _itemIds.Count);
        try
        {
            using var scope = App.Services?.CreateScope();
            var service = scope?.ServiceProvider.GetRequiredService<CasFlagsService>()
                ?? throw new InvalidOperationException("Services are not available");
            var result = await Task.Run(() => service.ApplyToItemsAsync(_itemIds, set, clear, progress, _cts.Token));

            ChangesSaved = result.ItemsChanged > 0;
            string message = result.StoppedByGame ? L("dialogs.cas_flags.stopped_game", result.PartsChanged, result.ItemsChanged)
                : result.Failures.Count > 0 ? L("dialogs.cas_flags.failed", result.Failures.Count, $"{result.Failures[0].FileName}: {result.Failures[0].Error}")
                : result.PartsChanged > 0 ? L("dialogs.cas_flags.done", result.PartsChanged, result.ItemsChanged)
                : L("dialogs.cas_flags.done_none");
            if (result.SkippedParts > 0) message += " " + L("dialogs.cas_flags.skipped_duplicates", result.SkippedParts);
            StatusMessage = message;
            IsDone = true;
        }
        catch (GameRunningException)
        {
            StatusMessage = L("dialogs.cas_flags.game_running");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Could not change CAS flags of the selected items", ex, "CasFlagsBatchViewModel");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    public void Dispose() => _cts.Cancel();
}


public sealed partial class CasFlagBatchOption : ObservableObject
{
    public string Label { get; }
    public uint Bit { get; }

    private bool? _initial;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    private bool? _state;

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    private bool _isMixed;

    public bool IsChanged => State != _initial;

    public event Action? Changed;

    public CasFlagBatchOption(string label, uint bit)
    {
        Label = label;
        Bit = bit;
    }

    public void Initialize(int withBit, int total)
    {
        _initial = withBit == 0 ? false : withBit == total ? true : null;
        IsMixed = _initial == null;
        State = _initial;
        CountText = LocalizationManager.Instance.GetString("dialogs.cas_flags.count", withBit, total);
    }

    partial void OnStateChanged(bool? value) => Changed?.Invoke();
}
