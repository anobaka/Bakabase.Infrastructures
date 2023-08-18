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
    }
}