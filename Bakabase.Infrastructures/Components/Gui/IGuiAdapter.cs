using System;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Bakabase.Infrastructures.Components.Gui
{
    public interface IGuiAdapter
    {
        void ShowFatalErrorWindow(string message, string title = "Fatal Error");
        void ShowInitializationWindow(string processName);
        void DestroyInitializationWindow();
        void ShowMainWebView([NotNull] string url, [NotNull] string title, Func<Task> onClosing);
        void SetMainWindowTitle(string title);
        bool MainWebViewVisible { get; }
        void Shutdown();
        void Hide();
        void Show();
        void ShowConfirmationDialogOnFirstTimeExiting(Func<CloseBehavior, bool, Task> onClosed);
        bool ShowConfirmDialog(string message, string caption);
        void ChangeUiTheme(UiTheme theme);

        /// <summary>
        ///
        /// </summary>
        /// <param name="type"></param>
        /// <param name="path">Must not be null or empty if <see cref="type"/> is <see cref="IconType.Dynamic"/></param>
        /// <returns></returns>
        byte[]? GetIcon(IconType type, string? path);

        /// <summary>
        /// Opens a WebView window for the user to log in to a third-party site, then extracts cookies.
        /// Returns null if the user cancels or cookie capture is not supported (e.g. non-desktop environment).
        /// </summary>
        /// <param name="loginUrl">The URL to navigate to for login.</param>
        /// <param name="title">Window title.</param>
        /// <param name="cookieUrls">URLs to extract cookies from after login.</param>
        /// <param name="onNavigated">Optional callback invoked when the WebView navigates to a new URL.
        /// Returns a tuple: (Done: true to auto-complete, NavigateToUrl: URL to navigate next or null to keep waiting).</param>
        /// <returns>Cookie header string, or null if cancelled/unsupported.</returns>
        Task<string?> CaptureWebViewCookiesAsync(string loginUrl, string title, string[] cookieUrls,
            Func<string, (bool Done, string? NavigateToUrl)>? onNavigated = null,
            Dictionary<string, string>? labels = null);
    }
}