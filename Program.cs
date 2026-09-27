using System;
using System.IO;
using Avalonia;
using PlumbobForge.Backend.Services;

namespace PlumbobForge.Desktop;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things may break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
        {
            try
            {
                var requestedName = new System.Reflection.AssemblyName(resolveArgs.Name).Name;
                if (string.IsNullOrEmpty(requestedName)) return null;

                string baseDir = AppContext.BaseDirectory;
                string directPath = Path.Combine(baseDir, requestedName + ".dll");
                if (File.Exists(directPath)) return System.Reflection.Assembly.LoadFrom(directPath);

                string libsDir = Path.Combine(baseDir, "libs");
                string libPath = Path.Combine(libsDir, requestedName + ".dll");
                if (File.Exists(libPath)) return System.Reflection.Assembly.LoadFrom(libPath);
            }
            catch { }
            return null;
        };

        AppLogger.Initialize();

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            AppLogger.LogFatal("FATAL UNHANDLED", e.ExceptionObject as Exception);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            AppLogger.LogFatal("UNOBSERVED TASK", e.Exception);
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLogger.LogFatal("FATAL MAIN", ex);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = 64 * 1024 * 1024 // 64 MB GPU Texture Cache
            })
            .WithInterFont()
            .LogToTrace();
}
