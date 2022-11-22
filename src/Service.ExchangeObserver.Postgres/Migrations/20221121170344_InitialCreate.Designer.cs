﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Service.ExchangeObserver.Postgres;

#nullable disable

namespace Service.ExchangeObserver.Postgres.Migrations
{
    [DbContext(typeof(DatabaseContext))]
    [Migration("20221121170344_InitialCreate")]
    partial class InitialCreate
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("exchangeobserver")
                .HasAnnotation("ProductVersion", "7.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Service.ExchangeObserver.Domain.Models.ObserverTransfer", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text");

                    b.Property<decimal>("Amount")
                        .HasColumnType("numeric");

                    b.Property<string>("Asset")
                        .HasColumnType("text");

                    b.Property<string>("From")
                        .HasColumnType("text");

                    b.Property<decimal>("IndexPrice")
                        .HasColumnType("numeric");

                    b.Property<string>("Reason")
                        .HasColumnType("text");

                    b.Property<DateTime>("TimeStamp")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("To")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.ToTable("transfers", "exchangeobserver");
                });
#pragma warning restore 612, 618
        }
    }
}