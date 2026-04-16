using System;
using System.Collections.Generic;
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
        /// The user clicks Confirm when ready. Returns null if cancelled or unsupported.
        /// </summary>
        Task<string?> CaptureWebViewCookiesAsync(string loginUrl, string title, string[] cookieUrls,
            Dictionary<string, string>? labels = null);
    }
}