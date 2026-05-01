using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Configuration.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Infrastructures.Components.Configurations.App
{
    /// <summary>
    /// <c>app.json</c> lives with the user's data, not at the anchor. The anchor only holds
    /// a <see cref="AnchorRedirect"/> file that points at the data dir; when no redirect
    /// exists, the data dir IS the anchor (default install). Path resolution is therefore
    /// dynamic — every read/write re-asks <see cref="EffectiveAppDataResolver"/> so a
    /// successful relocation mid-process takes effect immediately without re-instantiating
    /// the singleton.
    ///
    /// Before the first resolution, <see cref="LegacyAnchorAppJsonMigrator"/> runs to convert
    /// pre-redirect installs (anchor-owned <c>app.json</c> with non-empty <c>DataPath</c>) into
    /// the new layout. Idempotent; subsequent calls short-circuit on <c>.redirect</c>'s
    /// presence.
    /// </summary>
    public class AppOptionsManager : AbstractOptionsManager<AppOptions>
    {
        private readonly SemaphoreSlim _ss = new(1, 1);

        public static readonly AppOptionsManager Default = new();

        public override AppOptions Value
        {
            get
            {
                var (_, filePath) = ResolveFilePath();
                AppOptions? options = null;
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath, Encoding.UTF8);
                    var jObject = JObject.Parse(json);
                    options = jObject[OptionsKey]?.ToObject<AppOptions>();
                }
                return options ?? new AppOptions();
            }
        }

        public override void Save(AppOptions options)
        {
            var (_, filePath) = ResolveFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var jo = new JObject { [OptionsKey] = JToken.FromObject(options) };
            _ss.Wait();
            try
            {
                File.WriteAllText(filePath, JsonConvert.SerializeObject(jo, Formatting.Indented), Encoding.UTF8);
            }
            finally
            {
                _ss.Release();
            }
        }

        public override async Task SaveAsync(AppOptions options)
        {
            var (_, filePath) = ResolveFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var jo = new JObject { [OptionsKey] = JToken.FromObject(options) };
            await _ss.WaitAsync();
            try
            {
                await File.WriteAllTextAsync(filePath, JsonConvert.SerializeObject(jo, Formatting.Indented),
                    Encoding.UTF8);
            }
            finally
            {
                _ss.Release();
            }
        }

        /// <summary>
        /// Where AppData-scoped custom options files (e.g. <c>configs/*.json</c>) live.
        /// Follows the effective AppData root, not the anchor.
        /// </summary>
        public string GetCustomConfigurationFilesDirectory()
        {
            var (dataDir, _) = ResolveFilePath();
            return Path.Combine(dataDir, OptionsConstraints.RelativePathToAppData);
        }

        /// <summary>
        /// Effective <c>app.json</c> path — re-evaluated per call, post-migration. Used by
        /// <see cref="App.AppHost"/> to wire <c>app.json</c> into the DI configuration pipeline
        /// after the relocation runner has settled.
        /// </summary>
        public static string GetAppOptionsFilePath()
        {
            var (_, filePath) = ResolveFilePath();
            return filePath;
        }

        public static string GetAppOptionsKey() => OptionsKey;

        // OptionsKey is a function of the type alone (rootPath is irrelevant for the key);
        // resolved lazily so this static field initializer doesn't trigger AppService static
        // ctor — AppService itself constructs an AppOptionsManager during startup migration.
        private static readonly Lazy<string> OptionsKeyLazy = new(() =>
            ConfigurationUtils.GetOptionsDescriber<AppOptions>(string.Empty).OptionsKey);

        private static string OptionsKey => OptionsKeyLazy.Value;

        private static (string DataDir, string FilePath) ResolveFilePath()
        {
            // Idempotent; cheap once .redirect exists or the user never customised DataPath.
            LegacyAnchorAppJsonMigrator.RunIfNeeded(AppService.DefaultAppDataDirectory);

            var resolved = EffectiveAppDataResolver.Resolve(AppService.DefaultAppDataDirectory);
            return (resolved.DataDir, resolved.AppJsonPath);
        }
    }
}
