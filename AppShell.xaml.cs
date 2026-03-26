using System.Threading;
using Microsoft.Maui.ApplicationModel;

namespace BaseLogApp;

public partial class AppShell : Shell
{
    private readonly SemaphoreSlim _moreResetLock = new(1, 1);
    private int _moreResetRequestId;
    private INavigation? _lastKnownMoreNavigation;

    public AppShell()
    {
        InitializeComponent();
        Navigated += OnShellNavigated;
    }

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        if (IsMoreRoute(e.Current))
            _lastKnownMoreNavigation = CurrentPage?.Navigation ?? _lastKnownMoreNavigation;

        if (e.Source is ShellNavigationSource.Push or ShellNavigationSource.Pop)
            return;

        var isMoreCurrent = IsMoreRoute(e.Current);
        if (!isMoreCurrent && !HasMoreStackToReset())
            return;

        var requestId = Interlocked.Increment(ref _moreResetRequestId);
        MainThread.BeginInvokeOnMainThread(() => _ = ResetMoreTabToRootAsync(requestId));
    }

    private async Task ResetMoreTabToRootAsync(int requestId)
    {
        // Let the current tab transition finish before touching another navigation stack.
        await Task.Delay(120);
        await _moreResetLock.WaitAsync();

        try
        {
            if (requestId != _moreResetRequestId)
                return;

            var nav = ResolveMoreNavigation();
            if (nav?.NavigationStack is null || nav.NavigationStack.Count <= 1)
                return;

            for (var attempt = 0; attempt < 4; attempt++)
            {
                if (requestId != _moreResetRequestId)
                    return;

                try
                {
                    await nav.PopToRootAsync(false);
                    return;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Pending Navigations", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(120);
                }
            }
        }
        catch
        {
            // Never crash app flow for background stack normalization.
        }
        finally
        {
            _moreResetLock.Release();
        }
    }

    private static bool IsMoreRoute(ShellNavigationState state)
    {
        var route = state?.Location?.OriginalString ?? string.Empty;
        return route.Contains("/more", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasMoreStackToReset()
    {
        var nav = ResolveMoreNavigation();
        return nav?.NavigationStack?.Count > 1;
    }

    private INavigation? ResolveMoreNavigation()
    {
        if (IsMoreRoute(Current?.CurrentState ?? CurrentState))
            return CurrentPage?.Navigation ?? _lastKnownMoreNavigation;

        return _lastKnownMoreNavigation ?? MoreTab?.Navigation;
    }
}
