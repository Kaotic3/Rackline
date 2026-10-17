namespace Rackline.Web.Components.Shared;

/// <summary>
/// Interactive pages live for as long as the browser tab, so each action gets its own short-lived
/// scope (and DbContext) instead of holding one open with stale, tracked data.
/// </summary>
public static class ScopedWork
{
    public static async Task<TResult> RunAsync<TService, TResult>(this IServiceScopeFactory scopes, Func<TService, Task<TResult>> work)
        where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public static async Task RunAsync<TService>(this IServiceScopeFactory scopes, Func<TService, Task> work)
        where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        await work(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
