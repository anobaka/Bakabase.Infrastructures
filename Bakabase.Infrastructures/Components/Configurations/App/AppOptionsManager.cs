using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Gui;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Configuration.Helpers;
using Microsoft.Win32;

namespace Bakabase.Infrastructures.Components.Configurations.App
{
    public class AppOptionsManager : JsonOptionsManager<AppOptions>
    {
        public AppOptionsManager() : base(GetAppOptionsFilePath(), GetAppOptionsKey())
        {
        }

        public string GetCustomConfigurationFilesDirectory()
        {
            return Path.Combine(Value.DataPath ?? AppService.DefaultAppDataDirectory,
                OptionsConstraints.RelativePathToAppData);
        }

        public static string GetAppOptionsFilePath()
        {
            var appOptionsDescriber =
                ConfigurationUtils.GetOptionsDescriber<AppOptions>(AppService.DefaultAppDataDirectory);
            return appOptionsDescriber.FilePath;
        }

        public static string GetAppOptionsKey()
        {
            var appOptionsDescriber =
                ConfigurationUtils.GetOptionsDescriber<AppOptions>(AppService.DefaultAppDataDirectory);
            return appOptionsDescriber.OptionsKey;
        }

        public static readonly AppOptionsManager Instance = new();

        /// <summary>
        /// Only <see cref="UiTheme.Light"/> or <see cref="UiTheme.Dark"/> will be returned.
        /// </summary>
        public UiTheme CalculatedUiTheme
        {
            get
            {
                var theme = Value.UiTheme;
                if (theme == UiTheme.FollowSystem)
                {
                    try
                    {
                        var res = (int) Registry.GetValue(
                            "HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
                            "AppsUseLightTheme", null);
                        theme = res == 0 ? UiTheme.Dark : UiTheme.Light;
                    }
                    catch (Exception e)
                    {
                        theme = UiTheme.Light;
                    }
                    finally
                    {
                    }
                }

                return theme;
            }
        }
    }
}