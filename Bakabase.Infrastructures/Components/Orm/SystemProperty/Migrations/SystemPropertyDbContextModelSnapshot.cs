﻿// <auto-generated />

using Bakabase.Infrastructures.Components.Orm.SystemProperty;
using Bakabase.InsideWorld.Business;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bakabase.InsideWorld.Business.Migrations.SystemPropertyDb
{
    [DbContext(typeof(SystemPropertyDbContext))]
    partial class SystemPropertyDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "5.0.12");

            modelBuilder.Entity("Bootstrap.Components.Configuration.SystemProperty.SystemProperty", b =>
                {
                    b.Property<string>("Key")
                        .HasColumnType("TEXT");

                    b.Property<string>("Value")
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.ToTable("SystemProperties");
                });
#pragma warning restore 612, 618
        }
    }
}
