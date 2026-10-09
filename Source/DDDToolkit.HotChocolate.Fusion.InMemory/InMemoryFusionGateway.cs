using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Fusion.Execution.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RequestDelegate = Microsoft.AspNetCore.Http.RequestDelegate;

namespace DDDToolkit.HotChocolate.Fusion.InMemory;

/// <summary>
/// HotChocolate Fusion gateways inside a modular monolith: each composes source schemas the modules registered into
/// one schema, and answers GraphQL at an endpoint of its own by calling them directly, in the process.
/// </summary>
/// <remarks>
/// <para>
/// Each module registers a named source schema of its own, with <c>AddGraphQLServer("catalog")</c> and
/// <c>AddSourceSchemaDefaults()</c>: its types, its queries, and its part of the types other modules own,
/// keyed on a name and a key they agree on. A gateway knows no module's code. It composes the schemas it is given by
/// name, the way a Fusion gateway composes services across processes, so a module's GraphQL is the same code in the
/// monolith and as a service. An application with one gateway lets it compose every schema; one that serves several
/// surfaces, a user's and an administration's, names a gateway per surface and lists the schemas of each:
/// <c>[GraphQLSchema]</c> says which schema a class of fields belongs to, a gateway which schemas form one endpoint.
/// </para>
/// <para>
/// HotChocolate's own <c>AddGraphQLGatewayServer().AddInMemorySchema(...)</c> cannot serve the gateway over
/// HTTP next to more than one source schema: HotChocolate and Fusion each register themselves as the one
/// <see cref="IRequestExecutorProvider"/> of a service container, the endpoint asks it for the gateway and
/// the in-memory connector asks it for the modules, so one of the two loses. This gives each gateway a
/// service container of its own inside the application and hands it the modules explicitly, through the
/// same public classes <c>AddInMemorySchema</c> uses.
/// </para>
/// <para>
/// A source schema that cannot be composed does not fail loudly in the in-memory connector: the gateway
/// waits for a schema forever. The application's start therefore waits for every gateway's composed schema, and
/// fails as soon as the composer refuses one's source schemas, with every error it logged, or when there is no
/// schema after <see cref="InMemoryFusionGatewayOptions.CompositionTimeout"/>.
/// </para>
/// <para>
/// Each gateway is an endpoint, so what the host requires of its callers it puts on it the way it puts it on a
/// route: <c>MapInMemoryFusionGateway("/graphql", "user").RequireAuthorization()</c>. A tool that reads the schema,
/// GraphQL Codegen or the Relay compiler, has no user: by default it reads it with the <see cref="GraphQLSchemaKey"/>,
/// and in Development without one, as a gateway's <see cref="InMemoryFusionGatewayOptions.SchemaReaders"/> say.
/// </para>
/// </remarks>
public static class InMemoryFusionGateway
{
    /// <summary>
    /// The name of the gateway <see cref="AddInMemoryFusionGateway(IServiceCollection, Action{InMemoryFusionGatewayOptions}?)"/>
    /// registers, which composes every schema of the application: <c>_Default</c>, as HotChocolate calls a schema
    /// without a name. To require authorization of it, map it by this name.
    /// </summary>
    public const string DefaultName = "_Default";

    /// <summary>
    /// Registers the application's one gateway, which composes every schema registered in this service collection;
    /// <see cref="MapInMemoryFusionGateway(WebApplication, string)"/> serves it.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType()...;
    /// builder.Services.AddGraphQLServer("inventory").AddSourceSchemaDefaults().AddQueryType()...;
    /// builder.Services.AddInMemoryFusionGateway();
    ///
    /// var app = builder.Build();
    /// app.MapInMemoryFusionGateway();   // /graphql
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">It was called before.</exception>
    public static IServiceCollection AddInMemoryFusionGateway(
        this IServiceCollection services,
        Action<InMemoryFusionGatewayOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Register(services, DefaultName, listed: null, configure);
    }

    /// <summary>
    /// Registers a gateway named <paramref name="name"/> that composes the schemas <paramref name="sourceSchemas"/>
    /// lists, and no other; <see cref="MapInMemoryFusionGateway(IEndpointRouteBuilder, string, string)"/> serves it.
    /// </summary>
    /// <remarks>
    /// An application serves several surfaces this way, each one schema over the modules it lists: a user's, and an
    /// administration's with fields the user's has not. A schema may be composed by several gateways. A name listed
    /// that no schema is registered under fails the mapping, with the names there are.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddInMemoryFusionGateway("user", ["tenants", "projects", "inspections"]);
    /// builder.Services.AddInMemoryFusionGateway("admin", ["admin", "projects", "inspections"]);
    ///
    /// var app = builder.Build();
    /// app.MapInMemoryFusionGateway("/graphql", "user").RequireAuthorization();
    /// app.MapInMemoryFusionGateway("/admin/graphql", "admin").RequireAuthorization("Administrators");
    /// </code>
    /// </example>
    /// <param name="services">The application's services.</param>
    /// <param name="name">The gateway's name, which its endpoint is mapped by.</param>
    /// <param name="sourceSchemas">The names of the schemas it composes, as <c>AddGraphQLServer("name")</c> registered them.</param>
    /// <param name="configure">Its options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="sourceSchemas"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, or <paramref name="sourceSchemas"/> lists nothing or an empty name.</exception>
    /// <exception cref="InvalidOperationException">A gateway of that name is registered already.</exception>
    public static IServiceCollection AddInMemoryFusionGateway(
        this IServiceCollection services,
        string name,
        IEnumerable<string> sourceSchemas,
        Action<InMemoryFusionGatewayOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(sourceSchemas);

        string[] listed = [.. sourceSchemas.Distinct(StringComparer.Ordinal)];
        if (listed.Length == 0)
        {
            throw new ArgumentException($"The gateway '{name}' lists no source schema to compose.", nameof(sourceSchemas));
        }

        if (listed.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"The gateway '{name}' lists an empty name among its source schemas.", nameof(sourceSchemas));
        }

        return Register(services, name, listed, configure);
    }

    /// <summary>
    /// Serves the gateway <see cref="AddInMemoryFusionGateway(IServiceCollection, Action{InMemoryFusionGatewayOptions}?)"/>
    /// registers at <paramref name="path"/>, as an endpoint that requires nothing. To require something of its
    /// callers, map it by name: <c>MapInMemoryFusionGateway(path, InMemoryFusionGateway.DefaultName).RequireAuthorization()</c>.
    /// </summary>
    /// <remarks>
    /// An endpoint that requires nothing is refused nothing, so the schema key never has to pass its authorization,
    /// and an <c>IAuthorizationMiddlewareResultHandler</c> the host registered after <c>AddInMemoryFusionGateway()</c>
    /// is no matter to it, as it was not before the gateway was an endpoint.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="AddInMemoryFusionGateway(IServiceCollection, Action{InMemoryFusionGatewayOptions}?)"/> was not called,
    /// or no source schema is registered.
    /// </exception>
    public static WebApplication MapInMemoryFusionGateway(this WebApplication app, string path = "/graphql")
    {
        ArgumentNullException.ThrowIfNull(app);

        if (Registered(app.Services).Count > 0 && Find(app.Services, DefaultName) is null)
        {
            throw new InvalidOperationException(
                "MapInMemoryFusionGateway(path) serves the gateway AddInMemoryFusionGateway() registers, and the application registers only named ones: "
                + Names(app.Services) + ". Map each with MapInMemoryFusionGateway(path, name).");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Map(app, path, DefaultName, authorizationAnswered: false);
        return app;
    }

    /// <summary>
    /// Serves the gateway named <paramref name="name"/> at <paramref name="path"/>, as an endpoint: what the host
    /// requires of its callers goes on the builder this answers, <c>RequireAuthorization(...)</c> as on a route.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The schema is read at the endpoint as HotChocolate serves it: by introspection, and as a file at
    /// <c>{path}?sdl</c> and <c>{path}/schema.graphql</c>, behind a group's prefix where the gateway is mapped in a
    /// group. Who reads it is the gateway's <see cref="InMemoryFusionGatewayOptions.SchemaReaders"/> to say: by
    /// default any request in Development, and elsewhere only one that carries the <see cref="GraphQLSchemaKey"/>,
    /// with a token or without one. A tool such as GraphQL Codegen has no user, and passes the endpoint's
    /// authorization as a reader, for the schema and nothing else. Whoever is no reader is refused the file with 403,
    /// and introspection with an error that says what reads it.
    /// </para>
    /// <para>
    /// A reader passes authorization through the application's <c>IAuthorizationMiddlewareResultHandler</c>, which
    /// <c>AddInMemoryFusionGateway</c> wraps. One registered after it would replace that, so this checks that it did
    /// not, and fails when the configured key is too short to be a secret.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The application, or a group of its routes.</param>
    /// <param name="path">Where the gateway answers, <c>/graphql</c> for instance.</param>
    /// <param name="name">The name the gateway was registered under.</param>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> or <paramref name="name"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// No gateway is registered under <paramref name="name"/>; it lists a schema the application does not register,
    /// or there is none; <see cref="GraphQLSchemaKey.Setting"/> is too short; or another
    /// <c>IAuthorizationMiddlewareResultHandler</c> was registered after <c>AddInMemoryFusionGateway</c>.
    /// </exception>
    public static IEndpointConventionBuilder MapInMemoryFusionGateway(this IEndpointRouteBuilder endpoints, string path, string name)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Map(endpoints, path, name, authorizationAnswered: true);
    }

    /// <summary>
    /// Maps a gateway. <paramref name="authorizationAnswered"/> where a requirement may be put on its endpoint, and a
    /// reader then has to pass it.
    /// </summary>
    private static IEndpointConventionBuilder Map(IEndpointRouteBuilder endpoints, string path, string name, bool authorizationAnswered)
    {
        var application = endpoints.ServiceProvider;
        if (Registered(application).Count == 0)
        {
            throw new InvalidOperationException("Call AddInMemoryFusionGateway() on the services first.");
        }

        var gateway = Find(application, name)
            ?? throw new InvalidOperationException(
                $"No in-memory gateway is registered as '{name}'. The gateways registered are {Names(application)}: map one by the name AddInMemoryFusionGateway(name, ...) was given, in the same case.");

        var access = application.GetRequiredService<SchemaKeyAccess>();
        access.EnsureUsable();
        if (authorizationAnswered && !AnswersRefusals(application))
        {
            throw new InvalidOperationException(
                "An IAuthorizationMiddlewareResultHandler was registered after AddInMemoryFusionGateway(), and replaced the one that lets a request "
                + "with the GraphQL schema key past the gateway's authorization. Register it before AddInMemoryFusionGateway(), which wraps it.");
        }

        var gatewayServices = gateway.Build(application);
        var root = new PathString(path.TrimEnd('/'));
        var endpoint = new GatewayEndpoint(name, root, gateway.Options.SchemaReaders);

        var pipeline = endpoints.CreateApplicationBuilder();
        pipeline.ApplicationServices = gatewayServices;

        // The schema file is HotChocolate's to serve, to whoever reaches it, so only a reader reaches it: every GET
        // that asks for the file, whatever operation it carries besides, since whether HotChocolate runs that
        // operation or serves the file depends on server options a host may change. Introspection is the gateway's
        // own to refuse (SchemaKeyInterceptor).
        pipeline.Use(next => context => SchemaRequests.AsksForSchemaFile(context.Request, root) && !access.MayReadSchema(context, endpoint.Readers)
            ? Refused(context)
            : next(context));
        pipeline.MapGraphQL(root, ISchemaDefinition.DefaultName);
        var served = pipeline.Build();

        return endpoints.Map(RoutePatternFactory.Parse(root + "/{**" + GatewayEndpoint.Rest + "}"), context => UnderItsPath(context, endpoint, served))
            .WithDisplayName("In-memory Fusion gateway " + name)
            .WithMetadata(endpoint);
    }

    /// <summary>
    /// Whether the application's answer to a refusal is the one <c>AddInMemoryFusionGateway</c> registered around the
    /// host's own. Asked in a scope: ASP.NET Core asks for it per request, so a host may register its own per request.
    /// </summary>
    private static bool AnswersRefusals(IServiceProvider application)
    {
        var scope = application.CreateAsyncScope();
        try
        {
            return scope.ServiceProvider.GetService<IAuthorizationMiddlewareResultHandler>() is SchemaRequestsPastAuthorization;
        }
        finally
        {
            scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Serves a request under the gateway's own path. In a group of routes the request's path holds the group's
    /// prefix in front of it, and HotChocolate looks for its schema file under the path it was given; so the prefix
    /// moves to the path base for the gateway, as <c>UsePathBase</c> moves one.
    /// </summary>
    private static Task UnderItsPath(HttpContext context, GatewayEndpoint endpoint, RequestDelegate served)
    {
        var prefix = endpoint.Prefix(context);
        return prefix.HasValue ? UnderPrefixAsync(context, prefix, served) : served(context);
    }

    private static async Task UnderPrefixAsync(HttpContext context, PathString prefix, RequestDelegate served)
    {
        var request = context.Request;
        var (pathBase, path) = (request.PathBase, request.Path);
        if (!path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase, out var remaining))
        {
            await served(context).ConfigureAwait(false);
            return;
        }

        request.PathBase = pathBase.Add(prefix);
        request.Path = remaining;
        try
        {
            await served(context).ConfigureAwait(false);
        }
        finally
        {
            request.PathBase = pathBase;
            request.Path = path;
        }
    }

    private static Task Refused(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private static IServiceCollection Register(IServiceCollection services, string name, IReadOnlyList<string>? listed, Action<InMemoryFusionGatewayOptions>? configure)
    {
        var registered = services.Select(descriptor => descriptor.ImplementationInstance).OfType<GatewayRegistration>().ToList();
        if (registered.Any(gateway => gateway.Name == name))
        {
            throw new InvalidOperationException(name == DefaultName
                ? "AddInMemoryFusionGateway() is called twice. The application's one gateway composes every schema; to serve several, give each a name and the schemas it composes."
                : $"An in-memory gateway named '{name}' is registered already: each gateway has a name of its own.");
        }

        var options = new InMemoryFusionGatewayOptions();
        configure?.Invoke(options);
        services.AddSingleton(new GatewayRegistration(name, listed, options));

        if (registered.Count > 0)
        {
            return services;
        }

        // What every gateway of the application shares, registered with the first.

        // A gateway answers with the request's services, which are this container's, and asks them for its clients.
        services.AddSingleton<ISourceSchemaClientScopeFactory>(application => new InMemoryClientScopes(
            application.GetRequiredService<IRequestExecutorProvider>(),
            application.GetRequiredService<IRequestExecutorEvents>()));

        services.AddSingleton(application => new SchemaKeyAccess(
            application.GetService<IConfiguration>(),
            application.GetService<IHostEnvironment>(),
            application.GetService<ILoggerFactory>()?.CreateLogger(typeof(GraphQLSchemaKey).FullName!)));
        SchemaRequestsPastAuthorization.Register(services);

        services.AddHostedService(application => new CompositionChecks([.. application.GetServices<GatewayRegistration>()]));

        // The schemas as text, for a test that compares each with a committed file.
        services.AddSingleton(application => new InMemoryFusionSchemas(application, [.. application.GetServices<GatewayRegistration>()]));

        return services;
    }

    private static IReadOnlyList<GatewayRegistration> Registered(IServiceProvider application)
        => [.. application.GetServices<GatewayRegistration>()];

    private static GatewayRegistration? Find(IServiceProvider application, string name)
        => Registered(application).FirstOrDefault(gateway => gateway.Name == name);

    private static string Names(IServiceProvider application)
        => string.Join(", ", Registered(application).Select(gateway => gateway.Name).Order(StringComparer.Ordinal).Select(name => "'" + name + "'"));

    /// <summary>
    /// Holds the application's start until every gateway has a composed schema, and fails it, naming the problem
    /// where it can, when one has none in time.
    /// </summary>
    private sealed class CompositionChecks(IReadOnlyList<GatewayRegistration> gateways) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
            => Task.WhenAll(gateways.Select(gateway => gateway.ComposedAsync(cancellationToken)));

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>Options for one gateway of <see cref="InMemoryFusionGateway"/>.</summary>
public sealed class InMemoryFusionGatewayOptions
{
    /// <summary>
    /// How long the application's start waits for the composed schema at most. Thirty seconds by default. A
    /// composition the composer refuses fails the start at once, without waiting this long.
    /// </summary>
    /// <remarks>
    /// It bounds the gateway's own wait, and nothing that comes before it. HotChocolate builds every source schema
    /// when the application starts, in a hosted service that is registered before the gateway's and has no
    /// timeout of its own: a source schema whose building never ends, one that waits in
    /// <c>ConfigureSchemaAsync</c> for something that does not come, holds the start there, however short this is.
    /// </remarks>
    public TimeSpan CompositionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether <c>node(id:)</c> spans the source schemas, answered by whichever one owns the type in the id.
    /// On by default.
    /// </summary>
    public bool EnableGlobalObjectIdentification { get; set; } = true;

    /// <summary>
    /// Who reads the gateway's schema, by introspection and as its file: <see cref="SchemaReaders.DevelopmentOrKey"/>
    /// by default, every request in Development and elsewhere one with the <see cref="GraphQLSchemaKey"/>.
    /// </summary>
    /// <remarks>
    /// It decides the endpoint's authorization for a request that only reads the schema, introspection and the file
    /// alike, so a schema is never readable one way and refused the other. <c>DisableIntrospection</c> in
    /// <see cref="ConfigureGateway"/> does not overrule it.
    /// </remarks>
    public SchemaReaders SchemaReaders { get; set; } = SchemaReaders.DevelopmentOrKey;

    /// <summary>
    /// Anything else for the gateway, such as <c>ModifyRequestOptions(...)</c> or the bounds of a request. Each
    /// gateway has its own, so a host that bounds the requests of two gateways says so for each. Who reads its schema
    /// is for <see cref="SchemaReaders"/> to say, not for <c>DisableIntrospection</c> here.
    /// </summary>
    public Action<IFusionGatewayBuilder>? ConfigureGateway { get; set; }
}
