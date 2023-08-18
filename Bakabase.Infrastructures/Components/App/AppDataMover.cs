using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Infrastructures.Components.App
{
    public class AppDataMover
    {
        private readonly IBOptionsManager<AppOptions> _appOptionsManager;
        private readonly AppService _appService;
        private readonly ILogger<AppDataMover> _logger;

        private static readonly HashSet<string> IgnoredRelativeDirs =
            new(StringComparer.OrdinalIgnoreCase)
            {
                
            };

        private static readonly HashSet<string> IgnoredRelativeFiles =
            new(StringComparer.OrdinalIgnoreCase)
            {
                AppOptionsManager.GetAppOptionsFilePath()
            };

        public AppDataMover(AppService appService, ILogger<AppDataMover> logger, IBOptionsManager<AppOptions> appOptionsManager)
        {
            _appService = appService;
            _logger = logger;
            _appOptionsManager = appOptionsManager;
        }

        private string[] _getCoreDataFiles(string dir = null)
        {
            dir ??= _appService.AppDataDirectory;
            var relativeFiles = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Select(a =>
                    a.Replace(dir, null, StringComparison.OrdinalIgnoreCase).TrimStart(Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return relativeFiles.Except(IgnoredRelativeFiles)
                .Where(a => IgnoredRelativeDirs.All(d => !a.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
                .Select(a => Path.Combine(dir, a)).ToArray();
        }

        public async Task CopyCoreData(string destDirectory)
        {
            _logger.LogInformation($"Copying app data from {_appService.AppDataDirectory} to {destDirectory}.");
            Directory.CreateDirectory(destDirectory);

            var files = _getCoreDataFiles();

            _logger.LogInformation($"Found {files.Length} files to copy.");

            //Now Create all of the directories
            foreach (var dirPath in files.Select(Path.GetDirectoryName))
            {
                Directory.CreateDirectory(dirPath.Replace(_appService.AppDataDirectory, destDirectory));
                _logger.LogInformation($"Creating directory: {dirPath}.");
            }

            //Copy all the files & Replaces any files with the same name
            foreach (var file in files)
            {
                File.Copy(file, file.Replace(_appService.AppDataDirectory, destDirectory), true);
                _logger.LogInformation($"Copying file: {file}.");
            }

            await _appOptionsManager.SaveAsync(t =>
            {
                t.PrevDataPath = _appService.AppDataDirectory;
                t.DataPath = destDirectory;
            });

            _appService.NeedRestart = true;

            _logger.LogInformation(
                $"{files.Length} files have been copied to {destDirectory}, waiting for restarting...");
        }

        public async Task RemovePreviousCoreData()
        {
            var options = (_appOptionsManager).Value;
            if (options.PrevDataPath.IsNotEmpty() && options.DataPath.IsNotEmpty() &&
                options.PrevDataPath != options.DataPath && Directory.Exists(options.PrevDataPath))
            {
                _logger.LogInformation("A change of app data path is detected, deleting previous app data files.");

                var files = _getCoreDataFiles(options.PrevDataPath);
                var dirs = Directory.GetDirectories(options.PrevDataPath, "*", SearchOption.TopDirectoryOnly);

                _logger.LogInformation($"Found {files.Length} files to delete.");

                foreach (var f in files)
                {
                    FileUtils.Delete(f, true, true);
                    _logger.LogInformation($"Deleting file: {f}.");
                }

                foreach (var d in dirs.OrderByDescending(t => t.Length))
                {
                    if (Directory.GetFileSystemEntries(d).Length == 0)
                    {
                        DirectoryUtils.Delete(d, true, true);
                        _logger.LogInformation($"Deleting directory: {d}.");
                    }
                }

                await _appOptionsManager.SaveAsync(t => t.PrevDataPath = null);

                _logger.LogInformation($"{files.Length} files have been deleted successfully.");
            }
        }
    }
}