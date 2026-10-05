using DDDToolkit.HotChocolate.Authorization;
using DDDToolkit.HotChocolate.Conventions;
using DDDToolkit.HotChocolate.Errors;
using DDDToolkit.HotChocolate.Interceptors;
using DDDToolkit.HotChocolate.Subscriptions;
using DDDToolkit.Localization;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types.Descriptors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.HotChocolate;

/// <summary>Wires the DDDToolkit types and conventions into a HotChocolate schema.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the toolkit's schema conventions: the <see cref="IgnoreInternalFieldsInterceptor"/>,
    /// which hides every member marked <c>[Internal]</c>; the <see cref="DomainBehaviourFieldsInterceptor"/>,
    /// which lets entities, aggregates and value objects publish their properties by convention and never
    /// their methods; the <see cref="ShareableValueObjectsInterceptor"/>, which marks value objects
    /// <c>@shareable</c> in a Fusion source schema; and the <c>DomainEvent</c> interface type.
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
        builder.TryAddTypeInterceptor(typeof(DomainBehaviourFieldsInterceptor));
        builder.TryAddTypeInterceptor(typeof(ShareableValueObjectsInterceptor));

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
    /// <see cref="AddDDDToolkitErrors(IRequestExecutorBuilder)"/> for a schema whose enum values are spelled
    /// with <see cref="AddDDDToolkitEnumValues"/>: a refusal's <c>kind</c> extension is spelled the same way,
    /// <c>not_permitted</c> or <c>NOT_PERMITTED</c>, where the call without a spelling writes
    /// <c>NotPermitted</c>.
    /// <code>
    /// services
    ///     .AddGraphQLServer()
    ///     .AddDDDToolkitEnumValues(EnumValueSpelling.LowerSnakeCase)
    ///     .AddDDDToolkitErrors(EnumValueSpelling.LowerSnakeCase);
    /// </code>
    /// <para>
    /// Then <c>RefusalKind</c> reads the same wherever a client meets it: in the extensions of a refused
    /// query, and in the <c>RefusalError</c> of a refused mutation.
    /// </para>
    /// </summary>
    /// <param name="builder">The request executor builder to configure.</param>
    /// <param name="kindSpelling">How a refusal's <c>kind</c> is spelled: the spelling the schema's enum values have.</param>
    /// <returns>The same builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kindSpelling"/> is not one of the two spellings.</exception>
    public static IRequestExecutorBuilder AddDDDToolkitErrors(this IRequestExecutorBuilder builder, EnumValueSpelling kindSpelling)
    {
        ArgumentNullException.ThrowIfNull(builder);
        EnumValueSpellings.Checked(kindSpelling, nameof(kindSpelling));

        return builder.AddErrorFilter(services =>
            new FailureErrorFilter(services.GetRootServiceProvider().GetService<IFailureLocalizer>(), kindSpelling));
    }

    /// <summary>
    /// Turns on HotChocolate's mutation conventions for every mutation, an input object and a payload each, and
    /// gives every payload an <c>errors</c> list typed by the toolkit: a refusal, invalid values, broken rules
    /// or a concurrency conflict, each with its code, its message in the reader's language and its arguments.
    /// <code>
    /// services
    ///     .AddGraphQLServer()
    ///     .AddDDDToolkitTypes()
    ///     .AddDDDToolkitErrors()                 // queries, and whatever is not one of the four
    ///     .AddDDDToolkitMutationConventions()
    ///     .AddMutationType&lt;Mutation&gt;();
    /// </code>
    /// <code>
    /// mutation {
    ///   projectRename(input: { id: "42", name: "" }) {
    ///     project { name }
    ///     errors { ... on CodedError { code message } ... on RefusalError { kind field } }
    ///   }
    /// }
    /// </code>
    /// <para>
    /// A resolver declares nothing: it calls the use case, and what the use case throws is the error. A
    /// <c>RefusalException</c> becomes a <see cref="RefusalError"/>, an <c>InvalidValueObjectException</c> an
    /// <see cref="InvalidValuesError"/>, an <c>InvariantViolationException</c> a <see cref="BrokenRulesError"/>
    /// and a <c>ConcurrencyConflictException</c> a <see cref="ConcurrencyConflictError"/>. All four implement
    /// the interface <c>CodedError</c> (<see cref="ICodedError"/>), which becomes the interface of the schema's
    /// error types, your own included.
    /// </para>
    /// <para>
    /// HotChocolate matches an error type by the exception's exact type. A class derived from one of the four
    /// is therefore not a typed error: it passes on and reaches the client as a top-level error. Where
    /// <see cref="AddDDDToolkitErrors(IRequestExecutorBuilder)"/> is registered, one derived from a refusal, an
    /// invalid value or a broken rule is coded there; one derived from the conflict is not. Queries are not
    /// touched: a refused query answers a top-level coded error.
    /// </para>
    /// <para>
    /// In a Fusion source schema the error types are marked <c>@shareable</c>, by the
    /// <see cref="ShareableErrorTypesInterceptor"/>, so several modules can each have the conventions.
    /// Calling it more than once on the same builder is harmless.
    /// </para>
    /// <para>
    /// The error types, <c>CodedError</c> and <c>RefusalKind</c> carry descriptions of their own, written for
    /// the client that reads the schema, and not these comments: every schema with the conventions describes
    /// them alike, whether it reads XML documentation or not, so the source schemas of a gateway agree on them.
    /// </para>
    /// </summary>
    /// <param name="builder">The request executor builder to configure.</param>
    /// <returns>The same builder, so calls can be chained.</returns>
    public static IRequestExecutorBuilder AddDDDToolkitMutationConventions(this IRequestExecutorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddMutationConventions(applyToAllMutations: true);
        builder.AddErrorInterfaceType<ICodedError>();
        builder.AddMutationErrorConfiguration<ToolkitMutationErrors>();
        builder.TryAddTypeInterceptor(typeof(ShareableErrorTypesInterceptor));
        return builder;
    }

    /// <summary>
    /// Spells the values of the application's enums the host's way, in the whole schema: <c>not_permitted</c> with
    /// <see cref="EnumValueSpelling.LowerSnakeCase"/>, or <c>NOT_PERMITTED</c>, the way HotChocolate writes
    /// them, with <see cref="EnumValueSpelling.UpperSnakeCase"/>.
    /// <para>
    /// The lower spelling is what <c>JsonNamingPolicy.SnakeCaseLower</c> writes, the policy a host gives the
    /// enum converter of its REST JSON, so the two APIs of one application agree by construction, and both
    /// match a database that stores its enums so. A member that carries <c>[GraphQLName]</c> keeps that name,
    /// and HotChocolate's own enums keep HotChocolate's spelling: its directives and the Fusion composer know
    /// their values by it. A member the lower spelling would turn into <c>true</c>, <c>false</c> or
    /// <c>null</c>, which GraphQL reads as literals, fails the schema's build and needs a <c>[GraphQLName]</c>.
    /// </para>
    /// <para>
    /// It registers HotChocolate's default naming conventions with this one change, so a schema that has
    /// naming conventions of its own overrides <c>GetEnumValueName</c> there instead of calling this.
    /// </para>
    /// <para>
    /// <b>In a composed schema every source schema needs the same spelling.</b> An enum two source schemas
    /// share, such as <c>RefusalKind</c>, is one type to the gateway, and its values have to agree.
    /// </para>
    /// </summary>
    /// <param name="builder">The request executor builder to configure.</param>
    /// <param name="spelling">How the schema's enum values are spelled.</param>
    /// <returns>The same builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="spelling"/> is not one of the two spellings.</exception>
    public static IRequestExecutorBuilder AddDDDToolkitEnumValues(this IRequestExecutorBuilder builder, EnumValueSpelling spelling)
    {
        ArgumentNullException.ThrowIfNull(builder);
        EnumValueSpellings.Checked(spelling, nameof(spelling));

        return builder.AddConvention<INamingConventions>(_ => new SpelledEnumNamingConventions(spelling));
    }

    /// <summary>
    /// Makes every field of an entity nullable in the schema, except the fields of its key: registers the
    /// <see cref="EntityFieldsNullableInterceptor"/>. An entity is an object type with a key,
    /// <c>[EntityKey("id")]</c>.
    /// <code>
    /// services
    ///     .AddGraphQLServer("library")
    ///     .AddSourceSchemaDefaults()
    ///     .AddDDDToolkitTypes()
    ///     .AddDDDToolkitEntityNullability();
    /// </code>
    /// <para>
    /// For the source schemas a gateway composes. A reference the owning schema answers nothing for, because
    /// the entity is not there or not the caller's to read, then reaches the client as the object with its key
    /// and every other field <see langword="null"/>, and no error; a field declared as never null would turn
    /// it into <see langword="null"/> with an entry in <c>errors</c>. With this a module declares its GraphQL
    /// types over the records its application layer answers and writes nothing per field. Give every source
    /// schema the call, as it gets the other conventions: a schema that only names an entity has nothing but
    /// its key, and is not changed by it. Calling it more than once on the same builder is harmless.
    /// </para>
    /// </summary>
    /// <param name="builder">The request executor builder to configure.</param>
    /// <returns>The same builder, so calls can be chained.</returns>
    public static IRequestExecutorBuilder AddDDDToolkitEntityNullability(this IRequestExecutorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.TryAddTypeInterceptor(typeof(EntityFieldsNullableInterceptor));
        return builder;
    }

    /// <summary>
    /// Reads the policy of HotChocolate's <c>[Authorize]</c> as a permission key: registers HotChocolate's
    /// authorization with the <see cref="KeyAuthorizationHandler"/>, which asks the
    /// <see cref="IFieldKeys{TParent}"/> of the object a field belongs to.
    /// <code>
    /// services.AddScoped&lt;IFieldKeys&lt;Shelf&gt;, ShelfFieldKeys&gt;();
    ///
    /// services
    ///     .AddGraphQLServer()
    ///     .AddDDDToolkitErrors()                // shapes the refusal of a refused field
    ///     .AddDDDToolkitKeyAuthorization();
    /// </code>
    /// <code>
    /// [Authorize("library.shelves.manage")]
    /// public static IReadOnlyList&lt;Loan&gt;? GetLoans([Parent] Shelf shelf) =&gt; shelf.Loans;
    /// </code>
    /// <para>
    /// A refused field is <see langword="null"/> with the module's refusal at its path, coded as a refused query
    /// is; the object and its other fields stay. A parent type nobody answers for is refused, not allowed.
    /// </para>
    /// <para>
    /// <b>An application has one authorization handler</b>, in its own services, for all its schemas: this call
    /// replaces any other, such as the one HotChocolate's ASP.NET Core policies register, and a later
    /// registration replaces this one. Calling it on every source schema of a modular application is how each
    /// schema gets the <c>@authorize</c> directive, and is harmless.
    /// </para>
    /// </summary>
    /// <param name="builder">The request executor builder to configure.</param>
    /// <returns>The same builder, so calls can be chained.</returns>
    public static IRequestExecutorBuilder AddDDDToolkitKeyAuthorization(this IRequestExecutorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddAuthorizationHandler<KeyAuthorizationHandler>();
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
