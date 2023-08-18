using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using System;

namespace Bakabase.Infrastructures.Components.Orm.Notification
{
    [Obsolete]
    public class NotificationDbContext : global::Bootstrap.Components.Notification.Abstractions.NotificationDbContext
    {
        public NotificationDbContext([NotNull] DbContextOptions<NotificationDbContext> options) : base(options)
        {
            Database.OpenConnection();
            // cache_size is working with current connection only.
            Database.ExecuteSqlRaw($"PRAGMA cache_size = {5_000_000}");
        }
    }
}