using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Resources;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Infrastructures.Components.App
{
    public static class AppUtils
    {
        public static IHostBuilder CreateAppHostBuilder<TStartup>(params string[] args) where TStartup : class
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureServices(sc =>
                {
                    sc.AddSimpleLogging();
                    sc.AddLocalization(a => a.ResourcesPath = "Resources");
                    sc.AddSingleton<AppLocalizer>();
                })
                .ConfigureAppConfiguration((context, builder) => { builder.AddEnvironmentVariables(); })
                .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<TStartup>(); });
        }
    }
}
