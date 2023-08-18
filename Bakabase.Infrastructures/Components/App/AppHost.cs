using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.Jobs;
using Bakabase.Infrastructures.Components.Orm;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Configuration.Helpers;
using Bootstrap.Components.Logging.LogService;
using Bootstrap.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Semver;
using Serilog;

namespace Bakabase.Infrastructures.Components.App
{

    public abstract class AppHost
    {
        private readonly IGuiAdapter _guiAdapter;
        public IHost Host { get; private set; }
        protected ILogger<AppHost> Logger;
        public string FeAddress { get; set; }
        private const string DefaultFeAddress = "http://localhost:4444";

        protected AppHost(IGuiAdapter guiAdapter)
        {
            _guiAdapter = guiAdapter;
        }

        protected static IHostBuilder CreateHostBuilder<TStartup>(params string[] args) where TStartup : class
        {
            return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .ConfigureLogging(t =>
                {
#if DEBUG
                    t.AddSimpleConsole(options =>
                    {
                        options.IncludeScopes = true;
                        options.SingleLine = false;
                        options.ColorBehavior = LoggerColorBehavior.Enabled;
                        options.TimestampFormat = "HH:mm:ss.fff ";
                    });
#endif
                    t.AddSerilog();
                })
                .ConfigureAppConfiguration((context, builder) => { builder.AddEnvironmentVariables(); })
                .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<TStartup>(); });
        }

        public abstract IHostBuilder CreateHostBuilder(params string[] args);

        //protected abstract string AppKey { get; }
        protected abstract string DisplayName { get; }

        protected virtual string OverrideFeAddress(string feAddress)
        {
            return feAddress;
        }

        protected async Task Backup(IServiceProvider serviceProvider)
        {
            await _appService.MakeBackupIfNeeded();
        }

        protected enum MigrationTiming
        {
            BeforeDbMigration = 1,
            AfterDbMigration = 2,
        }

        protected async Task Migrate(IServiceProvider serviceProvider, MigrationTiming timing)
        {
            Logger.LogInformation($"Applying migrations on timing: {timing}");
            var migrators = serviceProvider.GetService<IEnumerable<IMigrator>>()?.ToArray();
            if (migrators?.Any() == true)
            {
                var prevVersion = await _appService.GetLastRunningVersion();
                if (!SemVersion.Parse("0.0.0").Equals(prevVersion))
                {
                    foreach (var m in migrators.Where(a => a.MaxVersion >= prevVersion).OrderBy(a => a.MaxVersion))
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

        protected virtual void PrepareUpdaterAsync(IServiceProvider serviceProvider)
        {
            try
            {
                var updaterUpdater = serviceProvider.GetRequiredService<UpdaterUpdater>();
                Task.Run(async () => { await updaterUpdater.StartUpdating(); });
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error while preparing updater updater");
            }
        }

        private IHost CreateHost(string[] args, ConfigurationRegistrations configurationRegistrations)
        {
            // {DataPath ?? AppData}/configs/*
            var optionsDescribers =
                configurationRegistrations.DiscoverAllOptionsDescribers(AppOptionsManager.Instance
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
#if RELEASE
                        var port = NetworkUtils.FreeTcpPort();
                        hostBuilder = hostBuilder.ConfigureWebHost(t => t.UseUrls($"http://localhost:{port}"));
#endif


            return hostBuilder.Build();
        }

        //protected abstract IStringLocalizer<TSharedResource> GetLocalizer<TSharedResource>();

        private IStringLocalizer<AppSharedResource> _appLocalizer;
        private AppService _appService;
        private IBOptionsManager<AppOptions> _appOptionsManager;

        protected virtual Assembly[] AssembliesForGlobalConfigurationRegistrationsScanning => new Assembly[] { };

        protected virtual void Initialize()
        {
        }


        public void Start(string[] args)
        {
            try
            {
                Initialize();

                var language = AppOptionsManager.Instance.Value?.Language;
                AppService.SetCulture(language);

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

                Logger = Host.Services.GetRequiredService<ILogger<AppHost>>();

                _appLocalizer = Host.Services.GetRequiredService<IStringLocalizer<AppSharedResource>>();
                _appService = Host.Services.GetRequiredService<AppService>();
                _appOptionsManager = Host.Services.GetRequiredService<IBOptionsManager<AppOptions>>();

                var appDataMover = Host.Services.GetRequiredService<AppDataMover>();

                _guiAdapter.ShowInitializationWindow(_appLocalizer[AppSharedResource.App_Initializing]);
                _guiAdapter.ShowTray(async () => await TryToExit(true));

                Task.Run(async () =>
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
                                _guiAdapter.ShowInitializationWindow(_appLocalizer[AppSharedResource.App_Cleaning]);
                                await appDataMover.RemovePreviousCoreData();

                                _guiAdapter.ShowInitializationWindow(
                                    _appLocalizer[AppSharedResource.App_MakingBackups]);
                                await Backup(Host.Services);

                                _guiAdapter.ShowInitializationWindow(_appLocalizer[AppSharedResource.App_Migrating]);
                                await Host.Services.MigrateSqliteDbContexts<LogDbContext>();

                                await Migrate(Host.Services, MigrationTiming.BeforeDbMigration);
                                await MigrateDb(Host.Services);
                                await Migrate(Host.Services, MigrationTiming.AfterDbMigration);

                                await _appOptionsManager.SaveAsync(t =>
                                {
                                    t.Version = AppService.CoreVersion.ToString();
                                });

                                _guiAdapter.ShowInitializationWindow(_appLocalizer[AppSharedResource.App_FinishingUp]);
                                await ExecuteCustomProgress(Host.Services);

                                PrepareUpdaterAsync(Host.Services);

                                var jobManager = Host.Services.GetService<SimpleJobManager>();
                                jobManager?.Start();

                                _guiAdapter.ShowMainWebView(address, $"{DisplayName} - {AppService.CoreVersion}",
                                    async () => await TryToExit(false));

                                _guiAdapter.DestroyInitializationWindow();
                            }
                            catch (Exception e)
                            {
                                _guiAdapter.ShowFatalErrorWindow(e.BuildFullInformationText());
                                Logger?.LogError(e.BuildFullInformationText());
                            }
                        });

                        #endregion
                    });

                    lifetime.ApplicationStopping.Register(_guiAdapter.Shutdown);

                    await Host.StartAsync();
                });
            }
            catch (Exception e)
            {
                var title = "Fatal error";
                if (_appLocalizer != null)
                {
                    title = _appLocalizer[AppSharedResource.App_FatalError];
                }

                Logger?.LogError(e.BuildFullInformationText());

                _guiAdapter.ShowFatalErrorWindow(e.BuildFullInformationText(), title);
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
                if (!_guiAdapter.ShowConfirmDialog(blockThings,
                        _appLocalizer?[AppSharedResource.App_Warning] ?? "Warning"))
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