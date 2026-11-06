using DTS.DDD.BaseTypes;
using DTS.DDD.EntityFramework.Outbox;
using DTS.DDD.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DTS.DDD.Benchmarks.Infrastructure;

/// <summary>A context with only the outbox table, for the outbox processor benchmarks.</summary>
public sealed class OutboxContext(DbContextOptions<OutboxContext> options) : DbContext(options)
{
    /// <summary>The outbox rows.</summary>
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddDomainEventOutbox(Database);
}

/// <summary>The event that every outbox row of the benchmark holds.</summary>
public sealed record OrderNoted(string Reference) : DomainEvent;

/// <summary>A sink that accepts every message and does nothing with it.</summary>
public sealed class DiscardingSink : IIntegrationEventSink
{
    /// <inheritdoc />
    public Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
