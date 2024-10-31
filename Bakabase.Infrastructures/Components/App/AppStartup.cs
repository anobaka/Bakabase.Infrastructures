using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.Infrastructures.Components.Storage.Cleaning;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.Infrastructures.Resources;
using Bootstrap.Components.Communication.SignalR;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Doc.Swagger;
using Bootstrap.Components.Logging.LogService.Extensions;
using Bootstrap.Components.Logging.LogService.Services;
using Bootstrap.Components.Miscellaneous;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Mobiles.Android;
using Bootstrap.Components.Tasks.Progressor;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Bakabase.Infrastructures.Components.App
{
    public abstract class AppStartup<TSwaggerCustomDocumentFilter>
        where TSwaggerCustomDocumentFilter : SwaggerCustomModelDocumentFilter
    {
        public IConfiguration Configuration { get; }
        public IWebHostEnvironment Env { get; }

        protected string AppDataPath => AppOptionsManager.Default.Value.DataPath ?? AppService.DefaultAppDataDirectory;

        protected AppStartup(IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            Env = env;
        }

        protected abstract void ConfigureServicesBeforeOthers(IServiceCollection services);

        protected virtual void RegisterMigrators(IServiceCollection services)
        {

        }

        public virtual void ConfigureServices(IServiceCollection services)
        {
            ConfigureServicesBeforeOthers(services);

            services.AddMvc(t => t.Filters.Add(new SimpleInvalidPayloadFilter())).AddNewtonsoftJson(t =>
            {
                t.SerializerSettings.ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy
                    {
                        ProcessDictionaryKeys = false
                    }
                };
                t.SerializerSettings.DateFormatString = "yyyy-MM-dd HH:mm:ss.fff";
                t.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
            });

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddBootstrapSwaggerGen<TSwaggerCustomDocumentFilter>("v1", "API");

            services.AddSignalR(a =>
                {
                    a.AddFilter<HubLogger>();
                    a.MaximumParallelInvocationsPerClient = 4;
                })
                .AddJsonProtocol(t => t.PayloadSerializerOptions.DictionaryKeyPolicy = null);

            //services.Configure<AdbOptions>(Configuration.GetSection(nameof(AdbOptions)));

            // services.AddSingleton<BackgroundTaskManager>();

            services.AddSpaStaticFiles(configuration => { configuration.RootPath = "ClientApp/build"; });

            services.TryAddSingleton<CleanerManager>();

            services.AddUpdater();

            #region Bootstrap

            services.AddSingleton<LogService, Logging.SqliteLogService>();
            services.AddBootstrapLogService<Orm.Log.LogDbContext>(c =>
                c.UseBootstrapSqLite(AppDataPath, "bootstrap_log"));

            #endregion

            services.AddSingleton<AppDataMover>();

            services.AddResponseCaching();

            services.AddSingleton<AppContext>();
        }

        protected virtual void ConfigureEndpointsAtFirst(IEndpointRouteBuilder routeBuilder)
        {

        }

        public virtual void Configure(IApplicationBuilder app, IHostApplicationLifetime lifetime)
        {
            var listeningAddresses = app.ServerFeatures.Get<IServerAddressesFeature>().Addresses.ToArray();

            lifetime.ApplicationStarted.Register(() =>
            {
                app.ApplicationServices.GetRequiredService<AppContext>().ServerAddresses = listeningAddresses;
            });

            app.UseBootstrapCors(null, listeningAddresses);

            app.UseSwagger(t => { t.RouteTemplate = "/internal-doc/swagger/{documentName}/swagger.json"; });
            app.UseSwaggerUI(t => { t.SwaggerEndpoint("/internal-doc/swagger/v1/swagger.json", "v1"); });
            // wwwroot
            app.UseStaticFiles();

            app.UseResponseCaching();

            app.UseSimpleExceptionHandler(new SimpleExceptionHandlingOptions
            {
                ModifyResponse = async (response, e) =>
                {
                    response.ContentType = "application/json";
                    if (e is NotInitializedException nie)
                    {
                        await response.WriteAsync(
                            JsonConvert.SerializeObject(BaseResponseBuilder.Build(ResponseCode.NotFound, nie.Message)),
                            Encoding.UTF8);
                    }
                    else
                    {
                        await response.WriteAsync(
                            JsonConvert.SerializeObject(BaseResponseBuilder.Build(ResponseCode.SystemError,
                                e.BuildAllMessages())), Encoding.UTF8);
                    }
                }
            });

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                ConfigureEndpointsAtFirst(endpoints);
                endpoints.MapSimpleProgressorHub("/hub/progressor");
                endpoints.MapDefaultControllerRoute();
            });

            app.UseSpaStaticFiles();

            app.UseSpa(spa =>
            {
                spa.Options.SourcePath = "ClientApp/build";
                spa.Options.DefaultPageStaticFileOptions = new StaticFileOptions()
                {
                    OnPrepareResponse = ctx =>
                    {
                        var headers = ctx.Context.Response.GetTypedHeaders();
                        headers.CacheControl = new CacheControlHeaderValue
                        {
                            Public = true,
                            MaxAge = TimeSpan.FromDays(0)
                        };
                    },
                };
            });
        }
    }
}