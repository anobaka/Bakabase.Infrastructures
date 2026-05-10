using System;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Bakabase.Infrastructures.Components.Gui
{
    public interface IGuiAdapter
    {
        void ShowFatalErrorWindow(string message, string title = "Fatal Error");

        /// <summary>
        /// Show the boot splash with an updated phase. Optionally pass a sub-line of detail
        /// (e.g. the migrator currently running) and a determinate <paramref name="fraction"/>
        /// in [0, 1] when the caller actually has progress to report; leave both null for the
        /// default indeterminate appearance.
        /// </summary>
        void ShowInitializationWindow(string processName, string? detail = null, double? fraction = null);

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
        /// Opens a WebView window and returns a session handle the caller drives directly.
        /// All navigation, cookie operations, and confirm/cancel signalling happen via the
        /// returned <see cref="IWebViewSession"/> — the GUI adapter is intentionally policy-free.
        /// Headless or non-GUI contexts return a <see cref="CancelledWebViewSession"/> whose
        /// <see cref="IWebViewSession.WaitForUserConfirmAsync"/> immediately cancels.
        /// </summary>
        IWebViewSession CreateWebViewSession(WebViewSessionOptions options);
    }
}
