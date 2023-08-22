using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Serilog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Infrastructures.Components.App
{
    public static class AppExtensions
    {
        public static IServiceCollection AddSimpleLogging(this IServiceCollection sc)
        {
            return sc.AddLogging(a =>
            {
#if DEBUG
                a.AddSimpleConsole(options =>
                {
                    options.IncludeScopes = true;
                    options.SingleLine = false;
                    options.ColorBehavior = LoggerColorBehavior.Enabled;
                    options.TimestampFormat = "HH:mm:ss.fff ";
                });
#endif
                a.AddSerilog();
            });
        }
    }
}
