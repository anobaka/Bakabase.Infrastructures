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
        public UiTheme UiTheme
        {
            get
            {
                try
                {
                    var res = (int) Registry.GetValue(
                        "HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
                        "AppsUseLightTheme", null);
                    return res == 0 ? UiTheme.Dark : UiTheme.Light;
                }
                catch (Exception e)
                {
                    return UiTheme.FollowSystem;
                }
            }
        }

        public string Language => CultureInfo.InstalledUICulture.Name;
    }
}