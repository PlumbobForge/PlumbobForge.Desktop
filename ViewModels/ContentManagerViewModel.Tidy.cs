using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

// Tidy by Type: the loose files of a Packages folder sorted into a folder per kind (CAS, Build/Buy, Patterns).
// The folders always go straight under Packages, because Resource.cfg loads Packages/*.package and
// Packages/*/*.package and nothing deeper. Script mods, core mods and anything else unknown stay where they are.
public partial class ContentManagerViewModel
{
    public sealed record TidyGroup(string Kind, string Label, string DefaultFolder, List<ItemViewModel> Items);

    public sealed record TidyChoice(string Kind, string Folder, List<long> ItemIds);

    private static string? TidyKind(ItemViewModel item)
    {
        string type = item.PackageType ?? string.Empty;
        if (PackageTypeHelper.IsCas(type)) return "cas";
        if (PackageTypeHelper.IsBuildBuy(type)) return "buildbuy";
        if (PackageTypeHelper.IsPattern(type)) return "patterns";
        return null;
    }

    private ModsFolderEntity? PackagesFolder() =>
        _modsFolders.FirstOrDefault(f => string.Equals(f.RelativePath, ModsFolderService.DefaultImportFolder, StringComparison.OrdinalIgnoreCase));

    // Packages itself, or a folder right under it. Other roots (Overrides, say) load in their own order, so
    // their files are left alone.
    public bool CanTidy(SetNodeViewModel node)
    {
        var folder = node.IsModsRoot ? PackagesFolder() : node.ModsFolder;
        if (folder == null) return false;
        string packages = ModsFolderService.DefaultImportFolder;
        string path = folder.RelativePath.Replace('\\', '/');
        return string.Equals(path, packages, StringComparison.OrdinalIgnoreCase)
               || (path.StartsWith(packages + "/", StringComparison.OrdinalIgnoreCase) && !path[(packages.Length + 1)..].Contains('/'));
    }

    // The files lying directly in the folder, by kind; a kind whose files already sit in its folder is left out.
    public List<TidyGroup> PlanTidy(SetNodeViewModel node)
    {
        var folder = node.IsModsRoot ? PackagesFolder() : node.ModsFolder;
        if (folder == null || !CanTidy(node)) return new();
        var loose = Items.Where(i => i.Entity.IsModsItem && i.Entity.ModsFolderId == folder.Id).ToList();
        string folderName = Path.GetFileName(folder.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        var groups = new List<TidyGroup>();
        foreach (var (kind, labelKey, folderKey) in new[]
                 {
                     ("cas", "library.tidy_cas", "library.tidy_folder_cas"),
                     ("buildbuy", "library.tidy_buildbuy", "library.tidy_folder_buildbuy"),
                     ("patterns", "library.tidy_patterns", "library.tidy_folder_patterns"),
                 })
        {
            string target = L(folderKey);
            if (string.Equals(folderName, target, StringComparison.OrdinalIgnoreCase)) continue;
            var items = loose.Where(i => TidyKind(i) == kind).OrderBy(i => i.FileName, StringComparer.OrdinalIgnoreCase).ToList();
            if (items.Count > 0) groups.Add(new TidyGroup(kind, L(labelKey), target, items));
        }
        return groups;
    }

    // A folder name the move can use: one level, nothing the file system rejects.
    public static bool IsValidTidyFolder(string? name)
    {
        string n = name?.Trim() ?? string.Empty;
        return n.Length > 0 && n.Trim('.').Length > 0 && n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !n.Contains('/') && !n.Contains('\\');
    }

    public async Task TidyByTypeAsync(SetNodeViewModel node)
    {
        var groups = PlanTidy(node);
        if (groups.Count == 0)
        {
            ShowToast(L("library.tidy_nothing"));
            return;
        }
        var choices = await DialogHelper.ShowTidyByTypeAsync(groups);
        if (choices == null || choices.Count == 0) return;
        await ApplyTidyAsync(choices);
    }

    public async Task ApplyTidyAsync(IReadOnlyList<TidyChoice> choices)
    {
        var packages = PackagesFolder();
        if (packages == null) return;
        var moving = Items.Where(i => choices.Any(c => c.ItemIds.Contains(i.Entity.Id))).ToList();
        var previous = moving.Where(i => i.Entity.ModsFolderId.HasValue)
            .GroupBy(i => i.Entity.ModsFolderId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(i => i.Entity.Id).ToList());

        int done = 0;
        var skipped = new List<ModsFolderService.Skipped>();
        IsLoading = true;
        try
        {
            long packagesId = packages.Id;
            string packagesPath = packages.RelativePath;
            (done, skipped) = await Task.Run(async () =>
            {
                using var scope = App.Services!.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ModsFolderService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                int count = 0;
                var skips = new List<ModsFolderService.Skipped>();
                // Same folder name twice (CAS and Build/Buy both into "CC", say) is one folder.
                foreach (var group in choices.GroupBy(c => c.Folder.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    string name = group.Key;
                    string wanted = packagesPath + "/" + name;
                    var existing = await db.ModsFolders.AsNoTracking().FirstOrDefaultAsync(f => f.RelativePath.ToLower() == wanted.ToLower());
                    if (existing == null)
                    {
                        string? created = await service.CreateFolderAsync(packagesId, name);
                        if (created != null) existing = await db.ModsFolders.AsNoTracking().FirstOrDefaultAsync(f => f.RelativePath.ToLower() == created.ToLower());
                    }
                    if (existing == null)
                    {
                        AppLogger.LogWarning($"Tidy by Type: could not make folder {wanted}", null, "ContentManagerViewModel");
                        continue;
                    }
                    var result = await service.MoveToFolderAsync(group.SelectMany(c => c.ItemIds).ToList(), existing.Id);
                    count += result.Done;
                    skips.AddRange(result.Skipped);
                }
                return (count, skips);
            });
        }
        finally
        {
            IsLoading = false;
        }
        await LoadDataAsync();
        NotifyDirtyStateChanged();

        Func<Task> undo = async () =>
        {
            foreach (var (folderId, back) in previous)
            {
                var target = ModsFolderNodes().FirstOrDefault(n => n.ModsFolder!.Id == folderId);
                if (target != null) await RunOnItemsAsync(back, () => MoveSelectedToModsFolderAsync(target));
            }
        };
        ShowToast(string.Format(L("library.tidy_done"), done), done > 0 ? undo : null);
        await ShowModsSkippedAsync(new ModsFolderService.ActionResult(done, skipped));
    }
}
