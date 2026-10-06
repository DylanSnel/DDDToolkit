using DDDToolkit.EntityFramework.Options;
using HotChocolate.Execution.Configuration;

namespace Examples.Hosting;

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
    /// The host's connections to Postgres, per purpose, for a host that runs on it as a role that owns nothing;
    /// <see langword="null"/> on every other database. A module that finds them registers its context on them
    /// (<see cref="PostgresPools.AddContext{TContext}"/>) and leaves <see cref="Database"/> alone.
    /// </summary>
    public PostgresPools? Postgres { get; init; }

    /// <summary>
    /// The host's connections to Postgres, for a module that runs on nothing else: <see cref="Postgres"/>, or a
    /// refusal that says how a host gets them. Such a module has one set of migrations and no second way to
    /// register its context, so a host built any other way is told when the module is added, and not by the
    /// first query that finds no table.
    /// </summary>
    /// <exception cref="InvalidOperationException">The host was not built with <see cref="OnPostgres"/>.</exception>
    public PostgresPools RequirePostgres()
        => Postgres ?? throw new InvalidOperationException("This module runs on Postgres: build the ModuleHost with ModuleHost.OnPostgres(...).");

    /// <summary>
    /// A modular monolith on Postgres, with every module's context on <paramref name="pools"/>: the migrations
    /// are applied by whoever owns the database, from the files the modules export, and the host only checks
    /// that none is missing. <see cref="Database"/> says the same, for whatever reads it. Row level security is the
    /// host's to register, with <c>AddSupabaseRowLevelSecurity</c>; <c>UseDDDToolkit</c> then runs every module's
    /// context as its caller.
    /// </summary>
    /// <param name="pools">The host's connections, which it makes once and disposes when it stops.</param>
    /// <param name="connectionString">The connection string the pools were made from.</param>
    public static ModuleHost OnPostgres(PostgresPools pools, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(pools);
        return InProcess(ModuleDatabase.Supabase(connectionString)) with { Postgres = pools };
    }

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
