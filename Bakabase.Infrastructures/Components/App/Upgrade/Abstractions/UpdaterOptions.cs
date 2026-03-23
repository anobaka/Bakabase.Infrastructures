using Bakabase.Infrastructures.Components.Configurations;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Infrastructures.Components.App.Upgrade.Abstractions
{
    [Options]
    public class UpdaterOptions
    {
        public string OssAccessKeyId { get; set; }
        public string OssAccessKeySecret { get; set; }
        public string OssEndpoint { get; set; }
        public string OssBucket { get; set; }
        public string OssDomain { get; set; }
        public string AppUpdaterOssObjectPrefix { get; set; }
        public string UpdaterUpdaterOssObjectPrefix { get; set; }
        /// <summary>
        /// Base URL for Velopack update source. If not set, falls back to OSS domain + prefix.
        /// Example: https://your-domain.com/app/bakabase/releases/win-x64/
        /// </summary>
        public string? VelopackUpdateUrl { get; set; }
    }
}