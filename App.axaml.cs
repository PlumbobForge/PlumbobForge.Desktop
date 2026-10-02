using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
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

public class App : Application
{
    public static IServiceProvider? Services { get; private set; }
    private IServiceScope? _appScope;

    public override void Initialize()
    {
        
        
        AvaloniaXamlLoader.Load(this);
        
    }

    public override void OnFrameworkInitializationCompleted()
    {
        
        
        // 1. Kick off filesystem & registry safety validation asynchronously so startup is non-blocking
        Task.Run(EnsureInstallSafetyOnStartup);

        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "plumbobforge-app");
        var dbPath = Path.Combine(appDataPath, "plumbobforge.db");
        var appSettingsPath = Path.Combine(appDataPath, "appsettings.json");

        bool appSettingsExistedBefore = File.Exists(appSettingsPath);

        var services = new ServiceCollection();
        ConfigureServices(services);
        var rootProvider = services.BuildServiceProvider();

        // 2. Run DB schema check and default seeding in background so UI opens instantly
        Task.Run(() =>
        {
            try
            {
                using var initScope = rootProvider.CreateScope();
                var db = initScope.ServiceProvider.GetRequiredService<AppDbContext>();
                EnsureDatabaseSchema(db);
                EnsureDefaultSetAndConfig(db);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Background DB init error", ex);
            }
        });
        // 3. Create a long-lived application scope so scoped services resolve correctly
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
                    var wizardVm = Services.GetRequiredService<UpgradeWizardViewModel>();
                    await DialogHelper.ShowUpgradeWizardAsync(wizardVm, mainWindow);
                };
            }
            else if (shouldShowWalkthrough)
            {
                mainWindow.Loaded += async (_, _) =>
                {
                    var walkthroughVm = Services.GetRequiredService<NewUserWalkthroughViewModel>();
                    await DialogHelper.ShowNewUserWalkthroughAsync(walkthroughVm, mainWindow);
                };
            }

            // 4. Run background maintenance, clean set hashing, library scanning, and file watching asynchronously
            Task.Run(async () =>
            {
                try
                {
                    using var scope = Services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // Initialize CachedHash for clean sets in background (doesn't delay UI)
                    var unhashedCleanSets = db.SetsEntities
                        .Include(s => s.MetaEntities)
                        .Where(s => s.CachedHash == null && !s.Dirty)
                        .ToList();

                    if (unhashedCleanSets.Count > 0)
                    {
                        foreach (var s in unhashedCleanSets)
                        {
                            s.CachedHash = SetDirtyTracker.ComputeContentHash(s);
                        }
                        db.SaveChanges();
                    }

                    var pkgManager = scope.ServiceProvider.GetRequiredService<PKGManager>();
                    await pkgManager.ScanLibraryDiskAsync();
                }
                catch { }
            });

            try
            {
                var watcher = Services.GetRequiredService<DownloadsWatcherService>();
                Task.Run(() => watcher.StartAsync(CancellationToken.None));
            }
            catch { }

            desktop.ShutdownRequested += (_, _) =>
            {
                try
                {
                    Services?.GetService<DownloadsWatcherService>()?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                catch { }

                try
                {
                    Services?.GetService<ContentManagerViewModel>()?.SaveCurrentUiState(immediate: true);
                }
                catch { }

                _appScope?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void EnsureInstallSafetyOnStartup()
    {
        try
        {
            var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var manifestPath = Path.Combine(appDir, InstallerConstants.InstallManifestFileName);

            // If manifest does not exist yet (e.g. legacy install prior to 1.0.1),
            // auto-generate it from recognized application components.
            if (!File.Exists(manifestPath))
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

                    var localesDir = Path.Combine(appDir, "Assets", "Locales");
                    if (Directory.Exists(localesDir))
                    {
                        foreach (var f in Directory.GetFiles(localesDir, "*.json"))
                        {
                            files.Add(Path.GetRelativePath(appDir, f).Replace('\\', '/'));
                        }
                    }

                    files.Add(InstallerConstants.InstallManifestFileName);
                    _ = InstallManifest.SaveAsync(appDir, files);
                }
            }

            // Heal registry location if it points directly to a system directory like Program Files
            var regLocation = RegistryManager.GetInstalledLocation();
            if (!string.IsNullOrEmpty(regLocation))
            {
                if (PathSafety.IsForbiddenDirectory(regLocation) || !PathSafety.IsDedicatedAppDirectory(regLocation))
                {
                    RegistryManager.RegisterInstallation(appDir);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Startup] EnsureInstallSafetyOnStartup failed: {ex.Message}");
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "plumbobforge-app");
        Directory.CreateDirectory(appDataPath);

        var configBuilder = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(appDataPath, "appsettings.json"), optional: true, reloadOnChange: true);

        var configuration = configBuilder.Build();
        services.Configure<PlumbobForgeOptions>(configuration.GetSection("PlumbobForge"));

        services.PostConfigure<PlumbobForgeOptions>(opts =>
        {
            if (string.IsNullOrEmpty(opts.DocumentBaseDir))
            {
                opts.DocumentBaseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "PlumbobForge");
            }
        });

        services.AddDbContext<AppDbContext>(options =>
        {
            var dbFile = Path.Combine(appDataPath, "plumbobforge.db");
            options.UseSqlite($"Data Source={dbFile}");
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

    private const int CurrentDbSchemaVersion = 4;

    private static void EnsureDefaultSetAndConfig(AppDbContext db)
    {
        try
        {
            var defaultSet = db.SetsEntities.FirstOrDefault(s => s.Name == "Default");
            if (defaultSet == null)
            {
                defaultSet = new SetsEntity { Name = "Default", FolderName = "Default", IsDefault = true };
                db.SetsEntities.Add(defaultSet);
                db.SaveChanges();
            }

            var defaultConfig = db.ConfigEntities.Include(c => c.ConfigSetsEntities).FirstOrDefault(c => c.Name == "Default" || c.Default);
            if (defaultConfig == null)
            {
                defaultConfig = new ConfigEntity { Name = "Default", Active = true, Default = true };
                db.ConfigEntities.Add(defaultConfig);
                db.SaveChanges();

                var allSets = db.SetsEntities.ToList();
                foreach (var set in allSets)
                {
                    if (!db.ConfigSetsEntities.Any(cs => cs.ConfigEntityId == defaultConfig.Id && cs.SetsEntityId == set.Id))
                    {
                        db.ConfigSetsEntities.Add(new ConfigSetsEntity { ConfigEntityId = defaultConfig.Id, SetsEntityId = set.Id });
                    }
                }
                db.SaveChanges();
            }
        }
        catch { }
    }

    private static void EnsureDatabaseSchema(AppDbContext db)
    {
        try
        {
            db.Database.ExecuteSqlRaw(@"
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA temp_store = MEMORY;
PRAGMA cache_size = -8000;
");
        }
        catch { }

        int userVersion = 0;
        try
        {
            using var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                conn.Open();
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            var res = cmd.ExecuteScalar();
            if (res != null && res != DBNull.Value)
            {
                userVersion = Convert.ToInt32(res);
            }
        }
        catch { }

        // If schema is up to date, skip table creation and the 43+ ALTER TABLE queries entirely!
        if (userVersion >= CurrentDbSchemaVersion)
        {
            return;
        }

        try
        {
            db.Database.EnsureCreated();
        }
        catch { }

        try
        {
            db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS Tombstones (
    Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    FileName TEXT NOT NULL,
    PackageType TEXT NOT NULL DEFAULT '',
    CASCategories TEXT,
    CASAge TEXT,
    CASGender TEXT,
    CASOutfitCategory TEXT,
    IsUserTagged INTEGER NOT NULL DEFAULT 0,
    UserTags TEXT,
    Description TEXT,
    SetsEntityId INTEGER,
    DeletedAt TEXT NOT NULL DEFAULT ''
);");
        }
        catch { }

        try
        {
            db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS Collections (
    Id INTEGER PRIMARY KEY NOT NULL,
    Name TEXT NOT NULL,
    IconType INTEGER NOT NULL,
    IconGroup INTEGER NOT NULL,
    IconInstance INTEGER NOT NULL,
    Flags INTEGER NOT NULL,
    IsPlumbobForge INTEGER NOT NULL DEFAULT 1,
    HideFromCatalog INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);");

            db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS CollectionSets (
    Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    CollectionEntityId INTEGER NOT NULL,
    SetsEntityId INTEGER NOT NULL,
    FOREIGN KEY (CollectionEntityId) REFERENCES Collections(Id) ON DELETE CASCADE,
    FOREIGN KEY (SetsEntityId) REFERENCES SetsEntities(Id) ON DELETE CASCADE
);");
        }
        catch { }

        var alterQueries = new[]
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

        foreach (var sql in alterQueries)
        {
            try
            {
                db.Database.ExecuteSqlRaw(sql);
            }
            catch { }
        }

        // Fast SQL deduplication for duplicate MetaEntities if any exist
        try
        {
            db.Database.ExecuteSqlRaw(@"
                DELETE FROM MetaEntities 
                WHERE Id NOT IN (
                    SELECT MIN(Id) FROM MetaEntities GROUP BY LOWER(FileName)
                );");
        }
        catch { }

        try
        {
            db.Database.ExecuteSqlRaw($"PRAGMA user_version = {CurrentDbSchemaVersion};");
        }
        catch { }
    }
}
