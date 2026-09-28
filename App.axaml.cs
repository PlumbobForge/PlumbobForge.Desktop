using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.ViewModels;
using PlumbobForge.Desktop.Views;
using PlumbobForge.Desktop.Views.Dialogs;

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
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "plumbobforge-app");
        var dbPath = Path.Combine(appDataPath, "plumbobforge.db");
        var appSettingsPath = Path.Combine(appDataPath, "appsettings.json");

        bool dbExistedBefore = File.Exists(dbPath);
        bool appSettingsExistedBefore = File.Exists(appSettingsPath);

        var services = new ServiceCollection();
        ConfigureServices(services);
        var rootProvider = services.BuildServiceProvider();

        // Initialize SQLite DB using a temporary scope
        using (var initScope = rootProvider.CreateScope())
        {
            var db = initScope.ServiceProvider.GetRequiredService<AppDbContext>();
            EnsureDatabaseSchema(db);

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

            try
            {
                // Initialize CachedHash for clean sets that don't have one yet
                var unhashedCleanSets = db.SetsEntities
                    .Include(s => s.MetaEntities)
                    .Where(s => s.CachedHash == null && !s.Dirty)
                    .ToList();

                if (unhashedCleanSets.Count > 0)
                {
                    foreach (var s in unhashedCleanSets)
                    {
                        s.CachedHash = PlumbobForge.Backend.Services.SetDirtyTracker.ComputeContentHash(s);
                    }
                    db.SaveChanges();
                }
            }
            catch { }
        }

        // Create a long-lived application scope so scoped services
        // (AppDbContext, ThumbnailService, IOptionsSnapshot, etc.) resolve correctly
        _appScope = rootProvider.CreateScope();
        Services = _appScope.ServiceProvider;

        var options = Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlumbobForgeOptions>>().Value;
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
        bool shouldShowNewUserWalkthrough = false;

        var defaultDocDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PlumbobForge");
        bool hasExistingLibrary = Directory.Exists(Path.Combine(defaultDocDir, "Library")) && Directory.GetFiles(Path.Combine(defaultDocDir, "Library")).Length > 0;

        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Ensure schema updates on startup
            try
            {
                db.Database.ExecuteSqlRaw("ALTER TABLE Tombstones ADD COLUMN Description TEXT;");
            }
            catch { }
            try
            {
                db.Database.ExecuteSqlRaw("ALTER TABLE Tombstones ADD COLUMN IsUserTagged INTEGER NOT NULL DEFAULT 0;");
            }
            catch { }
            try
            {
                db.Database.ExecuteSqlRaw("ALTER TABLE MetaEntities ADD COLUMN Description TEXT NOT NULL DEFAULT '';");
            }
            catch { }

            // Automatically clean up any existing phantom case-mismatch duplicates
            try
            {
                var allMetas = db.MetaEntities.ToList();
                var grouped = allMetas.GroupBy(m => m.FileName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1);
                bool hasDuplicates = false;
                foreach (var group in grouped)
                {
                    var primary = group.OrderByDescending(m => m.SetsEntityId != null && m.SetsEntityId != 1 ? 1 : 0)
                                       .ThenByDescending(m => !string.IsNullOrEmpty(m.Description))
                                       .First();
                    foreach (var duplicate in group)
                    {
                        if (duplicate.Id != primary.Id)
                        {
                            db.MetaEntities.Remove(duplicate);
                            hasDuplicates = true;
                        }
                    }
                }
                if (hasDuplicates)
                {
                    db.SaveChanges();
                }
            }
            catch { }

            if (appSettingsExistedBefore)
            {
                if (!options.HasCompletedUpgradeWizard)
                {
                    shouldShowUpgradeWizard = true;
                }
            }
            else
            {
                // Fresh clean install: new users skip upgrade wizard, but see walkthrough if not seen yet
                if (!options.HasCompletedUpgradeWizard)
                {
                    options.HasCompletedUpgradeWizard = true;
                    _ = AppSettingsService.SaveOptionsAsync(options);
                }

                if (!options.HasSeenWalkthrough)
                {
                    shouldShowNewUserWalkthrough = true;
                }
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
            else if (shouldShowNewUserWalkthrough)
            {
                mainWindow.Loaded += async (_, _) =>
                {
                    var walkthroughVm = Services.GetRequiredService<NewUserWalkthroughViewModel>();
                    await DialogHelper.ShowNewUserWalkthroughAsync(walkthroughVm, mainWindow);
                };
            }

            // Initial background library scan to pick up any restored or external files
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = Services.CreateScope();
                    var pkgManager = scope.ServiceProvider.GetRequiredService<PKGManager>();
                    await pkgManager.ScanLibraryDiskAsync();
                }
                catch { }
            });

            // Start background watchers for Downloads and Library folders
            try
            {
                var watcherService = Services.GetRequiredService<DownloadsWatcherService>();
                _ = watcherService.StartAsync(CancellationToken.None);
            }
            catch { }

            desktop.ShutdownRequested += (_, _) =>
            {
                try
                {
                    var watcherService = Services?.GetService<DownloadsWatcherService>();
                    watcherService?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                catch { }

                try
                {
                    var contentVm = Services?.GetService<ContentManagerViewModel>();
                    contentVm?.SaveCurrentUiState(immediate: true);
                }
                catch { }
                _appScope?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "plumbobforge-app");
        Directory.CreateDirectory(appDataPath);

        // Load configuration from the same appsettings.json the Electron backend uses
        var appSettingsPath = Path.Combine(appDataPath, "appsettings.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(appSettingsPath, optional: true, reloadOnChange: true)
            .Build();

        // Bind PlumbobForgeOptions from config section, with fallback
        services.Configure<PlumbobForgeOptions>(configuration.GetSection(PlumbobForgeOptions.SectionName));
        services.PostConfigure<PlumbobForgeOptions>(opts =>
        {
            if (string.IsNullOrEmpty(opts.DocumentBaseDir))
            {
                opts.DocumentBaseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PlumbobForge");
            }
        });

        // Database — use the same DB file as the Electron backend
        services.AddDbContext<AppDbContext>(options =>
        {
            var dbPath = Path.Combine(appDataPath, "plumbobforge.db");
            options.UseSqlite($"Data Source={dbPath}");
        });

        // Logging & Notification
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddProvider(new FileLoggerProvider());
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Backend & Desktop services
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

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<QuickSwitcherViewModel>();
        services.AddSingleton<ContentManagerViewModel>();
        services.AddSingleton<ConfigurationsViewModel>();
        services.AddSingleton<HealthViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<UpgradeWizardViewModel>();
        services.AddTransient<NewUserWalkthroughViewModel>();
    }

    private static void EnsureDatabaseSchema(AppDbContext db)
    {
        try
        {
            db.Database.EnsureCreated();
        }
        catch { }

        // Create Tombstones table if missing
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

        // Create Collections table if missing
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

        // Ensure all possible columns exist across all tables
        string[] alterQueries = new[]
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

            "ALTER TABLE ConfigEntities ADD COLUMN Icon TEXT;",
            "ALTER TABLE ConfigEntities ADD COLUMN Color TEXT;",
            "ALTER TABLE ConfigEntities ADD COLUMN Description TEXT;",

            "ALTER TABLE MetaEntities ADD COLUMN Description TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE MetaEntities ADD COLUMN IsUserTagged INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE MetaEntities ADD COLUMN UserTags TEXT;"
        };

        foreach (var sql in alterQueries)
        {
            try
            {
                db.Database.ExecuteSqlRaw(sql);
            }
            catch
            {
                // Column already exists or table not applicable
            }
        }
    }
}
