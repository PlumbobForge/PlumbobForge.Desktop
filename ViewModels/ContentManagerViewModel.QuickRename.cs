using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Services;

namespace PlumbobForge.Desktop.ViewModels;

// Inline rename in the library sidebar: the name turns into a text box, Enter or clicking away saves,
// Esc cancels. A name that can't be used keeps the box open with the reason; the rules are Customize's.
public partial class ContentManagerViewModel
{
    public void StartQuickRename(SetNodeViewModel node)
    {
        if (!node.CanQuickRename) return;
        foreach (var other in AllSidebarNodes().Where(n => n.IsRenaming && !ReferenceEquals(n, node))) CancelQuickRename(other);
        node.RenameText = node.Name;
        node.RenameError = null;
        node.IsRenaming = true;
    }

    public void CancelQuickRename(SetNodeViewModel node)
    {
        node.IsRenaming = false;
        node.RenameError = null;
    }

    // True when the box can close (saved, or nothing to save); false keeps it open with RenameError set.
    public async Task<bool> CommitQuickRenameAsync(SetNodeViewModel node)
    {
        if (!node.IsRenaming || node.IsRenameSaving) return true;
        string newName = node.RenameText.Trim();
        if (newName.Length == 0)
        {
            node.RenameError = L(node.IsModsFolder ? "dialogs.customize_set.name_empty_folder" : "dialogs.customize_set.name_empty");
            return false;
        }
        if (newName == node.Name)
        {
            CancelQuickRename(node);
            return true;
        }
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newName.Trim('.').Length == 0)
        {
            node.RenameError = L("library.rename_invalid");
            return false;
        }

        node.IsRenameSaving = true;
        try
        {
            string oldName = node.Name;
            long? setId = node.Id;
            long? folderId = node.ModsFolder?.Id;
            string? smartId = node.SmartSetId;
            string? error = node.IsSmartSet ? RenameSmartSetInline(node, newName)
                : node.IsModsFolder ? await RenameModsFolderInlineAsync(node, newName) : await RenameSetInlineAsync(node, newName);
            if (error != null)
            {
                node.RenameError = error;
                return false;
            }
            node.IsRenaming = false;
            // A set renamed only in case is left as it was (like Customize), so there is nothing to report.
            if (folderId.HasValue || smartId != null || !newName.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                ShowToast(string.Format(L("library.toast_renamed"), newName), () => RenameBackAsync(setId, folderId, oldName, smartId));
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Inline rename failed", ex, "ContentManagerViewModel");
            node.RenameError = ex.Message;
            return false;
        }
        finally
        {
            node.IsRenameSaving = false;
        }
    }

    private async Task<string?> RenameSetInlineAsync(SetNodeViewModel node, string newName)
    {
        if (!node.Id.HasValue || node.IsDefault) return null;
        var setEntity = await _db.SetsEntities.FindAsync(node.Id.Value);
        if (setEntity == null) return null;
        // Customize treats a change of case only as no change; so does this.
        if (newName.Equals(setEntity.Name, StringComparison.OrdinalIgnoreCase)) return null;

        bool duplicate = await _db.SetsEntities.AnyAsync(s => s.Id != setEntity.Id && s.ParentSetsEntityId == setEntity.ParentSetsEntityId && s.Name.ToLower() == newName.ToLower());
        if (duplicate) return string.Format(L(setEntity.ParentSetsEntityId.HasValue ? "library.rename_duplicate_subset" : "library.rename_duplicate_set"), newName);

        await SetNewSetNameAsync(setEntity, newName);
        await _db.Entry(setEntity).Collection(s => s.MetaEntities).LoadAsync();
        SetDirtyTracker.UpdateDirtyState(setEntity);
        await _db.SaveChangesAsync();
        node.IsRenaming = false;
        // Renamed in place rather than rebuilding the tree, so a click that ended the rename (on another set,
        // say) isn't undone by the rebuild, and expanded sets stay expanded.
        node.Name = newName;
        if (FlatSets.FirstOrDefault(s => s.Id == setEntity.Id) is { } flat)
        {
            flat.Name = newName;
            flat.FolderName = newName;
            flat.LongName = newName;
        }
        SortSetsTree();
        ApplyFilter();
        NotifyDirtyStateChanged();
        return null;
    }

    private async Task<string?> RenameModsFolderInlineAsync(SetNodeViewModel node, string newName)
    {
        if (!node.CanRenameModsFolder || node.ModsFolder == null) return null;
        var parent = AllSidebarNodes().FirstOrDefault(n => n.Children.Contains(node));
        bool sibling = parent != null && parent.Children.Any(c => !ReferenceEquals(c, node) && string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase));
        if (sibling) return string.Format(L("library.rename_duplicate_folder"), newName);

        bool ok;
        using (var scope = App.Services!.CreateScope())
        {
            ok = await scope.ServiceProvider.GetRequiredService<ModsFolderService>().RenameFolderAsync(node.ModsFolder.Id, newName);
        }
        if (!ok) return string.Format(L("library.mods_rename_folder_failed"), newName);
        node.IsRenaming = false;
        // Item paths under the folder changed on disk, so reload like New Folder and Delete do.
        await LoadDataAsync();
        return null;
    }

    // Undo for an inline rename: the same rename, back to the old name (the node may be a new one after a reload).
    private async Task RenameBackAsync(long? setId, long? folderId, string oldName, string? smartId = null)
    {
        var node = AllSidebarNodes().FirstOrDefault(n => (setId.HasValue && n.Id == setId) || (folderId.HasValue && n.ModsFolder?.Id == folderId)
                                                          || (smartId != null && n.SmartSetId == smartId));
        if (node == null || !node.CanQuickRename) return;
        StartQuickRename(node);
        node.RenameText = oldName;
        if (!await CommitQuickRenameAsync(node)) CancelQuickRename(node);
    }

    public SetNodeViewModel? RenamingNode() => AllSidebarNodes().FirstOrDefault(n => n.IsRenaming);

    public System.Collections.Generic.IEnumerable<SetNodeViewModel> AllSidebarNodes()
    {
        foreach (var root in RootSets)
        {
            yield return root;
            foreach (var d in root.GetAllDescendants()) yield return d;
        }
    }
}
