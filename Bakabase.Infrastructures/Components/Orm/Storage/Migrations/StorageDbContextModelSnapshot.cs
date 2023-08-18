﻿// <auto-generated />
using System;
using Bakabase.Infrastructures.Components.Orm.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bakabase.InsideWorld.Business.Migrations.StorageDb
{
    [DbContext(typeof(StorageDbContext))]
    partial class StorageDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "5.0.12");

            modelBuilder.Entity("Bootstrap.Bakabase.Components.Storage.Models.Entities.FileChangeLog", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("BatchId")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("ChangeDt")
                        .HasColumnType("TEXT");

                    b.Property<string>("New")
                        .HasColumnType("TEXT");

                    b.Property<string>("Old")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("FileChangeLogs");
                });
#pragma warning restore 612, 618
        }
    }
}