using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bootstrap.Components.Notification.Abstractions;
using Bootstrap.Components.Notification.Abstractions.Models.Constants;
using Bootstrap.Components.Notification.Abstractions.Models.RequestModels;
using Bootstrap.Components.Notification.Abstractions.Services;
using Bootstrap.Extensions;
using NPOI.OpenXmlFormats.Dml;
using Quartz;
using Quartz.Impl;

namespace Bakabase.Infrastructures.Components.Jobs
{
    public abstract class SimpleJobManager : IAsyncDisposable
    {
        private IScheduler _scheduler;
        private readonly IServiceProvider _serviceProvider;

        protected SimpleJobManager(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected abstract Task ScheduleJobs();

        public async Task Start()
        {
            var factory = new StdSchedulerFactory();
            _scheduler = await factory.GetScheduler();
            await _scheduler.Start();

            await ScheduleJobs();

            // await _messageService.Send(new CommonMessageSendRequestModel
            // {
            //     Content = "All jobs are registered",
            //     Subject = this.GetType().Name,
            //     Types = NotificationType.Os
            // });
        }

        protected Task ScheduleJob<TJob>(TimeSpan interval) where TJob : IJob
        {
            return ScheduleJob<TJob>(ssb => ssb.WithInterval(interval).RepeatForever());
        }

        protected Task ScheduleJob<TJob>(Action<SimpleScheduleBuilder> ssb) where TJob : IJob
        {
            return ScheduleJob<TJob>(tb => tb.WithSimpleSchedule(ssb));
        }

        protected async Task ScheduleJob<TJob>(Func<TriggerBuilder, TriggerBuilder> configure) where TJob : IJob
        {
            var jobName = SpecificTypeUtils<TJob>.Type.Name;
            var groupName = $"{jobName}Group";
            var triggerName = $"{jobName}Trigger";

            var job = JobBuilder.Create<TJob>()
                .WithIdentity(jobName, groupName)
                .SetJobData(new JobDataMap(
                    new Dictionary<string, object> {{nameof(IServiceProvider), _serviceProvider}} as IDictionary))
                .Build();

            var tb = TriggerBuilder.Create()
                .WithIdentity(triggerName, groupName);

            var trigger = configure(tb).Build();

            // Tell quartz to schedule the job using our trigger
            await _scheduler.ScheduleJob(job, trigger);
        }

        public async ValueTask DisposeAsync()
        {
            if (_scheduler != null)
            {
                await _scheduler.Shutdown();
            }
        }
    }
}