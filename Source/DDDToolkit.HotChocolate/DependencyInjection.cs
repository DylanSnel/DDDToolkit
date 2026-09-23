using DDDToolkit.HotChocolate.Errors;
using DDDToolkit.HotChocolate.Interceptors;
using DDDToolkit.HotChocolate.Subscriptions;
using DDDToolkit.Localization;
using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.HotChocolate;

/// <summary>Wires the DDDToolkit types and conventions into a HotChocolate schema.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the toolkit's schema conventions: the <see cref="IgnoreInternalFieldsInterceptor"/>,
    /// which hides every member marked <c>[Internal]</c>, and the <c>DomainEvent</c> interface type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This registers conventions only. The scalar bindings and type converters for your ids and
    /// single value objects are generated per assembly by DDDToolkit.HotChocolate.Analyzers; call
    /// that assembly's <c>Add{Module}GraphQlRuntimeBindings()</c> as well — once per assembly that
    /// declares them:
    /// </para>
    /// <code>
    /// services
    ///     .AddGraphQLServer()
    ///     .AddDDDToolkitTypes()
    ///     .AddCommonGraphQlRuntimeBindings()   // generated into {Assembly}.GraphQl
    ///     .AddQueryType&lt;Query&gt;();
    /// </code>
    /// <para>Calling it more than once on the same builder is harmless.</para>
    /// </remarks>
    /// <param name="builder">The request executor builder to configure.</param>
    /// <returns>The same builder, so calls can be chained.</returns>
    public static IRequestExecutorBuilder AddDDDToolkitTypes(this IRequestExecutorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.TryAddTypeInterceptor(typeof(IgnoreInternalFieldsInterceptor));
        builder.AddHotChocolateTypes();
        return builder;
    }

    /// <summary>
    /// Registers <see cref="FailureErrorFilter"/>, which turns <c>InvalidValueObjectException</c> and
    /// <c>InvariantViolationException</c> into one GraphQL error per failure, each carrying its code:
    /// <code>
    /// services
    ///     .AddGraphQLServer()
    ///     .AddDDDToolkitTypes()
    ///     .AddDDDToolkitErrors();
    /// </code>
    /// <para>
    /// When the application registered an <c>IFailureLocalizer</c> (<c>AddDDDToolkitLocalization()</c> in
    /// <c>DDDToolkit.Localization</c>), the messages are phrased in the reader's language; otherwise they
    /// are the domain's own. See <c>docs/localization.md</c>.
    /// </para>
    /// </summary>
    /// <param name="builder">The request executor builder to configure.</param>
    /// <returns>The same builder, so calls can be chained.</returns>
    public static IRequestExecutorBuilder AddDDDToolkitErrors(this IRequestExecutorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The localizer is an application service, and error filters are built from the schema's own
        // services, so it is asked for on the root provider.
        return builder.AddErrorFilter(services =>
            new FailureErrorFilter(services.GetRootServiceProvider().GetService<IFailureLocalizer>()));
    }

    /// <summary>
    /// Registers <see cref="GraphQlSubscriptionSink"/> and the map of contracts it pushes, so the outbox
    /// can name it with <c>outbox.SendTo&lt;GraphQlSubscriptionSink&gt;()</c>.
    /// <para>
    /// You still register a subscription transport on the schema yourself:
    /// <c>AddInMemorySubscriptions()</c> for a single process, and at HotChocolate 16.6.6 there are also
    /// transports for Postgres, Redis, RabbitMQ and NATS when more than one server is holding sockets.
    /// The Postgres one is worth knowing about if you are already on Postgres: it rides
    /// <c>LISTEN</c>/<c>NOTIFY</c>, so several servers share subscriptions with no broker to run.
    /// </para>
    /// <para>
    /// A subscription reaches the clients connected at that moment and nothing else. It is a way to
    /// refresh a screen, not a way for another part of the system to find out what happened; that is
    /// what the module sink and a real queue are for. See <c>docs/graphql.md</c>.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Names the contracts that reach clients and their topics.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is null.</exception>
    public static IServiceCollection AddIntegrationEventSubscriptions(this IServiceCollection services, Action<GraphQlSubscriptionMap> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var map = new GraphQlSubscriptionMap();
        configure(map);

        services.AddSingleton(map);
        services.TryAddSingleton<GraphQlSubscriptionSink>();
        return services;
    }
}
