using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bakabase.Infrastructures.Components.App.Upgrade.Abstractions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddUpdater(this IServiceCollection services)
        {
            return services
                .AddSingleton<OssDownloader>()
                .AddSingleton<AppUpdater>()
                .AddSingleton<UpdaterUpdater>();
        }
    }
}