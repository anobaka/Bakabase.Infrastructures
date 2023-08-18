using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Models.ResponseModels;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Jobs;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Humanizer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Org.BouncyCastle.Utilities.Collections;
using Semver;
using Serilog;

namespace Bakabase.Infrastructures.Components.App
{
    public class AppService
    {
        #region Static

        public static SemVersion CoreVersion => SemVersion.Parse(Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

        private static string _defaultAppDataDirectory;

        public static string DefaultAppDataDirectory
        {
            get
            {
                if (_defaultAppDataDirectory.IsNullOrEmpty())
                {
#if DEBUG
                    var name = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule?.FileName);
                    _defaultAppDataDirectory =
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            $"{name}.Debugging");
#else
                        var processDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName!)!;
                        _defaultAppDataDirectory = Path.Combine(processDir, "AppData");
#endif
                }

                return _defaultAppDataDirectory;
            }
        }

        internal static string LogPath
        {
            get
            {
                var exeLocation = Assembly.GetEntryAssembly()?.Location;
                var currentDirectory = exeLocation.IsNullOrEmpty()
                    ? Directory.GetCurrentDirectory()
                    : Path.GetDirectoryName(exeLocation);
                return string.IsNullOrEmpty(currentDirectory) ? null : Path.Combine(currentDirectory, "logs");
            }
        }

        public static void SetCulture(string language)
        {
            var cultureInfo = "cn".Equals(language, StringComparison.OrdinalIgnoreCase)
                ? "zh-cn"
                : "en";
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture =
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureInfo);
        }

        static AppService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy
                    {
                        ProcessDictionaryKeys = false
                    }
                },
                DateFormatString = "yyyy-MM-dd HH:mm:ss.fff",
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            };

            var logPath = Path.Combine(LogPath ?? "logs", "AppLog_.log");

            Log.Logger = new LoggerConfiguration()
                // .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    logPath,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 100_000_000,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 10,
                    retainedFileTimeLimit: TimeSpan.FromDays(14),
                    outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] ({SourceContext}.{Method}) {Message}{NewLine}{Exception}"
                )
                .CreateLogger();

            Directory.CreateDirectory(DefaultAppDataDirectory);

            Log.Logger.Information("Environment has been set up.");
        }

        #endregion

        private readonly ILogger<AppService> _logger;
        private UpdaterUpdater UpdaterUpdater => _serviceProvider.GetRequiredService<UpdaterUpdater>();
        private readonly IBOptionsManager<AppOptions> _appOptionsManager;
        private readonly IServiceProvider _serviceProvider;

        public AppService(ILogger<AppService> logger,
            IBOptionsManager<AppOptions> appOptionsManager, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _appOptionsManager = appOptionsManager;
            _serviceProvider = serviceProvider;
        }

        public string AppDataDirectory => _appOptionsManager.Value.DataPath ?? DefaultAppDataDirectory;
        public bool NeedRestart { get; set; }
        public bool NotAcceptTerms { get; set; }

        public string RequestAppDataDirectory(params string[] subDirs)
        {
            var fullPath = Path.Combine(AppDataDirectory,
                Path.Combine(subDirs.SelectMany(a => a.Split('/', '\\')).ToArray()));
            var dir = Directory.CreateDirectory(fullPath);
            return dir.FullName;
        }

        public string DataBackupDirectory => RequestAppDataDirectory("backups");

        public async Task<SemVersion> GetLastRunningVersion() =>
            SemVersion.Parse((_appOptionsManager).Value.Version ?? "0.0.0");

        public async Task MakeBackupIfNeeded()
        {
            // hardcode
            var prevVersion = await GetLastRunningVersion();
            if (prevVersion != CoreVersion && !SemVersion.Parse("0.0.0").Equals(prevVersion))
            {
                _logger.LogInformation("New version of app is starting, making backups...");
                var targetRootDir =
                    Directory.CreateDirectory(Path.Combine(DataBackupDirectory, prevVersion.ToString()));
                var ignoredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {DataBackupDirectory, UpdaterUpdater.RootPathPath};
                // dirs
                foreach (var dir in Directory.GetDirectories(AppDataDirectory).Where(a => !ignoredDirs.Contains(a)))
                {
                    var tmpDir = Directory.CreateDirectory(Path.Combine(targetRootDir.FullName, Path.GetFileName(dir)));
                    _logger.LogInformation($"Making backups of {dir}");
                    DirectoryUtils.CopyFilesRecursively(dir, tmpDir.FullName, false);
                }

                // files
                foreach (var file in Directory.GetFiles(AppDataDirectory))
                {
                    var destFileFullname = Path.Combine(targetRootDir.FullName, Path.GetFileName(file));
                    if (!File.Exists(destFileFullname))
                    {
                        _logger.LogInformation($"Making backups of {file}");
                        File.Copy(file, destFileFullname);
                    }
                }
            }
        }

        public AppInfo AppInfo => new()
        {
            AppDataPath = AppDataDirectory,
            CoreVersion = CoreVersion.ToString(),
            LogPath = LogPath,
            UpdaterPath = UpdaterUpdater.RootPathPath,
            BackupPath = DataBackupDirectory,
            NotAcceptTerms = NotAcceptTerms,
            NeedRestart = NeedRestart
        };
    }
}