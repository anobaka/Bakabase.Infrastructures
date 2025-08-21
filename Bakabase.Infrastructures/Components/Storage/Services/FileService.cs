using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Storage.Abstractions;
using Bakabase.Infrastructures.Components.Storage.Models.Aos.ResponseModels;
using Bakabase.Infrastructures.Components.Storage.Models.Entities;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic.FileIO;

namespace Bakabase.Infrastructures.Components.Storage.Services
{
    /// <summary>
    /// 因为Storage的文件系统非常简单，暂时将Storage与Resource合并
    /// </summary>
    [Obsolete]
    public class FileService : ResourceService<StorageDbContext, FileChangeLog, int>
    {
        public static string CustomDataWwwRootDirectory { get; private set; }
        private const string CustomDataWwwRootRequestPrefix = "/cdata";
        private const string DefaultDataWwwRootRequestPrefix = "/data";
        protected AppService AppService => GetRequiredService<AppService>();
        public string DefaultDataWwwRootDirectory => AppService.RequestAppDataDirectory("data");
        private IBOptionsManager<AppOptions> _appOptionsManager;

        public FileService(IServiceProvider serviceProvider, IBOptionsManager<AppOptions> appOptionsManager) : base(serviceProvider)
        {
            _appOptionsManager = appOptionsManager;
            //AppService.OnAppOptionsChange += options =>
            //{
            //    CustomDataWwwRootDirectory = options.WwwRootPath;
            //    return Task.CompletedTask;
            //};
            CustomDataWwwRootDirectory = _appOptionsManager.Value.WwwRootPath;
        }

        public void BatchDeletePhysicalFilesByUris(params string[] uris)
        {
            if (uris != null)
            {
                foreach (var uri in uris.Where(u => u.IsNotEmpty()))
                {
                    var fullname = GetPhysicalPath(uri);
                    FileUtils.Delete(fullname, true, false);
                }
            }
        }

        private async Task<string> GetCustomDataWwwRootDirectory() => (_appOptionsManager).Value.WwwRootPath;

        /// <summary>
        /// Will not check whether path exists.
        /// </summary>
        /// <param name="uri"></param>
        /// <returns></returns>
        public string GetPhysicalPath(string uri)
        {
            if (uri.IsNullOrEmpty())
            {
                return null;
            }

            var segments = uri.Split('/', StringSplitOptions.RemoveEmptyEntries).ToArray();
            var firstSeg = segments.FirstOrDefault();
            string root;
            switch ($"/{firstSeg}")
            {
                case CustomDataWwwRootRequestPrefix:
                    root = CustomDataWwwRootDirectory;
                    break;
                case DefaultDataWwwRootRequestPrefix:
                    root = DefaultDataWwwRootDirectory;
                    break;
                default:
                    return null;
            }

            return Path.Combine(new[] {root}.Concat(segments.Skip(1).Select(Uri.UnescapeDataString)).ToArray());
        }

        public (string PhysicalPath, string RequestPath) GetWebDirectory(params string[] subDirectories)
        {
            var physicalPaths = new[] {CustomDataWwwRootDirectory ?? DefaultDataWwwRootDirectory}.Concat(subDirectories)
                .SelectMany(a => a.Split(new[] {'/', '\\'}, StringSplitOptions.RemoveEmptyEntries)).ToArray();
            var physicalPath = Path.Combine(physicalPaths);
            var requestPaths = new[]
                {
                    CustomDataWwwRootDirectory.IsNotEmpty()
                        ? CustomDataWwwRootRequestPrefix
                        : DefaultDataWwwRootRequestPrefix
                }
                .Concat(subDirectories)
                .SelectMany(a => a.Split(new[] {'/', '\\'}, StringSplitOptions.RemoveEmptyEntries)).ToArray();
            var requestPath = $"/{string.Join('/', requestPaths.Select(Uri.EscapeUriString))}";
            CreateAndGetDirectory(physicalPath);
            return (physicalPath, requestPath);
        }

        public (string PhysicalPath, string RequestPath) GetWebTempDirectory(string module, string submodule,
            bool ensureExists)
        {
            if (submodule.IsNullOrEmpty())
            {
                throw new ArgumentNullException(nameof(submodule));
            }

            return GetWebTempDirectory(Path.Combine(module, submodule), ensureExists);
        }

        public (string PhysicalPath, string RequestPath) GetWebTempDirectory(string module, bool ensureExists)
        {
            if (module.IsNullOrEmpty())
            {
                throw new ArgumentNullException(nameof(module));
            }

            const string root = "temp";
            return GetWebDirectory(root, module);
        }

        public static DirectoryInfo CreateAndGetDirectory(string fullPath)
        {
            return Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : Directory.CreateDirectory(fullPath);
        }

        public async Task<Dictionary<string, string>> GeneratePhysicalAndRequestPathMappings()
        {
            // uri - directory
            var fileInfo = new Dictionary<string, string>
            {
                {DefaultDataWwwRootRequestPrefix, DefaultDataWwwRootDirectory}
            };

            var options = _appOptionsManager;
            if (options.Value.WwwRootPath.IsNotEmpty())
            {
                CustomDataWwwRootDirectory = options.Value.WwwRootPath;
                fileInfo[CustomDataWwwRootRequestPrefix] = CustomDataWwwRootDirectory;
            }

            // Make sure all directories have been created.
            foreach (var (_, directory) in fileInfo)
            {
                CreateAndGetDirectory(directory);
            }

            return fileInfo;
        }

        public async Task<FileMoveResponseModel> MoveFiles(string root,
            IDictionary<string, string> moveActionsOnRelativePaths, bool overwrite)
        {
            return await MoveFiles(moveActionsOnRelativePaths.ToDictionary(a => Path.Combine(root, a.Key),
                a => Path.Combine(root, a.Value)), overwrite);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="moveActions"></param>
        /// <param name="overwrite"></param>
        /// <returns></returns>
        public async Task<FileMoveResponseModel> MoveFiles(IDictionary<string, string> moveActions, bool overwrite)
        {
            var tbMovedFiles = moveActions.Where(t => t.Key != t.Value).ToDictionary(t => t.Key, t => t.Value);
            var existedCount = 0;
            var failedActions = new Dictionary<string, string>();
            var successfulActions = new Dictionary<string, string>();
            // var doneCount = 0;
            // var percentage = 0;
            if (tbMovedFiles.Any())
            {
                foreach (var (source, dest) in tbMovedFiles)
                {
                    var folder = Path.GetDirectoryName(dest);
                    if (!Directory.Exists(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }

                    // try
                    // {
                    if (File.Exists(source))
                    {
                        File.Move(source, dest, overwrite);
                    }
                    else
                    {
                        FileSystem.MoveDirectory(source, dest, overwrite);
                    }

                    successfulActions.Add(source, dest);
                }

                if (successfulActions.Any())
                {
                    var batchId = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    await AddRange(successfulActions
                        .Select(t => new FileChangeLog {New = t.Value, Old = t.Key, BatchId = batchId}).ToList());
                }
            }

            return new FileMoveResponseModel
            {
                TotalCount = moveActions.Count,
                FileExistedCount = existedCount,
                FailedMoveActions = failedActions.Any() ? failedActions : null
            };
        }

        public async Task<List<string>> GetFileChangeBatchIdsAsync()
        {
            return await DbContext.Set<FileChangeLog>().Select(t => t.BatchId).Distinct().ToListAsync();
        }

        public async Task<BaseResponse> RevertFileChanges(string batchId)
        {
            var changeLogs = await GetAll(t => t.BatchId == batchId);
            foreach (var log in changeLogs)
            {
                var folder = Path.GetDirectoryName(log.Old);
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                try
                {
                    Directory.Move(log.New, log.Old);
                }
                catch (Exception e)
                {

                }
            }

            return BaseResponseBuilder.Ok;
        }

        public static void RemoveRelayDirectories(string rootOfTargetDirectories)
        {
            var directories = Directory.GetDirectories(rootOfTargetDirectories);
            var loop = false;
            do
            {
                foreach (var d in directories)
                {
                    var subfolders = Directory.GetDirectories(d);
                    var subfiles = Directory.GetFiles(d);
                    //只在文件夹内只有一个文件夹时才往外层抽取
                    if (!subfiles.Any())
                    {
                        if (subfolders.Length == 1)
                        {
                            foreach (var subFolder in subfolders)
                            {
                                Merge(subFolder, d);
                            }

                            loop = true;
                        }
                    }
                }
            } while (loop);
        }

        public static BaseResponse ExtractFromSubDirectories(string path)
        {
            var needRestart = true;
            while (needRestart)
            {
                needRestart = false;
                foreach (var folder in Directory.GetDirectories(path))
                {
                    var subfolders = Directory.GetDirectories(folder);
                    var subfiles = Directory.GetFiles(folder);
                    //只在文件夹内只有文件夹时才往外层抽取
                    if (!subfiles.Any())
                    {
                        if (subfolders.Any())
                        {
                            foreach (var subFolder in subfolders)
                            {
                                var destFolder = Path.Combine(path, Path.GetFileName(subFolder));
                                Merge(new DirectoryInfo(subFolder), new DirectoryInfo(destFolder));
                            }

                            needRestart = true;
                            if (!Directory.GetDirectories(folder).Any())
                            {
                                Directory.Delete(folder, true);
                            }
                        }
                    }
                }
            }

            return BaseResponseBuilder.Ok;
        }

        public static void Merge(string source, string target)
        {
            Merge(new DirectoryInfo(source), new DirectoryInfo(target));
        }

        public static void Merge(DirectoryInfo source, DirectoryInfo target)
        {
            if (string.Equals(source.FullName, target.FullName, StringComparison.CurrentCultureIgnoreCase))
            {
                return;
            }

            // Check if the target directory exists, if not, create it.
            if (Directory.Exists(target.FullName) == false)
            {
                Directory.CreateDirectory(target.FullName);
            }

            // Copy each file into it's new directory.
            foreach (var fi in source.GetFiles())
            {
                var path = Path.Combine(target.ToString(), fi.Name);
                if (File.Exists(path))
                {
                    fi.Delete();
                }
                else
                {
                    fi.MoveTo(path);
                }
            }

            // Copy each sub directory using recursion.
            foreach (var diSourceSubDir in source.GetDirectories())
            {
                var nextTargetSubDir = target.CreateSubdirectory(diSourceSubDir.Name);
                Merge(diSourceSubDir, nextTargetSubDir);
            }

            source.Delete();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="path"></param>
        /// <param name="openInDirectory">True for opening directory of <see cref="path"/> then select it and false for opening <see cref="path"/> itself.</param>
        public static async Task Open(string path, bool openInDirectory)
        {
            var arguments = $"\"{path}\"".Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (openInDirectory)
            {
                arguments = $"/select,{arguments}";
            }
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo("explorer", arguments) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", path);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", path);
            else
                throw new PlatformNotSupportedException();
        }

        public static List<DriveInfo> GetAllDrives()
        {
            return DriveInfo.GetDrives().ToList();
        }

        public static void GroupFilesToDirectories(string root)
        {
            var fileGroups = Directory.GetFiles(root).Select(Path.GetFileName)
                .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(a => a.Key, a => a.ToList());
            var dirs = Directory.GetDirectories(root)
                .ToDictionary(Path.GetFileName, a => a, StringComparer.OrdinalIgnoreCase);
            var multipleFilesGroups = fileGroups.Where(a => a.Value.Count > 1 || dirs.ContainsKey(a.Key))
                .ToDictionary(a => a.Key, a => a.Value);
            var moveActions = new Dictionary<string, string>();
            foreach (var g in multipleFilesGroups)
            {
                var dir = Path.Combine(root, g.Key);
                Directory.CreateDirectory(dir);
                g.Value.ForEach(v =>
                    moveActions[Path.Combine(root, v)] = Path.Combine(dir, $"{g.Key}{Path.GetExtension(v)}"));
            }

            foreach (var (s, d) in moveActions)
            {
                Directory.Move(s, d);
            }
        }
    }
}