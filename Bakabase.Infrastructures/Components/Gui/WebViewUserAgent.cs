using System;

namespace Bakabase.Infrastructures.Components.Gui;

/// <summary>
/// What an embedded WebView presents itself as.
/// </summary>
/// <remarks>
/// <para>
/// One place, because this string is written twice and the two copies must agree: the
/// platform host sets it on the browser control, and the cookie a capture produces is
/// later replayed with it. A site that handed a session to one browser will often refuse
/// a request that presents it as another, so a drift between the two turns into a
/// sign-in that appears to work and then does not.
/// </para>
/// <para>
/// It is the host operating system's Chrome string rather than the running engine's. The
/// WebView is Chromium on all three platforms — WebView2, WKWebView, WebKitGTK — and the
/// platform is the part sites key off; the versions here also keep sites from refusing
/// the window as "browser too old".
/// </para>
/// </remarks>
public static class WebViewUserAgent
{
    public const string Windows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    public const string MacOS =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    public const string Linux =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    /// <summary>
    /// The string for the machine this process is running on.
    /// </summary>
    /// <remarks>
    /// Windows is the fallback rather than a fourth value: every remaining platform runs
    /// no WebView at all, and answering with something no browser sends would be worse
    /// than answering with the most common one.
    /// </remarks>
    public static string ForThisPlatform =>
        OperatingSystem.IsMacOS() ? MacOS : OperatingSystem.IsLinux() ? Linux : Windows;
}
