using System;
using System.Drawing;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Bakabase.Infrastructures.Components.Gui
{
    public interface IGuiAdapter
    {
        [ItemCanBeNull] string[] OpenFilesSelector(string? initialDirectory = null);
        [CanBeNull] string OpenFileSelector(string? initialDirectory = null);
        [CanBeNull] string OpenFolderSelector(string? initialDirectory = null);
        string GetDownloadsDirectory();
        void ShowTray(Func<Task> onExiting);
        void HideTray();
        void SetTrayText([NotNull] string text);
        void SetTrayIcon([NotNull] Icon icon);
        void ShowFatalErrorWindow([NotNull] string message, string title = "Fatal Error");
        void ShowInitializationWindow([NotNull] string processName);
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
    }
}