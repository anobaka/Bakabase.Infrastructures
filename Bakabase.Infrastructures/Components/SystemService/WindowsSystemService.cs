using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Microsoft.Win32;

namespace Bakabase.Infrastructures.Components.SystemService
{
    public class WindowsSystemService : ISystemService
    {
        private UiTheme _uiTheme;

        public UiTheme UiTheme
        {
            get => _uiTheme;
            private set
            {
                if (_uiTheme != value)
                {
                    _uiTheme = value;
                    OnUiThemeChange?.Invoke(_uiTheme);
                }
            }
        }

        public string Language => CultureInfo.InstalledUICulture.Name;
        public event Func<UiTheme, Task> OnUiThemeChange;

        private void UpdateUiTheme()
        {
            try
            {
                var res = (int) Registry.GetValue(
                    "HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
                    "AppsUseLightTheme", null);
                UiTheme = res == 0 ? UiTheme.Dark : UiTheme.Light;
            }
            catch (Exception e)
            {
                UiTheme = UiTheme.Light;
            }
        }

        public WindowsSystemService()
        {
            UpdateUiTheme();

            SystemEvents.UserPreferenceChanging += (sender, e) =>
            {
                if (e.Category == UserPreferenceCategory.General)
                {
                    UpdateUiTheme();
                }
            };
        }
    }
}