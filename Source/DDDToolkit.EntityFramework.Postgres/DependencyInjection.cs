using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Postgres.Pgmq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// Registration of the pgmq sink.
/// <code>
/// builder.Services.AddPgmqSink&lt;OrderingContext&gt;(pgmq => pgmq.UseQueue("ordering_events"));
///
/// builder.Services.AddDDDToolkitEntityFramework(options =>
/// {
///     options.UseOutbox(outbox =>
///     {
///         outbox.RegisterEventsFromAssemblyContaining&lt;Program&gt;();
///         outbox.SendToPgmq&lt;OrderingContext&gt;();
///         outbox.DeliverInTransaction = true;   // the enqueue and the processed mark, one commit
///     });
/// });
/// </code>
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="PgmqSink{TContext}"/> (scoped), which enqueues on the connection of
    /// <typeparamref name="TContext"/> and therefore inside whatever transaction that context has.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Which queue, and what rides alongside the payload.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddPgmqSink<TContext>(this IServiceCollection services, Action<PgmqSinkOptions>? configure = null) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new PgmqSinkOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        // Scoped: it rides the scope's context, and therefore that context's transaction.
        services.TryAddScoped<PgmqSink<TContext>>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="PgmqSink"/> (singleton), which opens its own connections from
    /// <paramref name="dataSource"/>. For a queue in a database this process does not otherwise write
    /// to; there is no shared transaction on this path.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="dataSource"/> is null.</exception>
    public static IServiceCollection AddPgmqSink(this IServiceCollection services, NpgsqlDataSource dataSource, Action<PgmqSinkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dataSource);

        var options = new PgmqSinkOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(_ => new PgmqSink(dataSource, options));
        return services;
    }

    /// <summary>
    /// Publishes to the pgmq queue reached through <typeparamref name="TContext"/>. Pair it with
    /// <c>services.AddPgmqSink&lt;TContext&gt;()</c>.
    /// <para>
    /// Turn on <see cref="OutboxOptions.DeliverInTransaction"/> alongside it. The queue is a table in the
    /// same database as the outbox, so with one transaction around the attempt the enqueue and the
    /// processed mark commit together and the message reaches the queue exactly once.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> is null.</exception>
    public static OutboxOptions SendToPgmq<TContext>(this OutboxOptions outbox) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(outbox);
        return outbox.SendTo<PgmqSink<TContext>>();
    }

    /// <summary>
    /// Publishes to the pgmq queue reached through the registered <see cref="PgmqSink"/>. Pair it with
    /// <c>services.AddPgmqSink(dataSource)</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> is null.</exception>
    public static OutboxOptions SendToPgmq(this OutboxOptions outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        return outbox.SendTo<PgmqSink>();
    }
}
