using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Aliyun.OSS;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using JetBrains.Annotations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Semver;

namespace Bakabase.Infrastructures.Components.App.Upgrade.Abstractions
{
    public abstract class AbstractUpdater
    {
        private OssClient _ossClient;

        private OssClient OssClient => _ossClient ??= new OssClient(Options.Value.OssEndpoint,
            Options.Value.OssAccessKeyId, Options.Value.OssAccessKeySecret);

        private readonly OssDownloader _downloader;
        protected readonly ILogger<AbstractUpdater> Logger;
        private CancellationTokenSource _cts;
        public UpdaterState State { protected set; get; } = new() {Status = UpdaterStatus.Idle};
        protected virtual Task<bool> GetEnablePreReleaseChannel() => Task.FromResult(false);
        protected abstract SemVersion CurrentVersion { get; }
        protected string DownloadDir => Path.Combine(AppRootPath, "bakabase-updater-temp");
        protected abstract string AppRootPath { get; }
        protected abstract string UnpackedFilesOssPathAfterVersion { get; }
        protected readonly AppService AppService;
        protected IOptions<UpdaterOptions> Options => UpdaterOptionsManager;
        protected IBOptionsManager<UpdaterOptions> UpdaterOptionsManager { get; }
        protected IBOptionsManager<AppOptions> AppOptionsManager { get; }
        protected abstract string OssObjectPrefix { get; }

        protected AbstractUpdater(OssDownloader downloader, ILogger<AbstractUpdater> logger, AppService appService,
            IBOptionsManager<UpdaterOptions> updaterOptionsManager, IBOptionsManager<AppOptions> appOptionsManager)
        {
            _downloader = downloader;
            Logger = logger;
            AppService = appService;
            UpdaterOptionsManager = updaterOptionsManager;
            AppOptionsManager = appOptionsManager;
        }

        private AppVersionInfo CheckNewVersion(SemVersion minimalVersion, bool includePreRelease)
        {
            var versionPaths = OssClient.ListObjects(new ListObjectsRequest(Options.Value.OssBucket)
            {
                Prefix = OssObjectPrefix,
                Delimiter = "/"
            });
            var versions = versionPaths.CommonPrefixes.Where(a => a.EndsWith('/'))
                .Select(a =>
                {
                    var versionString = Path.GetFileName(a.TrimEnd('/').TrimStart('v'));
                    return (Prefix: a, Version: SemVersion.TryParse(versionString, out var v) ? v : null);
                }).Where(a => a.Version != null).OrderByDescending(a => a.Version).ToArray();

            versions = versions.Where(a =>
                (includePreRelease || string.IsNullOrEmpty(a.Version.Prerelease)) &&
                (minimalVersion == null || a.Version > minimalVersion)).ToArray();

            var version = versions.Any() ? versions.FirstOrDefault().Version : null;
            if (version == null)
            {
                return null;
            }

            var installersPrefix = $"{OssObjectPrefix.TrimEnd('/')}/{version}/installer/";
            var installers = OssClient
                .ListObjects(new ListObjectsRequest(Options.Value.OssBucket)
                    {Prefix = installersPrefix}).ObjectSummaries.Where(a => a.Size > 0).Select(a =>
                    new AppVersionInfo.Installer
                    {
                        Name = Path.GetFileName(a.Key),
                        Size = a.Size,
                        Url =
                            $"{Options.Value.OssDomain.TrimEnd('/')}/{string.Join('/', a.Key.Split('/').Select(Uri.EscapeUriString))}"
                    }).ToArray();
            return new AppVersionInfo
            {
                Installers = installers,
                Version = version.ToString()
            };
        }

        private OssObjectSummary[] ListOssObjects(string prefix)
        {
            var remoteFiles = new List<OssObjectSummary>();
            const int pageSize = 1000;
            ObjectListing result = null;
            do
            {
                // 每页列举的文件个数通过mMxKeys指定，超出指定数量的文件将分页显示。
                var listObjectsRequest = new ListObjectsRequest(Options.Value.OssBucket)
                {
                    Prefix = prefix,
                    Marker = result?.NextMarker,
                    MaxKeys = pageSize
                };
                result = OssClient.ListObjects(listObjectsRequest);
                remoteFiles.AddRange(result.ObjectSummaries.Where(a => a.Size > 0));
            } while (result.IsTruncated);

            return remoteFiles.ToArray();
        }

        public async Task<AppVersionInfo> CheckNewVersion() =>
            CheckNewVersion(CurrentVersion, await GetEnablePreReleaseChannel());

        public async Task<BaseResponse> StartUpdating()
        {
            if (_cts?.IsCancellationRequested == false)
            {
                return BaseResponseBuilder.Ok;
            }

            var version = await CheckNewVersion();
            if (version == null)
            {
                var noLocalVersion =
                    CurrentVersion == SemVersion.Parse(AppConstants.InitialVersion, SemVersionStyles.Any);
                await UpdateState(s =>
                {
                    s.Reset();
                    s.Status = noLocalVersion ? UpdaterStatus.Failed : UpdaterStatus.UpToDate;
                    if (noLocalVersion)
                    {
                        s.Error = "No available version";
                    }
                });

                return BaseResponseBuilder.Ok;
            }

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            await UpdateState(s =>
            {
                s.Reset();
                s.StartDt = DateTime.Now;
                s.Status = UpdaterStatus.Running;
            });

            // App Files
            var appPrefix =
                $"{OssObjectPrefix.TrimEnd('/')}/{version.Version}/{UnpackedFilesOssPathAfterVersion}";
            var remoteFiles = ListOssObjects(appPrefix);

            await UpdateState(s => s.TotalFileCount = remoteFiles.Length);

            var remoteFilesMap = remoteFiles.ToDictionary(p => p.Key.Replace(appPrefix, null), p => p);

            _ = Task.Run(async () => { await DownloadAndUpdate(version, remoteFilesMap, ct); }, ct);

            return BaseResponseBuilder.Ok;
        }

        private async Task DownloadAndUpdate(AppVersionInfo version,
            Dictionary<string, OssObjectSummary> ossObjectRelativePathSummaryMap,
            CancellationToken ct)
        {
            // todo: retry interval
            try
            {
                // url - (local fullname, cache fullname)
                var filesToDownload = ossObjectRelativePathSummaryMap
                    .ToDictionary(p => $"{Options.Value.OssDomain.TrimEnd('/')}/{p.Value.Key.TrimStart('/')}",
                        p => (Fullname: Path.Combine(DownloadDir, p.Key),
                            CacheFullname: Path.Combine(AppRootPath, p.Key)));

                var urlFullnameMap = filesToDownload
                    .ToDictionary(p => p.Key, p => p.Value.Fullname);

                await _downloader.Download(urlFullnameMap,
                    (url, md5, size) =>
                    {
                        var cacheFullname = filesToDownload[url].CacheFullname;
                        var file = new FileInfo(cacheFullname);
                        if (file.Exists)
                        {
                            if (file.Length == size)
                            {
                                using var s = file.OpenRead();
                                var localMd5 = OssDownloader.CalcFileMd5(s);
                                if (localMd5 == md5)
                                {
                                    return true;
                                }
                            }
                        }

                        return false;
                    },
                    async url => await UpdateState(s => ++s.SkippedFileCount),
                    async url => await UpdateState(s => ++s.DownloadedFileCount),
                    async url => await UpdateState(s => ++s.FailedFileCount), ct);

                await UpdateWithDownloadedFiles(version);
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"An error occurred during downloading update files: {e.Message}");

                await UpdateState(s =>
                {
                    s.Error = e.Message;
                    s.Status = UpdaterStatus.Failed;
                });
            }
            finally
            {
                _cts?.Cancel();
            }
        }

        protected virtual async Task UpdateWithDownloadedFiles(AppVersionInfo version)
        {
            DirectoryUtils.Merge(DownloadDir, AppRootPath, true);
            await UpdateState(s => s.Status = UpdaterStatus.UpToDate);
        }

        public void StopUpdating()
        {
            _cts?.Cancel();
        }

        public async Task UpdateState(Action<UpdaterState> update)
        {
            update(State);
            if (OnStateChange != null)
            {
                await OnStateChange(State);
            }
        }

        [CanBeNull] public event Func<UpdaterState, Task> OnStateChange;
    }
}