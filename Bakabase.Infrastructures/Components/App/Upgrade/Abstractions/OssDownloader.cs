#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

#endregion

namespace Bakabase.Infrastructures.Components.App.Upgrade.Abstractions
{
    public class OssDownloader
    {
        private readonly HttpClient _client = new();
        private readonly ILogger<OssDownloader> _logger;

        public OssDownloader(ILogger<OssDownloader> logger)
        {
            _logger = logger;
        }

        /// <summary>
        ///     下载文件到指定路径
        /// </summary>
        /// <param name="fileDic">remote url - local path</param>
        /// <param name="skip">Func&lt;url, remote md5, size, bool&gt;</param>
        /// <param name="onSkipped"></param>
        /// <param name="onDownloaded"></param>
        /// <param name="onFailed"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task Download(Dictionary<string, string> fileDic,
            Func<string, string, long, bool> skip,
            Func<string, Task> onSkipped,
            Func<string, Task> onDownloaded,
            Func<string, Task> onFailed,
            CancellationToken ct,
            int retryCount = 0)
        {
            var failedFiles = new ConcurrentDictionary<string, string>();

            var internalCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var internalCt = internalCts.Token;
            var mixedCt = CancellationTokenSource.CreateLinkedTokenSource(internalCt, ct).Token;

            var semaphore = new SemaphoreSlim(10, 10);
            var tasks = new List<Task>();
            using var enumerator = fileDic.GetEnumerator();
            while (enumerator.MoveNext())
            {
                _logger.LogInformation(
                    $"Task status distribution: {string.Join(", ", tasks.GroupBy(a => a.Status).Select(t => $"{t.Key}:{t.Count()}"))}");
                var faultedTask = tasks.FirstOrDefault(a => a.IsFaulted);
                if (faultedTask?.Exception != null)
                {
                    throw faultedTask.Exception?.GetBaseException();
                }

                var remoteMd5Error = false;
                await semaphore.WaitAsync(ct);
                var f = enumerator.Current;
                var task = Task.Run(async () =>
                {
                    var (url, path) = f;

                    try
                    {
                        var directory = Path.GetDirectoryName(path);

                        Directory.CreateDirectory(directory);

                        _logger.LogInformation($"Getting file MD5: {url}");
                        var remoteFile =
                            await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), mixedCt);

                        if (remoteFile.Content.Headers.ContentMD5 != null)
                        {
                            var remoteMd5 = Convert.ToBase64String(remoteFile.Content.Headers.ContentMD5);
                            if (skip != null && skip(url, remoteMd5, remoteFile.Content.Headers.ContentLength ?? 0))
                            {
                                if (onSkipped != null)
                                {
                                    await onSkipped(url);
                                    return;
                                }
                            }

                            await using var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                                FileShare.ReadWrite);
                            var rangeEnd = remoteFile.Content.Headers.ContentLength - 1;
                            if (fs.Length < remoteFile.Content.Headers.ContentLength)
                            {
                                _logger.LogInformation($"Start downloading file {url} to path {path}.");
                                var rangeBegin = fs.Length;
                                if (rangeBegin > 0)
                                {
                                    _logger.LogInformation(
                                        $"Found unfinished download {url}, continuing to download.");
                                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                                    req.Headers.Range = new RangeHeaderValue(rangeBegin, rangeEnd);
                                    remoteFile = await _client.SendAsync(req, mixedCt);
                                    fs.Seek(fs.Length, SeekOrigin.Begin);
                                }
                                else
                                {
                                    remoteFile = await _client.GetAsync(url, mixedCt);
                                }

                                await (await remoteFile.Content.ReadAsStreamAsync(mixedCt))
                                    .CopyToAsync(fs, mixedCt);
                            }

                            fs.Seek(0, SeekOrigin.Begin);

                            _logger.LogInformation("Comparing md5 checksum.");

                            var localMd5 = CalcFileMd5(fs);
                            if (remoteMd5 != localMd5)
                            {
                                await fs.DisposeAsync();
                                File.Delete(path);
                                _logger.LogInformation(
                                    $"Md5 checksum check failed, remote: {remoteMd5}, local: {localMd5}, restart downloading {url}");

                                failedFiles.TryAdd(url, path);

                                if (onFailed != null)
                                {
                                    await onFailed(url);
                                }
                            }
                            else
                            {
                                if (onDownloaded != null)
                                {
                                    await onDownloaded(url);
                                }
                            }
                        }
                        else
                        {
                            remoteMd5Error = true;
                            throw new Exception($"Can not get remote file's md5 from {url}, update aborted.");
                        }

                        _logger.LogInformation($"{url} has been downloaded successfully and saved to path {path}.");
                    }
                    catch (Exception ex)
                    {
                        if (remoteMd5Error)
                        {
                            throw;
                        }

                        _logger.LogError(ex, $"{url} download exception, restart downloading");

                        failedFiles.TryAdd(url, path);

                        if (onFailed != null)
                        {
                            await onFailed(url);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct);
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            if (failedFiles.Count > 0)
            {
                if (retryCount >= 3)
                {
                    throw new Exception($"Failed to download {failedFiles.Count} files after {retryCount} retries: {string.Join(", ", failedFiles.Keys)}");
                }
                _logger.LogInformation($"{failedFiles.Count} files remaining, retry {retryCount + 1}/3");
                await Download(failedFiles.ToDictionary(p => p.Key, p => p.Value), skip, onSkipped, onDownloaded,
                    onFailed, ct, retryCount + 1);
            }
        }

        /// <summary>
        ///     获取远程文件md5
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public async Task<string> GetRemoteMd5(string url)
        {
            var md5 = string.Empty;

            try
            {
                var req = new HttpRequestMessage(HttpMethod.Head, url);

                var response = await _client.SendAsync(req);

                if (response.Content.Headers.ContentMD5 != null)
                    md5 = Convert.ToBase64String(response.Content.Headers.ContentMD5);
                else
                    _logger.LogError($"{url} no md5");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{url} get md5 exception");
            }

            return md5;
        }

        public static string CalcFileMd5(Stream file)
        {
            using var md5 = MD5.Create();
            var bytes = md5.ComputeHash(file);
            return Convert.ToBase64String(bytes);
        }
    }
}