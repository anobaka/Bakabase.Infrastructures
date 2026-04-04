using System;
using System.Threading.Tasks;
using AspectCore.DynamicProxy;

namespace Bakabase.Infrastructures.Components.Gui
{
    public abstract class GuiAdapter : IGuiAdapter
    {
        public abstract void InvokeInGuiContext(Action action);
        public abstract T InvokeInGuiContext<T>(Func<T> func);
        public abstract void ShowFatalErrorWindow(string message, string title = "Fatal Error");
        public abstract void ShowInitializationWindow(string processName);
        public abstract void DestroyInitializationWindow();
        public abstract void ShowMainWebView(string url, string title, Func<Task> onClosing);
        public abstract void SetMainWindowTitle(string title);

        public abstract bool MainWebViewVisible { get; }
        public abstract void Shutdown();
        public abstract void Hide();
        public abstract void Show();
        public abstract void ShowConfirmationDialogOnFirstTimeExiting(Func<CloseBehavior, bool, Task> onClosed);
        public abstract bool ShowConfirmDialog(string message, string caption);
        public abstract void ChangeUiTheme(UiTheme theme);
        public abstract byte[]? GetIcon(IconType type, string path);
        public abstract Task<string?> CaptureWebViewCookiesAsync(string loginUrl, string title, string[] cookieUrls,
            Func<string, (bool Done, string? NavigateToUrl)>? onNavigated = null,
            Dictionary<string, string>? labels = null);
    }
}