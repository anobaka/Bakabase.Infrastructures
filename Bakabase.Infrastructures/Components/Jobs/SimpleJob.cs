using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Bakabase.Infrastructures.Components.Jobs
{
    public abstract class SimpleJob : IJob
    {
        protected ILogger Logger { private set; get; }
        protected T GetRequiredService<T>() => ServiceProvider.GetRequiredService<T>();
        protected IServiceProvider ServiceProvider;

        public async Task Execute(IJobExecutionContext context)
        {
            var rootServiceProvider = context.GetData<IServiceProvider>();
            var scope = rootServiceProvider.CreateAsyncScope();
            ServiceProvider = scope.ServiceProvider;

            Logger = GetRequiredService<ILoggerFactory>().CreateLogger(GetType());

            Logger.LogInformation($"Starting job {GetType().Name}");

            try
            {
                await Execute(scope);
                Logger.LogInformation($"Job {GetType().Name} is finished");
            }
            catch (Exception e)
            {
                Logger.LogInformation($"An error occurred during Job {GetType().Name} execution");
                Logger.LogError(e.BuildFullInformationText());
            }
        }

        public abstract Task Execute(AsyncServiceScope scope);
    }
}