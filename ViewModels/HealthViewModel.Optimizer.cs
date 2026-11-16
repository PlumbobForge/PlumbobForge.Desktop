using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Utils;
using PlumbobForge.Desktop.Views.Dialogs;
using S3ForgeTools.GameFiles.Package;

namespace PlumbobForge.Desktop.ViewModels;

public partial class HealthViewModel
{
    public CcOptimizerViewModel Optimizer { get; } = new();

    // Counted like the optimizer list: each CAS part, and each package's objects as one.
    public int VeryHeavyCount => OptimizerEntries(OptimizerRows.Where(r => r.IsVeryHeavy));
    public int HeavyCount => OptimizerEntries(OptimizerRows.Where(r => r.IsHeavyOrWorse));

    private IEnumerable<MeshReportRowViewModel> OptimizerRows => MeshKindFilter switch
    {
        MeshKindCas => _allMeshRows.Where(r => r.IsCas),
        MeshKindBuildBuy => _allMeshRows.Where(r => r.IsBuildBuy),
        _ => _allMeshRows,
    };
    public bool HasHeavy => HeavyCount > 0;
    public string OptimizeVeryHeavyText => LocalizationManager.Instance.GetString("tools.meshes.optimize_very_heavy", VeryHeavyCount);
    public string OptimizeHeavyText => LocalizationManager.Instance.GetString("tools.meshes.optimize_heavy", HeavyCount);

    private static int OptimizerEntries(IEnumerable<MeshReportRowViewModel> rows)
    {
        var list = rows.ToList();
        return list.Count(r => r.IsCas) + list.Where(r => !r.IsCas).Select(r => r.Model.MetaEntityId).Distinct().Count();
    }

    private void RaiseOptimizerCounts()
    {
        OnPropertyChanged(nameof(VeryHeavyCount));
        OnPropertyChanged(nameof(HeavyCount));
        OnPropertyChanged(nameof(HasHeavy));
        OnPropertyChanged(nameof(OptimizeVeryHeavyText));
        OnPropertyChanged(nameof(OptimizeHeavyText));
    }

    private void InitOptimizer()
    {
        Optimizer.ItemsSaved += ids =>
        {
            if (!HasScannedMeshes) return;
            foreach (long id in ids) _ = RescanMeshItemAsync(id);
        };
        Optimizer.AddItemsHandler = AddItemsToOptimizerAsync;
        LocalizationManager.Instance.LanguageChanged += _ => RaiseOptimizerCounts();
        _ = PruneMeshBackupsAsync();
    }

    // Removes backups of deleted items.
    private async Task PruneMeshBackupsAsync()
    {
        try
        {
            await Task.Run(async () =>
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ids = (await db.MetaEntities.AsNoTracking().Select(m => m.Id).ToListAsync()).ToHashSet();
                if (ids.Count > 0) MeshBackupStore.Prune(ids);
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("Could not clean up CC Optimizer backups", ex, "HealthViewModel");
        }
    }

    public void OpenInOptimizer(IEnumerable<MeshReportRow> rows)
    {
        var list = rows.ToList();
        var objectItems = list.Where(r => !r.IsCas).Select(r => r.MetaEntityId).ToHashSet();
        var withPackages = list.Where(r => r.IsCas)
            .Concat(_allMeshRows.Select(r => r.Model).Where(r => !r.IsCas && objectItems.Contains(r.MetaEntityId)))
            .Concat(list.Where(r => !r.IsCas));
        Optimizer.AddRows(withPackages);
        SelectSection("Optimizer");
    }

    public async Task OpenInOptimizerAsync(IReadOnlyCollection<long> itemIds, ISet<(long ItemId, TGI_Key Key)>? only = null)
    {
        SelectSection("Optimizer");
        await Optimizer.AddItemsAsync(itemIds, only);
    }

    [RelayCommand]
    private void OptimizeVeryHeavy()
    {
        var rows = OptimizerRows.Where(r => r.IsVeryHeavy).ToList();
        if (rows.Count == 0) rows = OptimizerRows.Where(r => r.IsHeavyOrWorse).ToList();
        OpenInOptimizer(rows.Select(r => r.Model));
    }

    [RelayCommand]
    private void OptimizeHeavy() => OpenInOptimizer(OptimizerRows.Where(r => r.IsHeavyOrWorse).Select(r => r.Model));

    private async Task AddItemsToOptimizerAsync()
    {
        try
        {
            List<SetsEntity> sets;
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                sets = await db.SetsEntities.AsNoTracking().ToListAsync();
            }
            var existing = Optimizer.Items.Select(i => i.ItemId).Distinct().ToDictionary(id => id, _ => true);
            var picked = await DialogHelper.ShowConfigItemPickerAsync(true, string.Empty, sets, existing, forOptimizer: true);
            if (picked.Count > 0) await Optimizer.AddItemsAsync(picked);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Could not add items to the CC optimizer", ex, "HealthViewModel");
            ShowCacheToast(ex.Message, isError: true);
        }
    }
}
