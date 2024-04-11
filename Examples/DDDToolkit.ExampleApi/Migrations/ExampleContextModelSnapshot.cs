﻿// <auto-generated />
using System;
using System.Collections.Generic;
using DDDToolkit.ExampleApi.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace DDDToolkit.ExampleApi.Migrations
{
    [DbContext(typeof(ExampleContext))]
    partial class ExampleContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.1")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("DDDToolkit.ExampleApi.Domain.ProductAggregate.Product", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("Price")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.ToTable("Products");
                });

            modelBuilder.Entity("DDDToolkit.ExampleApi.Domain.UserAggregate.User", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Email")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("PasswordHash")
                        .IsRequired()
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.ComplexProperty<Dictionary<string, object>>("Name", "DDDToolkit.ExampleApi.Domain.UserAggregate.User.Name#PersonName", b1 =>
                        {
                            b1.IsRequired();

                            b1.Property<string>("FirstName")
                                .IsRequired()
                                .HasColumnType("nvarchar(max)");

                            b1.Property<string>("LastName")
                                .IsRequired()
                                .HasColumnType("nvarchar(max)");

                            b1.Property<string>("MiddleNames")
                                .HasColumnType("nvarchar(max)");
                        });

                    b.HasKey("Id");

                    b.ToTable("Users");
                });

            modelBuilder.Entity("DDDToolkit.ExampleApi.Domain.UserAggregate.User", b =>
                {
                    b.OwnsMany("DDDToolkit.ExampleApi.Domain.UserAggregate.Entities.Order", "Orders", b1 =>
                        {
                            b1.Property<Guid>("UserId")
                                .HasColumnType("uniqueidentifier");

                            b1.Property<Guid>("Id")
                                .HasColumnType("uniqueidentifier");

                            b1.Property<DateTime>("PlacedAt")
                                .HasColumnType("datetime2");

                            b1.HasKey("UserId", "Id");

                            b1.ToTable("Order");

                            b1.WithOwner("User")
                                .HasForeignKey("UserId");

                            b1.OwnsMany("DDDToolkit.ExampleApi.Domain.ProductAggregate.ValueObjects.ProductId", "Products", b2 =>
                                {
                                    b2.Property<Guid>("OrderUserId")
                                        .HasColumnType("uniqueidentifier");

                                    b2.Property<Guid>("OrderId")
                                        .HasColumnType("uniqueidentifier");

                                    b2.Property<int>("Id")
                                        .ValueGeneratedOnAdd()
                                        .HasColumnType("int");

                                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b2.Property<int>("Id"));

                                    b2.Property<Guid>("Value")
                                        .HasColumnType("uniqueidentifier");

                                    b2.HasKey("OrderUserId", "OrderId", "Id");

                                    b2.ToTable("UserProducts", (string)null);

                                    b2.WithOwner()
                                        .HasForeignKey("OrderUserId", "OrderId");
                                });

                            b1.Navigation("Products");

                            b1.Navigation("User");
                        });

                    b.Navigation("Orders");
                });
#pragma warning restore 612, 618
        }
    }
}
