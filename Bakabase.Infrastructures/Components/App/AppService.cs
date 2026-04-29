using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.Infrastructures.Components.App.Models.ResponseModels;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Jobs;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Humanizer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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

        public static SemVersion CoreVersion
        {
            get
            {
                try
                {
                    var verStr = Assembly.GetEntryAssembly()
                                     ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                     ?.InformationalVersion ??
                                 AppConstants.InitialVersion;
                    return SemVersion.Parse(verStr, SemVersionStyles.Any);
                }
                catch
                {
                    return SemVersion.Parse(AppConstants.InitialVersion, SemVersionStyles.Any);
                }
            }
        }

        private static string? _defaultAppDataDirectory;

        /// <summary>
        /// The anchor for <c>app.json</c> and the default data location when
        /// <see cref="AppOptions.DataPath"/> is null. Resolved once per process via
        /// <see cref="DefaultAppDataPathResolver"/>; the env var override is honoured.
        /// </summary>
        public static string DefaultAppDataDirectory
        {
            get
            {
                if (string.IsNullOrEmpty(_defaultAppDataDirectory))
                {
                    var entryAssemblyName = Path.GetFileNameWithoutExtension(
                        Process.GetCurrentProcess().MainModule?.FileName) ?? "Bakabase";
                    bool isDebug;
#if DEBUG
                    isDebug = true;
#else
                    isDebug = false;
#endif
                    _defaultAppDataDirectory = DefaultAppDataPathResolver.Resolve(
                        DefaultAppDataPathResolver.GetCurrentOsPlatform(),
                        Environment.GetEnvironmentVariable,
                        Environment.GetFolderPath,
                        entryAssemblyName,
                        isDebug);
                }

                return _defaultAppDataDirectory;
            }
        }

        /// <summary>
        /// Logs co-locate with the anchor (i.e. <see cref="DefaultAppDataDirectory"/>) so
        /// the static logger can be configured before <c>AppOptions</c> is loaded.
        /// </summary>
        internal static string LogPath => Path.Combine(DefaultAppDataDirectory, "logs");

        public static void SetCulture(string language)
        {
            var cultureInfo = NormalizeLanguageCode(language);
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture =
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureInfo);
        }

        /// <summary>
        /// Normalizes language codes to BCP 47 standard format.
        /// Supports both legacy format (cn/en) and standard format (zh-CN/en-US).
        /// </summary>
        public static string NormalizeLanguageCode(string? language)
        {
            return language?.ToLowerInvariant() switch
            {
                "cn" or "zh-cn" or "zh-hans" or "zh-hans-cn" => "zh-CN",
                "en" or "en-us" => "en-US",
                _ => "zh-CN"
            };
        }

        /// <summary>
        /// Converts standard language code to legacy format for backward compatibility.
        /// </summary>
        public static string ToLegacyLanguageCode(string? language)
        {
            return NormalizeLanguageCode(language) == "zh-CN" ? "cn" : "en";
        }

        private static OsPlatform? _osPlatform;

        public static OsPlatform OsPlatform
        {
            get
            {
                if (!_osPlatform.HasValue)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _osPlatform = OsPlatform.Windows;
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        _osPlatform = OsPlatform.Linux;
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        _osPlatform = OsPlatform.Osx;
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
                    {
                        _osPlatform = OsPlatform.FreeBsd;
                    }
                    else
                    {
                        _osPlatform = OsPlatform.Unknown;
                    }
                }

                return _osPlatform.Value;
            }
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
                NullValueHandling = NullValueHandling.Ignore
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

            Log.Logger.Information(
                "Environment has been set up. AppData anchor: {Anchor}",
                DefaultAppDataDirectory);
        }

#if RUNTIME_MODE_WINFORMS
        public static RuntimeMode RuntimeMode => RuntimeMode.WinForms;
#elif RUNTIME_MODE_DOCKER
        public static RuntimeMode RuntimeMode => RuntimeMode.Docker;
#elif RUNTIME_MODE_MACOS
        public static RuntimeMode RuntimeMode => RuntimeMode.MacOS;
#else
        public static RuntimeMode RuntimeMode => RuntimeMode.Dev;
#endif

        #endregion

        private readonly ILogger<AppService> _logger;
        private readonly IBOptionsManager<AppOptions> _appOptionsManager;
        private readonly IServiceProvider _serviceProvider;
        private IWebHostEnvironment Env => _serviceProvider.GetRequiredService<IWebHostEnvironment>();

        public AppService(ILogger<AppService> logger,
            IBOptionsManager<AppOptions> appOptionsManager, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _appOptionsManager = appOptionsManager;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// <see cref="DefaultAppDataPathResolver.EnvVarName"/> overrides everything (including
        /// a user-set <see cref="AppOptions.DataPath"/>) — it is intended for Docker / headless
        /// scenarios where the operator wants a single mounted volume.
        /// </summary>
        public string AppDataDirectory
        {
            get
            {
                if (IsEnvironmentDataDirOverride)
                {
                    return DefaultAppDataDirectory;
                }

                return _appOptionsManager.Value.DataPath ?? DefaultAppDataDirectory;
            }
        }

        public DataPathSource DataPathSource
        {
            get
            {
                if (IsEnvironmentDataDirOverride)
                {
                    return DataPathSource.Environment;
                }

                return string.IsNullOrWhiteSpace(_appOptionsManager.Value.DataPath)
                    ? DataPathSource.Default
                    : DataPathSource.UserConfigured;
            }
        }

        public static bool IsEnvironmentDataDirOverride =>
            !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(DefaultAppDataPathResolver.EnvVarName));

        /// <summary>
        /// Where <c>app.json</c> lives. When <see cref="DefaultAppDataPathResolver.EnvVarName"/>
        /// is set, this is the env-var path (anchor and data are co-located). Otherwise it is
        /// the platform-default path, independent of the user-configured DataPath.
        /// </summary>
        public string AnchorPath => DefaultAppDataDirectory;

        public bool NeedRestart { get; set; }
        public bool NotAcceptTerms { get; set; }

        public string RequestAppDataDirectory(params string[] subDirs)
        {
            // Ensure both the anchor (where app.json lives) AND the data dir exist.
            // When DataPath is null they collapse to the same path; the second call is a no-op.
            Directory.CreateDirectory(DefaultAppDataDirectory);
            var fullPath = Path.Combine(AppDataDirectory,
                Path.Combine(subDirs.SelectMany(a => a.Split('/', '\\')).ToArray()));
            var dir = Directory.CreateDirectory(fullPath);
            return dir.FullName;
        }

        public string DataBackupDirectory => RequestAppDataDirectory("backups");
        public string TempFilesPath => RequestAppDataDirectory("temp");
        public string DataFilesPath => RequestAppDataDirectory("data");
        public string ComponentsPath => RequestAppDataDirectory("components");

        public async Task<SemVersion> GetLastRunningVersion() =>
            SemVersion.Parse((_appOptionsManager).Value.Version ?? AppConstants.InitialVersion);

        public async Task MakeBackupIfNeeded()
        {
            // hardcode
            var prevVersion = await GetLastRunningVersion();
            if (prevVersion != CoreVersion &&
                !SemVersion.Parse(AppConstants.InitialVersion, SemVersionStyles.Any).Equals(prevVersion))
            {
                _logger.LogInformation("New version of app is starting, start making backups...");
                var targetRootDir =
                    Directory.CreateDirectory(Path.Combine(DataBackupDirectory, prevVersion.ToString()!));
                var ignoredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {DataBackupDirectory, TempFilesPath, ComponentsPath, DataFilesPath};
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
            AnchorPath = AnchorPath,
            DefaultDataPath = DefaultAppDataDirectory,
            DataPathSource = DataPathSource,
            EnvVarName = DefaultAppDataPathResolver.EnvVarName,
            CoreVersion = CoreVersion.ToString(),
            LogPath = LogPath,
            BackupPath = DataBackupDirectory,
            NotAcceptTerms = NotAcceptTerms,
            NeedRestart = NeedRestart,
            TempFilesPath = TempFilesPath,
            DataPath = DataFilesPath
        };
    }
}