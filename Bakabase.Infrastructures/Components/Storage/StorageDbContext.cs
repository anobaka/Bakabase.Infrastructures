using System;
using System.Diagnostics.CodeAnalysis;
using Bakabase.Infrastructures.Components.Storage.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Infrastructures.Components.Storage
{
    [Obsolete]
    public class StorageDbContext : DbContext
    {
        public DbSet<FileChangeLog> FileChangeLogs { get; set; }

        public StorageDbContext([NotNull] DbContextOptions options) : base(options)
        {
        }
    }
}