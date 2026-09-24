using DDDToolkit.EntityFramework.Options;
using HotChocolate.Execution.Configuration;

namespace DDDToolkit.Examples.Hosting;

/// <summary>
/// What a host decides for every module it runs: where the module's tables live, where the integration
/// events it publishes go, and whether it serves GraphQL. Everything else a module decides for itself.
/// </summary>
/// <param name="Database">Where the module's tables live.</param>
/// <param name="Publish">
/// Where the module's outbox delivers: to the other modules in this process, to a queue, to a broker.
/// Called once per publishing module, on that module's own outbox.
/// </param>
public sealed record ModuleHost(ModuleDatabase Database, Action<OutboxOptions> Publish)
{
    /// <summary>
    /// A modular monolith: every module in one process, each message handed to the other modules through
    /// the module sink.
    /// </summary>
    public static ModuleHost InProcess(ModuleDatabase database) => new(database, outbox => outbox.SendToModules());

    /// <summary>
    /// Delivers every module's messages to <typeparamref name="TSink"/> as well, after the transport
    /// already chosen: a GraphQL subscription sink next to the module sink, for instance.
    /// </summary>
    public ModuleHost AlsoSendTo<TSink>() where TSink : DDDToolkit.Interfaces.IIntegrationEventSink
        => this with
        {
            Publish = outbox =>
            {
                Publish(outbox);
                outbox.SendTo<TSink>();
            },
        };

    /// <summary>
    /// What the host adds to the GraphQL source schema of every module, when it serves GraphQL at all;
    /// <see langword="null"/> when it does not, and each module then registers none.
    /// </summary>
    public Action<IRequestExecutorBuilder>? GraphQL { get; init; }

    /// <summary>
    /// Serves GraphQL: every module registers its own source schema, for a Fusion gateway to compose, and
    /// <paramref name="configure"/> adds what is the host's to choose, such as the subscription transport.
    /// </summary>
    public ModuleHost WithGraphQL(Action<IRequestExecutorBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return this with { GraphQL = configure };
    }
}
