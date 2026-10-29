using System.Collections.Concurrent;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework;
using DDDToolkit.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>A message a sink was offered, and whether a Tenancy user was current when it was.</summary>
/// <param name="Name">The published name.</param>
/// <param name="TenancyUser">The kind of Tenancy user current during delivery, or <see langword="null"/> for none.</param>
public sealed record Offered(string Name, TenancyUserKind? TenancyUser);

/// <summary>
/// A sink added next to the module sink on every module's outbox: it is offered exactly what the module sink is
/// offered, and remembers it.
/// </summary>
public sealed class RecordingSink : IIntegrationEventSink
{
    private readonly ConcurrentQueue<Offered> _offered = new();

    /// <summary>Everything offered so far, in the order it was.</summary>
    public IReadOnlyList<Offered> Offered => [.. _offered];

    /// <inheritdoc />
    public Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _offered.Enqueue(new Offered(message.Name, TenancyUsers.Ambient?.Kind));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds this sink to the outbox of each module of the sample, after the host's own sinks. The host has registered its
    /// outboxes by the time a test's services run, and a second registration configures the same ones further.
    /// </summary>
    public void AddTo(IServiceCollection services)
        => services.AddDDDToolkitEntityFramework(options =>
        {
            options.UseOutbox<TenantsContext>(outbox => outbox.SendTo(this));
            options.UseOutbox<ProjectsContext>(outbox => outbox.SendTo(this));
            options.UseOutbox<InspectionsContext>(outbox => outbox.SendTo(this));
        });
}
