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
        PlumbobForge.Desktop.Controls.SmoothScrolling.Install();
        _ = Task.Run(EnsureInstallSafetyOnStartup);

        string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "plumbobforge-app");
        bool appSettingsExistedBefore = File.Exists(Path.Combine(appDataPath, "appsettings.json"));

        var services = new ServiceCollection();
        ConfigureServices(services);
        var rootProvider = services.BuildServiceProvider();

        // Schema first and synchronously: queries made during startup select the newest columns. It is a single
        // PRAGMA read once the database is up to date.
        try
        {
            using var schemaScope = rootProvider.CreateScope();
            EnsureDatabaseSchema(schemaScope.ServiceProvider.GetRequiredService<AppDbContext>());
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Database schema upgrade error", ex);
        }

        _ = Task.Run(() =>
        {
            try
            {
                using var scope = rootProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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

        ItemViewModel.TextureOptimizationActive = options.OptimizeTextures;

        AppLogger.LogInfo($"PlumbobForge Desktop started (Theme: {options.Theme}, Lang: {options.Language}, Logs: {AppLogger.LogsDirectory})");

        ThemeService.Initialize(
            options.Theme,
            options.AccentColor,
            options.IsCustomTheme || string.Equals(options.Theme, "Custom", StringComparison.OrdinalIgnoreCase),
            options.CustomThemeBaseColor,
            options.CustomThemeIsDark,
            options.UseCustomAccent,
            options.CustomThemeAccentColor,
            options.CustomThemeIntensity
        );
        CustomColorPresetsService.Initialize(options);
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

                    if (!options.EnableSingleSourceVault && (db.VaultShards.Any() || db.MetaEntities.Any(m => m.IsVaultManaged)))
                    {
                        options.EnableSingleSourceVault = true;
                        await AppSettingsService.SaveOptionsAsync(options);
                    }

                    var cacheBuilder = scope.ServiceProvider.GetRequiredService<CacheBuilderService>();
                    if (db.VaultShards.Any())
                    {
                        // Finish/roll back interrupted compactions, trim interrupted writes, re-derive mismatched indexes.
                        bool layoutChanged = await scope.ServiceProvider.GetRequiredService<VaultService>().CheckAndRecoverAllShardsAsync();
                        if (layoutChanged)
                        {
                            Services.GetRequiredService<IOptionsMonitor<PlumbobForgeOptions>>().CurrentValue.EnableSingleSourceVault = true;
                            await cacheBuilder.SyncToSims3Async();
                        }
                    }

                    if (Services.GetRequiredService<IOptionsMonitor<PlumbobForgeOptions>>().CurrentValue.EnableSingleSourceVault)
                    {
                        // Lots, Sims and worlds older versions put into shards go back to the library, and items a
                        // migration could not move are retried. Then deploy lots/Sims/worlds and refresh Resource.cfg.
                        await scope.ServiceProvider.GetRequiredService<VaultService>().ExtractLibraryOnlyItemsAsync();
                        var leftovers = await scope.ServiceProvider.GetRequiredService<MigrationService>().MigrateRemainingLibraryItemsAsync();
                        foreach (var failure in leftovers.Failures)
                        {
                            AppLogger.LogWarning($"Library item still not in the vault: {failure}", null, "Startup");
                        }
                        await cacheBuilder.SyncToSims3Async();
                    }
                    else
                    {
                        // Set caches move from the old name-based folders to Set_{id}; a set whose cache is missing is
                        // marked for rebuild. Resource.cfg has to follow the moved folders.
                        if (await cacheBuilder.UpgradeSetCacheFoldersAsync())
                        {
                            await cacheBuilder.SyncToSims3Async();
                        }
                    }

                    // Mods changed while the game was running last session: clear its caches now that it has exited.
                    scope.ServiceProvider.GetRequiredService<CacheBuilderService>().ProcessPendingGameCacheClear();

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

            _ = Task.Run(WatchForGameExitAsync);

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

            string? installedLocation = RegistryManager.GetInstalledLocation();
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
        services.AddScoped<PackageInspectorService>();
        services.AddScoped<PackageEditService>();
        services.AddScoped<CasFlagsService>();
        services.AddScoped<CacheBuilderService>();
        services.AddScoped<MigrationService>();
        services.AddScoped<PKGManager>();
        services.AddScoped<VaultService>();
        services.AddScoped<ConfigItemOverrideService>();
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

    private static async Task WatchForGameExitAsync()
    {
        bool wasRunning = Sims3GameProcess.IsGameRunning();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        while (await timer.WaitForNextTickAsync())
        {
            bool running = Sims3GameProcess.IsGameRunning();
            if ((wasRunning && !running) || (!running && VaultService.HasPendingIndexSync))
            {
                try
                {
                    using var scope = Services!.CreateScope();
                    var opts = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<PlumbobForgeOptions>>().CurrentValue;
                    if (opts.EnableSingleSourceVault)
                    {
                        await scope.ServiceProvider.GetRequiredService<VaultService>().PunchPendingHolesAsync();
                    }
                    scope.ServiceProvider.GetRequiredService<CacheBuilderService>().ProcessPendingGameCacheClear();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Finishing deferred work after the game exited failed", ex);
                }
            }
            wasRunning = running;
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

        if (userVersion < 5) ApplySchemaV5(db);
        if (userVersion < 6) ApplySchemaV6(db);
        if (userVersion < 7) ApplySchemaV7(db);
        if (userVersion < 8) ApplySchemaV8(db);
        if (userVersion < 9) ApplySchemaV9(db);
        if (userVersion < 10) ApplySchemaV10(db);
        if (userVersion < 11) ApplySchemaV11(db);
    }

    private static void ApplySchemaV11(AppDbContext db)
    {
        try
        {
            db.Database.ExecuteSqlRaw(Sims3CollectionService.CreateCollectionItemsTableSql);
            db.Database.ExecuteSqlRaw("PRAGMA user_version = 11;");
        }
        catch
        {
        }
    }

    private static void ApplySchemaV10(AppDbContext db)
    {
        string[] migrations =
        {
            "ALTER TABLE MetaEntities ADD COLUMN HasConfigOverrides INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE VaultShards ADD COLUMN IndexScope TEXT;",
            @"
CREATE TABLE IF NOT EXISTS ConfigItemOverrides (
    Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    ConfigEntityId INTEGER NOT NULL,
    MetaEntityId INTEGER NOT NULL,
    Include INTEGER NOT NULL,
    FOREIGN KEY (ConfigEntityId) REFERENCES ConfigEntities(Id) ON DELETE CASCADE,
    FOREIGN KEY (MetaEntityId) REFERENCES MetaEntities(Id) ON DELETE CASCADE
);",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ConfigItemOverrides_ConfigEntityId_MetaEntityId ON ConfigItemOverrides (ConfigEntityId, MetaEntityId);",
            "CREATE INDEX IF NOT EXISTS IX_ConfigItemOverrides_MetaEntityId ON ConfigItemOverrides (MetaEntityId);"
        };

        foreach (var sql in migrations)
        {
            try
            {
                db.Database.ExecuteSqlRaw(sql);
            }
            catch
            {
                // Column already exists (fresh database created from the model).
            }
        }

        try
        {
            db.Database.ExecuteSqlRaw("PRAGMA user_version = 10;");
        }
        catch
        {
        }
    }

    private static void ApplySchemaV9(AppDbContext db)
    {
        string[] migrations =
        {
            "ALTER TABLE MetaEntities ADD COLUMN DisabledObjectCount INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE MetaEntities ADD COLUMN DisabledObjectsStamp TEXT;",
            @"
CREATE TABLE IF NOT EXISTS DisabledObjects (
    Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    MetaEntityId INTEGER NOT NULL,
    Type INTEGER NOT NULL,
    ""Group"" INTEGER NOT NULL,
    Instance INTEGER NOT NULL,
    FOREIGN KEY (MetaEntityId) REFERENCES MetaEntities(Id) ON DELETE CASCADE
);",
            "CREATE INDEX IF NOT EXISTS IX_DisabledObjects_MetaEntityId ON DisabledObjects (MetaEntityId);"
        };

        foreach (var sql in migrations)
        {
            try
            {
                db.Database.ExecuteSqlRaw(sql);
            }
            catch
            {
                // Column already exists (fresh database created from the model).
            }
        }

        try
        {
            db.Database.ExecuteSqlRaw("PRAGMA user_version = 9;");
        }
        catch
        {
        }
    }

    private static void ApplySchemaV8(AppDbContext db)
    {
        string[] migrations =
        {
            "ALTER TABLE MetaEntities ADD COLUMN BBKind TEXT;",
            "ALTER TABLE MetaEntities ADD COLUMN BBRooms TEXT;",
            "ALTER TABLE MetaEntities ADD COLUMN BBFunctions TEXT;",
            "ALTER TABLE MetaEntities ADD COLUMN BBPriceMin REAL;",
            "ALTER TABLE MetaEntities ADD COLUMN BBPriceMax REAL;"
        };

        foreach (var sql in migrations)
        {
            try
            {
                db.Database.ExecuteSqlRaw(sql);
            }
            catch
            {
                // Column already exists (fresh database created from the model).
            }
        }

        try
        {
            db.Database.ExecuteSqlRaw("PRAGMA user_version = 8;");
        }
        catch
        {
        }
    }

    private static void ApplySchemaV7(AppDbContext db)
    {
        try
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE VaultChunks ADD COLUMN OptimizedResourceLength INTEGER NOT NULL DEFAULT 0;");
        }
        catch
        {
            // Column already exists (fresh database created from the model).
        }

        try
        {
            db.Database.ExecuteSqlRaw("PRAGMA user_version = 7;");
        }
        catch
        {
        }
    }

    private static void ApplySchemaV6(AppDbContext db)
    {
        string[] migrations =
        {
            "ALTER TABLE MetaEntities ADD COLUMN SkipTextureOptimization INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE VaultChunks ADD COLUMN OptimizedOffset INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE VaultChunks ADD COLUMN OptimizedLength INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE VaultChunks ADD COLUMN OptimizedProfile INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE VaultHoles ADD COLUMN IsPending INTEGER NOT NULL DEFAULT 0;"
        };

        foreach (var sql in migrations)
        {
            try
            {
                db.Database.ExecuteSqlRaw(sql);
            }
            catch
            {
                // Column already exists (fresh database created from the model).
            }
        }

        try
        {
            db.Database.ExecuteSqlRaw("PRAGMA user_version = 6;");
        }
        catch
        {
        }
    }

    private static void ApplySchemaV5(AppDbContext db)
    {
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

                try
        {
            db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS VaultShards (
    Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    SetsEntityId INTEGER NOT NULL,
    ShardIndex INTEGER NOT NULL,
    RelativePath TEXT NOT NULL,
    FileSize INTEGER NOT NULL DEFAULT 0,
    DeadHoleBytes INTEGER NOT NULL DEFAULT 0,
    HeaderIndexOffset INTEGER NOT NULL DEFAULT 0,
    HeaderIndexSize INTEGER NOT NULL DEFAULT 0,
    ResourceCount INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (SetsEntityId) REFERENCES SetsEntities(Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_VaultShards_SetsEntityId_ShardIndex ON VaultShards (SetsEntityId, ShardIndex);

CREATE TABLE IF NOT EXISTS VaultChunks (
    Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    MetaEntityId INTEGER NOT NULL,
    VaultShardId INTEGER NOT NULL,
    Type INTEGER NOT NULL,
    ""Group"" INTEGER NOT NULL,
    Instance INTEGER NOT NULL,
    Offset INTEGER NOT NULL,
    ChunkLength INTEGER NOT NULL,
    ResourceLength INTEGER NOT NULL,
    IsCompressed INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (MetaEntityId) REFERENCES MetaEntities(Id) ON DELETE CASCADE,
    FOREIGN KEY (VaultShardId) REFERENCES VaultShards(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_VaultChunks_TGI ON VaultChunks (Type, ""Group"", Instance);
CREATE INDEX IF NOT EXISTS IX_VaultChunks_MetaEntityId ON VaultChunks (MetaEntityId);
CREATE INDEX IF NOT EXISTS IX_VaultChunks_VaultShardId ON VaultChunks (VaultShardId);

CREATE TABLE IF NOT EXISTS VaultHoles (
    Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    VaultShardId INTEGER NOT NULL,
    Offset INTEGER NOT NULL,
    Length INTEGER NOT NULL,
    FOREIGN KEY (VaultShardId) REFERENCES VaultShards(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_VaultHoles_VaultShardId ON VaultHoles (VaultShardId);
");
        }
        catch
        {
        }

        string[] migrations = new string[44]
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
            "ALTER TABLE Tombstones ADD COLUMN IsFavorite INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE MetaEntities ADD COLUMN IsVaultManaged INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE MetaEntities ADD COLUMN VaultShardId INTEGER;",
            "ALTER TABLE MetaEntities ADD COLUMN ParentMetaEntityId INTEGER;"
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
            db.Database.ExecuteSqlRaw($"PRAGMA user_version = 5;");
        }
        catch
        {
        }
    }
}
