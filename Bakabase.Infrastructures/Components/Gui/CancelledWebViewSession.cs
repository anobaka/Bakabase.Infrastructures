using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Infrastructures.Components.Gui;

/// <summary>
/// Stub <see cref="IWebViewSession"/> for headless contexts (Service-only deployments,
/// tests that don't exercise the GUI path). All navigation / cookie ops are no-ops, and
/// <see cref="WaitForUserConfirmAsync"/> immediately throws OperationCanceledException —
/// equivalent to "no GUI available, treat as user-cancelled".
/// </summary>
public sealed class CancelledWebViewSession : IWebViewSession
{
    public static CancelledWebViewSession Instance { get; } = new();

    private CancelledWebViewSession() { }

    public string? CurrentUrl => null;

    public void OnNavigated(Func<string, Task> handler) { }

    public Task NavigateAsync(string url) => Task.CompletedTask;

    public Task DeleteCookieAsync(string url, string name) => Task.CompletedTask;

    public Task MirrorCookiesAsync(string sourceUrl, string targetDomain, string[] cookieNames) => Task.CompletedTask;

    public Task<string?> GetCookiesAsync(string[] urls) => Task.FromResult<string?>(null);

    public Task WaitForUserConfirmAsync(CancellationToken cancellationToken = default) =>
        Task.FromCanceled(new CancellationToken(canceled: true));

    public void SetStatusText(string text) { }

    public ValueTask DisposeAsync() => default;
}
