using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace PlumbobForge.Desktop.ViewModels;

// Singleton view models get their PKGManager and AppDbContext from the root scope, so that database session lives as long
// as the app and keeps the rows it once loaded. Work done in other scopes (set dirty flags, configuration item rules)
// never shows up there. Rebuilding and syncing must see the current database, so they run in a scope of their own.
internal static class FreshScope
{
    public static async Task RunAsync<T>(Func<T, Task> action) where T : notnull
    {
        var services = App.Services ?? throw new InvalidOperationException("Services are not available");
        using var scope = services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<T>());
    }
}
