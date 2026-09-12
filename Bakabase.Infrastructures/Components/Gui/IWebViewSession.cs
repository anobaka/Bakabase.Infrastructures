using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Infrastructures.Components.Gui;

/// <summary>
/// A handle to a live WebView window. The caller drives navigation, cookie ops, and the
/// confirm/cancel lifecycle directly. Replaces the prior god-method-style
/// CaptureWebViewCookiesAsync — flow-specific policy (chain rules, cookie mirrors, stale
/// markers, completion detection) lives in the calling Service layer, not in the GUI adapter.
/// All operations are safe to call from any thread; the implementation marshals to UI
/// thread internally where required.
/// </summary>
public interface IWebViewSession : IAsyncDisposable
{
    /// <summary>The URL currently displayed (or about to load). Null if nothing has loaded yet.</summary>
    string? CurrentUrl { get; }

    /// <summary>
    /// How this window identifies itself to the sites it visits.
    /// </summary>
    /// <remarks>
    /// Asked of the session rather than assumed by the caller, because whatever is done
    /// with a cookie afterwards has to be done as the browser that was given it. The
    /// default is what every platform host sets, so an implementation only overrides it
    /// if it genuinely presents itself as something else.
    /// </remarks>
    string UserAgent => WebViewUserAgent.ForThisPlatform;

    /// <summary>
    /// Register a handler invoked after each URL change. Handlers are awaited and
    /// serialized — the next URL change waits for all in-flight handlers to complete
    /// before firing, so chain-style logic doesn't race. Handler exceptions are caught
    /// and logged; one misbehaving handler can't crash the session or block subsequent
    /// navigations.
    /// </summary>
    void OnNavigated(Func<string, Task> handler);

    Task NavigateAsync(string url);

    /// <summary>Idempotent — silently no-ops if the cookie isn't present.</summary>
    Task DeleteCookieAsync(string url, string name);

    /// <summary>
    /// Copy named cookies from <paramref name="sourceUrl"/>'s cookie store onto
    /// <paramref name="targetDomain"/> (preserving Secure/HttpOnly/SameSite/Expires).
    /// Names not present on the source are silently skipped.
    /// </summary>
    Task MirrorCookiesAsync(string sourceUrl, string targetDomain, string[] cookieNames);

    /// <summary>
    /// Returns a `name=value; ...` cookie header string built from the cookie stores of
    /// the given URLs. URLs are processed in priority order — when the same name appears
    /// on multiple URLs, the first wins. Returns null if no cookies were found.
    /// </summary>
    Task<string?> GetCookiesAsync(string[] urls);

    /// <summary>
    /// Resolves when the user clicks Confirm.
    /// Throws <see cref="OperationCanceledException"/> if the user cancels (Cancel button or
    /// window close), if <paramref name="cancellationToken"/> fires, or if the session is
    /// disposed while waiting.
    /// </summary>
    Task WaitForUserConfirmAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates the status line shown in the window. Marshalled to UI thread internally.</summary>
    void SetStatusText(string text);
}

/// <summary>Static configuration for a new <see cref="IWebViewSession"/>.</summary>
public record WebViewSessionOptions
{
    public required string Title { get; init; }
    public string ConfirmButtonText { get; init; } = "Confirm";
    public string CancelButtonText { get; init; } = "Cancel";
    public string InitialStatusText { get; init; } = "";
    public string InitialUrl { get; init; } = "about:blank";
}
