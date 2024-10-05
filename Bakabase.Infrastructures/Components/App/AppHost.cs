using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.Jobs;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.Infrastructures.Resources;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Configuration.Helpers;
using Bootstrap.Components.Logging.LogService;
using Bootstrap.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Infrastructures.Components.App
{

    public abstract class AppHost
    {
        private readonly IGuiAdapter _guiAdapter;
        private readonly ISystemService _systemService;
        protected IServiceProvider HostServices { get; private set; }
        protected ILogger Logger { get; private set; }
        protected AppLocalizer AppLocalizer { get; private set; }
        protected abstract string DisplayName { get; }

        protected virtual int ListeningPortCount { get; } = 1;

        public IHost Host { get; private set; }
        public string FeAddress { get; set; }
        private const string DefaultFeAddress = "http://localhost:4444";

        protected AppHost(IGuiAdapter guiAdapter, ISystemService systemService)
        {
            _guiAdapter = guiAdapter;
            _systemService = systemService;
        }

        protected abstract IHostBuilder CreateHostBuilder(params string[] args);

        protected virtual string OverrideFeAddress(string feAddress)
        {
            return feAddress;
        }

        protected async Task Backup(IServiceProvider serviceProvider)
        {
            await _appService.MakeBackupIfNeeded();
        }

        protected async Task Migrate(IServiceProvider serviceProvider, MigrationTiming timing)
        {
            Logger.LogInformation($"Applying migrations on timing: {timing}");
            var migrators = serviceProvider.GetService<IEnumerable<IMigrator>>()?.ToArray();
            if (migrators?.Any() == true)
            {
                var prevVersion = await _appService.GetLastRunningVersion();
                if (!AppConstants.InitialSemVersion.Equals(prevVersion))
                {
                    foreach (var m in migrators.Where(a => prevVersion.CompareSortOrderTo(a.ApplyOnVersionEqualsOrBefore) <= 0)
                                 .OrderBy(a => a.ApplyOnVersionEqualsOrBefore))
                    {
                        Logger.LogInformation($"Applying [{m.GetType().Name}] migrations on {timing}");
                        switch (timing)
                        {
                            case MigrationTiming.BeforeDbMigration:
                                await m.MigrateBeforeDbMigration();
                                break;
                            case MigrationTiming.AfterDbMigration:
                                await m.MigrateAfterDbMigration();
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(timing), timing, null);
                        }
                    }
                }
            }
        }

        protected virtual Task MigrateDb(IServiceProvider serviceProvider)
        {
            return Task.CompletedTask;
        }

        protected virtual Task ExecuteCustomProgress(IServiceProvider serviceProvider)
        {
            return Task.CompletedTask;
        }

        private IHost CreateHost(string[] args, ConfigurationRegistrations configurationRegistrations)
        {
            // {DataPath ?? AppData}/configs/*
            var optionsDescribers =
                configurationRegistrations.DiscoverAllOptionsDescribers(AppOptionsManager.Default
                    .GetCustomConfigurationFilesDirectory()).ToList();

            // {AppData}/app.json
            var appOptionsDescriber =
                optionsDescribers.FirstOrDefault(a => a.OptionsType == SpecificTypeUtils<AppOptions>.Type);
            if (appOptionsDescriber != null)
            {
                optionsDescribers.Remove(appOptionsDescriber);
            }

            appOptionsDescriber =
                ConfigurationUtils.GetOptionsDescriber<AppOptions>(AppService.DefaultAppDataDirectory);
            optionsDescribers.Add(appOptionsDescriber);


            var hostBuilder = CreateHostBuilder(args)
                .ConfigureHostConfiguration(t =>
                {
                    foreach (var d in optionsDescribers)
                    {
                        configurationRegistrations.AddRegisteredFile(t, d);
                    }
                })
                .ConfigureServices((context, collection) =>
                {
                    collection.AddTransient(sp => _guiAdapter)
                        .AddSingleton<AppDataMover>()
                        .AddSingleton<AppService>();

                    foreach (var d in optionsDescribers)
                    {
                        configurationRegistrations.Configure(collection, context.Configuration, d);
                    }
                });

            var listenPorts = new List<int>();
#if RELEASE
            for (var i = 0; i < ListeningPortCount; i++)
            {
                listenPorts.Add(NetworkUtils.GetFreeTcpPortAfter(i == 0 ? 34567 : listenPorts[i - 1]));
            }
            
#else
            listenPorts.Add(5000);
            for (var i = 1; i < ListeningPortCount; i++)
            {
                listenPorts.Add(NetworkUtils.GetFreeTcpPortAfter(listenPorts[i - 1]));
            }
#endif
            hostBuilder = hostBuilder.ConfigureWebHost(t =>
                t.UseUrls(listenPorts.Select(p => $"http://localhost:{p}").ToArray()));
            return hostBuilder.Build();
        }

        private AppService _appService;
        private IBOptionsManager<AppOptions> _appOptionsManager;

        protected virtual Assembly[] AssembliesForGlobalConfigurationRegistrationsScanning => new Assembly[] { };

        protected virtual void Initialize()
        {
        }

        private async Task<AppOptions> GetInitializationOptions()
        {
            var options = AppOptionsManager.Default.Value;

            if (options.IsNotInitialized())
            {
                #region Bakabase-Scope Settings

                // todo: acquire bakabase app eco settings

                #endregion

                #region System-Scope Settings

                options.Language = _systemService.Language;

                Logger.LogInformation($"Settings for bakabase scope are not found, use system-scope settings, language: {options.Language}, ui theme: {options.UiTheme}");

                #endregion
            }

            return options;
        }

        public async Task Start(string[] args)
        {
            try
            {
                #region Initialize host services

                var fallbackSc = new ServiceCollection();
                fallbackSc.AddSimpleLogging();
                fallbackSc.AddLocalization(a => a.ResourcesPath = "Resources");
                fallbackSc.AddSingleton<AppLocalizer>();
                HostServices = fallbackSc.BuildServiceProvider();
                Logger = HostServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
                AppLocalizer = HostServices.GetRequiredService<AppLocalizer>();

                #endregion

                var initialOptions = await GetInitializationOptions();

                AppService.SetCulture(initialOptions.Language);
                var uiTheme = initialOptions.UiTheme == UiTheme.FollowSystem
                    ? _systemService.UiTheme
                    : initialOptions.UiTheme;
                _guiAdapter.ChangeUiTheme(uiTheme);
                _systemService.OnUiThemeChange += async (newTheme) =>
                {
                    if (initialOptions.UiTheme == UiTheme.FollowSystem)
                    {
                        _guiAdapter.ChangeUiTheme(newTheme);
                    }
                };

                Initialize();

                var cr = new ConfigurationRegistrations();
                cr.AddApplicationPart(SpecificTypeUtils<AppOptions>.Type.Assembly);
                if (AssembliesForGlobalConfigurationRegistrationsScanning != null)
                {
                    foreach (var assembly in AssembliesForGlobalConfigurationRegistrationsScanning)
                    {
                        cr.AddApplicationPart(assembly);
                    }
                }

                Host = CreateHost(args, cr);

                _appService = Host.Services.GetRequiredService<AppService>();
                _appOptionsManager = Host.Services.GetRequiredService<IBOptionsManager<AppOptions>>();

                var appDataMover = Host.Services.GetRequiredService<AppDataMover>();

                _guiAdapter.ShowInitializationWindow(AppLocalizer.App_Initializing());
                _guiAdapter.ShowTray(async () => await TryToExit(true));

                // while (true)
                // {
                //     await Task.Delay(1000);
                // }

                await Task.Run(async () =>
                {
                    var lifetime = Host.Services.GetRequiredService<IHostApplicationLifetime>();
                    lifetime.ApplicationStarted.Register(() =>
                    {
#if DEBUG
                        var address = FeAddress ?? DefaultFeAddress;
#else
                        var server = Host.Services.GetRequiredService<IServer>();
                        var features = server.Features;
                        var address = features.Get<IServerAddressesFeature>()!
                            .Addresses.FirstOrDefault();
#endif
                        address = OverrideFeAddress(address);

                        #region Custom Progresses

                        Task.Run(async () =>
                        {
                            try
                            {
                                _guiAdapter.ShowInitializationWindow(AppLocalizer.App_Cleaning());
                                await appDataMover.RemovePreviousCoreData();

                                _guiAdapter.ShowInitializationWindow(AppLocalizer.App_MakingBackups());
                                await Backup(Host.Services);

                                _guiAdapter.ShowInitializationWindow(AppLocalizer.App_Migrating());
                                await Host.Services.MigrateSqliteDbContexts<LogDbContext>();

                                await Migrate(Host.Services, MigrationTiming.BeforeDbMigration);
                                await MigrateDb(Host.Services);
                                await Migrate(Host.Services, MigrationTiming.AfterDbMigration);

                                await _appOptionsManager.SaveAsync(t =>
                                {
                                    t.Version = AppService.CoreVersion.ToString();
                                });

                                _guiAdapter.ShowInitializationWindow(AppLocalizer.App_FinishingUp());
                                await ExecuteCustomProgress(Host.Services);

                                var jobManager = Host.Services.GetService<SimpleJobManager>();
                                jobManager?.Start();

                                _guiAdapter.ShowMainWebView(address, $"{DisplayName} - {AppService.CoreVersion}",
                                    async () => await TryToExit(false));

                                _guiAdapter.DestroyInitializationWindow();
                            }
                            catch (Exception e)
                            {
                                // todo: same error handling as outer scope
                                Logger?.LogError(e.BuildFullInformationText());
                                _guiAdapter.ShowFatalErrorWindow(e.BuildFullInformationText(),
                                    AppLocalizer?.App_FatalError() ?? "Fatal error");
                            }

                        }).ConfigureAwait(false).GetAwaiter().GetResult();

                        #endregion
                    });

                    lifetime.ApplicationStopping.Register(_guiAdapter.Shutdown);

                    await Host.StartAsync();
                });
            }
            catch (Exception e)
            {
                Logger?.LogError(e.BuildFullInformationText());
                _guiAdapter.ShowFatalErrorWindow(e.BuildFullInformationText(),
                    AppLocalizer?.App_FatalError() ?? "Fatal error");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>Cancel</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private async Task TryToExit(bool fromTray)
        {
            AppOptions appOptions = null;

            if (!fromTray)
            {
                appOptions = _appOptionsManager.Value;
                if (appOptions.CloseBehavior == CloseBehavior.Minimize)
                {
                    _guiAdapter.Hide();
                }
            }

            // from tray or minimize is not remembered

            var blockThings = await CheckIfAppCanExitSafely();
            if (blockThings.IsNotEmpty())
            {
                if (!_guiAdapter.ShowConfirmDialog(blockThings, AppLocalizer?.App_Warning() ?? "Warning"))
                {
                    return;
                }
            }

            if (fromTray)
            {
                _guiAdapter.Shutdown();
            }

            appOptions ??= _appOptionsManager.Value;

            switch (appOptions.CloseBehavior)
            {
                case CloseBehavior.Prompt:
                {
                    _guiAdapter.ShowConfirmationDialogOnFirstTimeExiting(async (operation, remember) =>
                    {
                        if (operation != CloseBehavior.Cancel)
                        {
                            if (remember)
                            {
                                await _appOptionsManager.SaveAsync(t =>
                                    t.CloseBehavior = operation == CloseBehavior.Minimize
                                        ? CloseBehavior.Minimize
                                        : CloseBehavior.Exit);
                            }

                            switch (operation)
                            {
                                case CloseBehavior.Minimize:
                                    _guiAdapter.Hide();
                                    break;
                                case CloseBehavior.Exit:
                                    _guiAdapter.Shutdown();
                                    break;
                                case CloseBehavior.Prompt:
                                case CloseBehavior.Cancel:
                                default:
                                    throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
                            }
                        }
                    });
                    break;
                }
                case CloseBehavior.Exit:
                    _guiAdapter.Shutdown();
                    break;
                case CloseBehavior.Minimize:
                    _guiAdapter.Hide();
                    break;
                case CloseBehavior.Cancel:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected abstract Task<string> CheckIfAppCanExitSafely();
    }
}