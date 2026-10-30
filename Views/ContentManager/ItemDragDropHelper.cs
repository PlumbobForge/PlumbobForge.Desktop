using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.ContentManager;

public static class ItemDragDropHelper
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    private static string DragTempRoot => Path.Combine(Path.GetTempPath(), "PlumbobForge", "DragDropExport");

    private static void CleanUpPreviousDrags()
    {
        try
        {
            if (Directory.Exists(DragTempRoot)) Directory.Delete(DragTempRoot, true);
        }
        catch { }
    }


    private static string? LinkOrCopy(string sourcePath, string dragDir)
    {
        try
        {
            Directory.CreateDirectory(dragDir);
            string target = Path.Combine(dragDir, Path.GetFileName(sourcePath));
            if (File.Exists(target)) File.Delete(target);
            if (!(OperatingSystem.IsWindows() && CreateHardLink(target, sourcePath, IntPtr.Zero)))
            {
                File.Copy(sourcePath, target, overwrite: true);
            }
            return target;
        }
        catch
        {
            return null;
        }
    }

    public static async Task StartItemDragAsync(
        Visual sourceVisual,
        PointerEventArgs e,
        ItemViewModel itemVm,
        ContentManagerViewModel? cmVm)
    {
        var itemsToDrag = new List<ItemViewModel>();
        if (itemVm.IsSelected && cmVm != null)
        {
            var selected = cmVm.Items.Where(i => i.IsSelected).ToList();
            if (selected.Count > 0)
            {
                itemsToDrag.AddRange(selected);
            }
            else
            {
                itemsToDrag.Add(itemVm);
            }
        }
        else
        {
            itemsToDrag.Add(itemVm);
        }

        CleanUpPreviousDrags();

        var vaultService = App.Services?.GetService<VaultService>();
        var options = App.Services?.GetService<IOptionsMonitor<PlumbobForgeOptions>>()?.CurrentValue;

        var validFilePaths = new List<string>();

        foreach (var item in itemsToDrag)
        {
            // One folder per item so a multi-part Sims3Pack export and same-named items do not collide.
            string itemDir = Path.Combine(DragTempRoot, item.Entity.Id.ToString());

            if (item.Entity.IsVaultManaged && vaultService != null)
            {
                // Vault-managed mod: reconstruct standalone .package on the fly
                try
                {
                    Directory.CreateDirectory(itemDir);
                    string exportFileName = Path.ChangeExtension(item.Entity.FileName, ".package");
                    string tempPath = Path.Combine(itemDir, exportFileName);
                    if (await vaultService.ExportModAsync(item.Entity.Id, tempPath))
                    {
                        validFilePaths.AddRange(Directory.GetFiles(itemDir));
                    }
                }
                catch { }
            }
            else
            {
                string filePath = item.Entity.CompleteFileName;
                if (!File.Exists(filePath) && options != null)
                {
                    var fallback = Path.Combine(options.ManagedPackageFolderPath, item.Entity.FileName);
                    if (File.Exists(fallback)) filePath = fallback;
                }

                if (File.Exists(filePath))
                {
                    string? dragPath = LinkOrCopy(filePath, itemDir);
                    if (dragPath != null) validFilePaths.Add(dragPath);
                }
            }
        }

        var data = new DataObject();
        // Maintain internal drag payload so dragging onto Sets in PlumbobForge still moves items
        data.Set("plumbob-item-drag", "move");

        // Populate file drop formats so Windows Explorer, Desktop, or external folders can receive the package
        var topLevel = TopLevel.GetTopLevel(sourceVisual);
        if (topLevel?.StorageProvider != null && validFilePaths.Count > 0)
        {
            var storageFiles = new List<IStorageItem>();
            foreach (var path in validFilePaths)
            {
                try
                {
                    var sf = await topLevel.StorageProvider.TryGetFileFromPathAsync(path);
                    if (sf != null) storageFiles.Add(sf);
                }
                catch { }
            }

            if (storageFiles.Count > 0)
            {
                data.Set(DataFormats.Files, storageFiles);
            }
        }

        if (validFilePaths.Count > 0)
        {
            data.Set("FileDrop", validFilePaths.ToArray());
        }

        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Move);
    }
}
