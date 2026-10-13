using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.ViewModels;
using PlumbobForge.Desktop.Views;
using PlumbobForge.Desktop.Views.Dialogs;
using PlumbobForge.Installer.Shared;

namespace PlumbobForge.Desktop;

public partial class App : Application
{
    private IServiceScope? _appScope;
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _ = Task.Run(EnsureInstallSafetyOnStartup);

        string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "plumbobforge-app");
        bool appSettingsExistedBefore = File.Exists(Path.Combine(appDataPath, "appsettings.json"));

        var services = new ServiceCollection();
        ConfigureServices(services);
        var rootProvider = services.BuildServiceProvider();

        _ = Task.Run(() =>
        {
            try
            {
                using var scope = rootProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                EnsureDatabaseSchema(db);
                EnsureDefaultSetAndConfig(db);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Background DB init error", ex);
            }
        });

        _appScope = rootProvider.CreateScope();
        Services = _appScope.ServiceProvider;

        var options = Services.GetRequiredService<IOptions<PlumbobForgeOptions>>().Value;

        if (!string.IsNullOrWhiteSpace(options.DocumentBaseDir))
        {
            AppLogger.SetLogsDirectory(Path.Combine(options.DocumentBaseDir, "Logs"));
            ItemViewModel.GlobalThumbnailDirectory = Path.Combine(options.DocumentBaseDir, "Thumbnails");
        }

        AppLogger.LogInfo($"PlumbobForge Desktop started (Theme: {options.Theme}, Lang: {options.Language}, Logs: {AppLogger.LogsDirectory})");

        ThemeService.Initialize(options.Theme, options.AccentColor);
        LocalizationManager.Instance.Initialize(options.Language);
        LocalizationService.SetExternalProvider((key, args) => LocalizationManager.Instance.GetString(key, args ?? Array.Empty<object>()));

        bool shouldShowUpgradeWizard = false;
        bool shouldShowWalkthrough = false;

        if (appSettingsExistedBefore)
        {
            if (!options.HasCompletedUpgradeWizard)
            {
                shouldShowUpgradeWizard = true;
            }
        }
        else
        {
            if (!options.HasCompletedUpgradeWizard)
            {
                options.HasCompletedUpgradeWizard = true;
                options.LastOptimizedCacheMilestone = MainViewModel.CurrentCacheOptimizationMilestone;
                _ = AppSettingsService.SaveOptionsAsync(options);
            }

            if (!options.HasSeenWalkthrough)
            {
                shouldShowWalkthrough = true;
            }
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainViewModel>();
            var mainWindow = new MainWindow
            {
                DataContext = mainVm
            };
            desktop.MainWindow = mainWindow;

            if (shouldShowUpgradeWizard)
            {
                mainWindow.Loaded += async (_, _) =>
                {
                    await DialogHelper.ShowUpgradeWizardAsync(Services.GetRequiredService<UpgradeWizardViewModel>(), mainWindow);
                };
            }
            else if (shouldShowWalkthrough)
            {
                mainWindow.Loaded += async (_, _) =>
                {
                    await DialogHelper.ShowNewUserWalkthroughAsync(Services.GetRequiredService<NewUserWalkthroughViewModel>(), mainWindow);
                };
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = Services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var unhashedSets = db.SetsEntities
                        .Include(s => s.MetaEntities)
                        .Where(s => s.CachedHash == null && !s.Dirty)
                        .ToList();

                    if (unhashedSets.Count > 0)
                    {
                        foreach (var set in unhashedSets)
                        {
                            set.CachedHash = SetDirtyTracker.ComputeContentHash(set);
                        }
                        db.SaveChanges();
                    }

                    await scope.ServiceProvider.GetRequiredService<PKGManager>().ScanLibraryDiskAsync();

                    var contentVm = Services?.GetService<ContentManagerViewModel>();
                    if (contentVm != null)
                    {
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await contentVm.LoadDataAsync();
                        });
                    }
                }
                catch
                {
                }
            });

            try
            {
                var watcher = Services.GetRequiredService<DownloadsWatcherService>();
                _ = Task.Run(() => watcher.StartAsync(CancellationToken.None));
            }
            catch
            {
            }

            desktop.ShutdownRequested += (_, _) =>
            {
                try
                {
                    Services?.GetService<DownloadsWatcherService>()?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                catch
                {
                }
                try
                {
                    Services?.GetService<ContentManagerViewModel>()?.SaveCurrentUiState(immediate: true);
                }
                catch
                {
                }
                _appScope?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void EnsureInstallSafetyOnStartup()
    {
        try
        {
            string appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!File.Exists(Path.Combine(appDir, "install-manifest.json")))
            {
                var files = new List<string>();
                if (Directory.Exists(appDir))
                {
                    foreach (var file in Directory.GetFiles(appDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (PathSafety.IsKnownAppFile(file, appDir))
                        {
                            files.Add(Path.GetFileName(file));
                        }
                    }
                    string localesDir = Path.Combine(appDir, "Assets", "Locales");
                    if (Directory.Exists(localesDir))
                    {
                        foreach (var localeFile in Directory.GetFiles(localesDir, "*.json"))
                        {
                            files.Add(Path.GetRelativePath(appDir, localeFile).Replace('\\', '/'));
                        }
                    }
                    files.Add("install-manifest.json");
                    InstallManifest.SaveAsync(appDir, files);
                }
            }

            string installedLocation = RegistryManager.GetInstalledLocation();
            if (!string.IsNullOrEmpty(installedLocation) && (PathSafety.IsForbiddenDirectory(installedLocation) || !PathSafety.IsDedicatedAppDirectory(installedLocation)))
            {
                RegistryManager.RegisterInstallation(appDir);
            }
        }
        catch (Exception)
        {
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "plumbobforge-app");
        Directory.CreateDirectory(appDataPath);

        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(appDataPath, "appsettings.json"), optional: true, reloadOnChange: true)
            .Build();

        services.Configure<PlumbobForgeOptions>(config.GetSection("PlumbobForge"));
        services.PostConfigure<PlumbobForgeOptions>(opts =>
        {
            if (string.IsNullOrEmpty(opts.DocumentBaseDir))
            {
                opts.DocumentBaseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "PlumbobForge");
            }
        });

        services.AddDbContext<AppDbContext>(options =>
        {
            string dbPath = Path.Combine(appDataPath, "plumbobforge.db");
            options.UseSqlite($"Data Source={dbPath}");
        });

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddProvider(new FileLoggerProvider());
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<UiStateService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<DownloadsWatcherService>();
        services.AddScoped<LocalizationService>();
        services.AddScoped<PackageTypeService>();
        services.AddScoped<ArchiveService>();
        services.AddScoped<ThumbnailService>();
        services.AddScoped<CacheBuilderService>();
        services.AddScoped<MigrationService>();
        services.AddScoped<PKGManager>();
        services.AddScoped<Sims3HealthService>();
        services.AddScoped<Sims3HiderService>();
        services.AddScoped<Sims3CollectionService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<QuickSwitcherViewModel>();
        services.AddSingleton<ContentManagerViewModel>();
        services.AddSingleton<ConfigurationsViewModel>();
        services.AddSingleton<HealthViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<UpgradeWizardViewModel>();
        services.AddTransient<NewUserWalkthroughViewModel>();
    }

    private static void EnsureDefaultSetAndConfig(AppDbContext db)
    {
        try
        {
            var defaultSet = db.SetsEntities.FirstOrDefault(s => s.Name == "Default");
            if (defaultSet == null)
            {
                defaultSet = new SetsEntity
                {
                    Name = "Default",
                    FolderName = "Default",
                    IsDefault = true
                };
                db.SetsEntities.Add(defaultSet);
                db.SaveChanges();
            }

            var defaultConfig = db.ConfigEntities
                .Include(c => c.ConfigSetsEntities)
                .FirstOrDefault(c => c.Name == "Default" || c.Default);

            if (defaultConfig != null) return;

            defaultConfig = new ConfigEntity
            {
                Name = "Default",
                Active = true,
                Default = true
            };
            db.ConfigEntities.Add(defaultConfig);
            db.SaveChanges();

            foreach (var set in db.SetsEntities.ToList())
            {
                if (!db.ConfigSetsEntities.Any(cs => cs.ConfigEntityId == defaultConfig.Id && cs.SetsEntityId == set.Id))
                {
                    db.ConfigSetsEntities.Add(new ConfigSetsEntity
                    {
                        ConfigEntityId = defaultConfig.Id,
                        SetsEntityId = set.Id
                    });
                }
            }
            db.SaveChanges();
        }
        catch
        {
        }
    }

    private static void EnsureDatabaseSchema(AppDbContext db)
    {
        try
        {
            db.Database.ExecuteSqlRaw("\r\nPRAGMA journal_mode = WAL;\r\nPRAGMA synchronous = NORMAL;\r\nPRAGMA temp_store = MEMORY;\r\nPRAGMA cache_size = -8000;\r\n");
        }
        catch
        {
        }

        int userVersion = 0;
        try
        {
            using var conn = db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                conn.Open();
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            var val = cmd.ExecuteScalar();
            if (val != null && val != DBNull.Value)
            {
                userVersion = Convert.ToInt32(val);
            }
        }
        catch
        {
        }

        if (userVersion >= 4) return;

        try
        {
            db.Database.EnsureCreated();
        }
        catch
        {
        }

        try
        {
            db.Database.ExecuteSqlRaw("\r\nCREATE TABLE IF NOT EXISTS Tombstones (\r\n    Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,\r\n    FileName TEXT NOT NULL,\r\n    PackageType TEXT NOT NULL DEFAULT '',\r\n    CASCategories TEXT,\r\n    CASAge TEXT,\r\n    CASGender TEXT,\r\n    CASOutfitCategory TEXT,\r\n    IsUserTagged INTEGER NOT NULL DEFAULT 0,\r\n    UserTags TEXT,\r\n    Description TEXT,\r\n    SetsEntityId INTEGER,\r\n    DeletedAt TEXT NOT NULL DEFAULT ''\r\n);");
        }
        catch
        {
        }

        try
        {
            db.Database.ExecuteSqlRaw("\r\nCREATE TABLE IF NOT EXISTS Collections (\r\n    Id INTEGER PRIMARY KEY NOT NULL,\r\n    Name TEXT NOT NULL,\r\n    IconType INTEGER NOT NULL,\r\n    IconGroup INTEGER NOT NULL,\r\n    IconInstance INTEGER NOT NULL,\r\n    Flags INTEGER NOT NULL,\r\n    IsPlumbobForge INTEGER NOT NULL DEFAULT 1,\r\n    HideFromCatalog INTEGER NOT NULL DEFAULT 0,\r\n    CreatedAt TEXT NOT NULL,\r\n    UpdatedAt TEXT NOT NULL\r\n);");
            db.Database.ExecuteSqlRaw("\r\nCREATE TABLE IF NOT EXISTS CollectionSets (\r\n    Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,\r\n    CollectionEntityId INTEGER NOT NULL,\r\n    SetsEntityId INTEGER NOT NULL,\r\n    FOREIGN KEY (CollectionEntityId) REFERENCES Collections(Id) ON DELETE CASCADE,\r\n    FOREIGN KEY (SetsEntityId) REFERENCES SetsEntities(Id) ON DELETE CASCADE\r\n);");
        }
        catch
        {
        }

        string[] migrations = new string[41]
        {
            "ALTER TABLE Tombstones ADD COLUMN PackageType TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Tombstones ADD COLUMN CASCategories TEXT;",
            "ALTER TABLE Tombstones ADD COLUMN CASAge TEXT;",
            "ALTER TABLE Tombstones ADD COLUMN CASGender TEXT;",
            "ALTER TABLE Tombstones ADD COLUMN CASOutfitCategory TEXT;",
            "ALTER TABLE Tombstones ADD COLUMN IsUserTagged INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Tombstones ADD COLUMN UserTags TEXT;",
            "ALTER TABLE Tombstones ADD COLUMN Description TEXT;",
            "ALTER TABLE Tombstones ADD COLUMN SetsEntityId INTEGER;",
            "ALTER TABLE Tombstones ADD COLUMN DeletedAt TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE SetsEntities ADD COLUMN Icon TEXT;",
            "ALTER TABLE SetsEntities ADD COLUMN Color TEXT;",
            "ALTER TABLE SetsEntities ADD COLUMN CachedHash TEXT;",
            "ALTER TABLE SetsEntities ADD COLUMN Description TEXT;",
            "ALTER TABLE SetsEntities ADD COLUMN FolderName TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE SetsEntities ADD COLUMN LongName TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE SetsEntities ADD COLUMN IsLegacy INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE SetsEntities ADD COLUMN IsExpanded INTEGER NOT NULL DEFAULT 1;",
            "ALTER TABLE SetsEntities ADD COLUMN IsDefault INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE ConfigEntities ADD COLUMN Icon TEXT;",
            "ALTER TABLE ConfigEntities ADD COLUMN Color TEXT;",
            "ALTER TABLE ConfigEntities ADD COLUMN Description TEXT;",
            "ALTER TABLE MetaEntities ADD COLUMN Description TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE MetaEntities ADD COLUMN IsUserTagged INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE MetaEntities ADD COLUMN UserTags TEXT;",
            "ALTER TABLE MetaEntities ADD COLUMN CASCategories TEXT;",
            "ALTER TABLE MetaEntities ADD COLUMN CASAge TEXT;",
            "ALTER TABLE MetaEntities ADD COLUMN CASGender TEXT;",
            "ALTER TABLE MetaEntities ADD COLUMN CASOutfitCategory TEXT;",
            "ALTER TABLE MetaEntities ADD COLUMN CompleteFileName TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE MetaEntities ADD COLUMN FileType TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE MetaEntities ADD COLUMN FileSize REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE MetaEntities ADD COLUMN PackageType TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE MetaEntities ADD COLUMN ResourceID TEXT;",
            "ALTER TABLE MetaEntities ADD COLUMN ThumbnailID TEXT;",
            "ALTER TABLE MetaEntities ADD COLUMN InstallDate TEXT;",
            "ALTER TABLE MetaEntities ADD COLUMN Manifest TEXT;",
            "ALTER TABLE MetaEntities ADD COLUMN URL TEXT;",
            "ALTER TABLE Collections ADD COLUMN HideFromCatalog INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE MetaEntities ADD COLUMN IsFavorite INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Tombstones ADD COLUMN IsFavorite INTEGER NOT NULL DEFAULT 0;"
        };

        foreach (var sql in migrations)
        {
            try
            {
                db.Database.ExecuteSqlRaw(sql);
            }
            catch
            {
            }
        }

        try
        {
            db.Database.ExecuteSqlRaw("\r\n                DELETE FROM MetaEntities \r\n                WHERE Id NOT IN (\r\n                    SELECT MIN(Id) FROM MetaEntities GROUP BY LOWER(FileName)\r\n                );");
        }
        catch
        {
        }

        try
        {
            db.Database.ExecuteSqlRaw($"PRAGMA user_version = 4;");
        }
        catch
        {
        }
    }
}
