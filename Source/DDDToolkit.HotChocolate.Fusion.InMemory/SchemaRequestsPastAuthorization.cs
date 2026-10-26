using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Fusion.InMemory;

/// <summary>
/// Lets a schema request that may read the schema past the authorization of a gateway's endpoint: one that carries
/// the <see cref="GraphQLSchemaKey"/>, or any in Development, as the gateway's <see cref="SchemaReaders"/> say. Every
/// other outcome, and every other endpoint, is the application's own handler's.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET Core's authorization middleware asks this handler what to do with a request the endpoint's policy
/// refused: a tool fetching the schema has no user, so the policy refuses it. A request that only reads the schema
/// (<see cref="SchemaRequests"/>) and may read it (<see cref="SchemaKeyAccess"/>) goes on to the gateway; anything
/// else is challenged or forbidden by the handler the application had, which this one wraps. A request the policy
/// let through is that handler's too, so nothing changes for a signed-in caller.
/// </para>
/// <para>
/// It wraps the handler registered before <c>AddInMemoryFusionGateway</c>, or ASP.NET Core's own, and lives as long
/// as that one: ASP.NET Core asks for it per request, so a handler of the host's may be scoped. One registered after
/// it would replace it, and with it the schema key, so mapping a gateway that may require something checks that this
/// is the handler and says what to move where it is not.
/// </para>
/// </remarks>
internal sealed class SchemaRequestsPastAuthorization(IAuthorizationMiddlewareResultHandler otherwise, SchemaKeyAccess access) : IAuthorizationMiddlewareResultHandler
{
    /// <inheritdoc />
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Succeeded
            && context.GetEndpoint()?.Metadata.GetMetadata<GatewayEndpoint>() is { } gateway
            && access.MayReadSchema(context, gateway.Readers)
            && await SchemaRequests.IsSchemaRequestAsync(context, gateway.MatchedPath(context)).ConfigureAwait(false))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        await otherwise.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
    }

    /// <summary>
    /// Registers this handler around the one <paramref name="services"/> holds, for as long as that one lives, or
    /// around ASP.NET Core's own, as a singleton, when it holds none yet, which <c>AddAuthorization()</c> then leaves
    /// alone.
    /// </summary>
    internal static void Register(IServiceCollection services)
    {
        var existing = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler) && !descriptor.IsKeyedService);
        if (existing is not null)
        {
            services.Remove(existing);
        }

        services.Add(ServiceDescriptor.Describe(
            typeof(IAuthorizationMiddlewareResultHandler),
            provider => new SchemaRequestsPastAuthorization(Wrapped(existing, provider), provider.GetRequiredService<SchemaKeyAccess>()),
            existing?.Lifetime ?? ServiceLifetime.Singleton));
    }

    private static IAuthorizationMiddlewareResultHandler Wrapped(ServiceDescriptor? existing, IServiceProvider provider) => existing switch
    {
        null => new AuthorizationMiddlewareResultHandler(),
        { ImplementationInstance: IAuthorizationMiddlewareResultHandler instance } => instance,
        { ImplementationFactory: { } factory } => (IAuthorizationMiddlewareResultHandler)factory(provider),
        { ImplementationType: { } type } => (IAuthorizationMiddlewareResultHandler)ActivatorUtilities.CreateInstance(provider, type),
        _ => new AuthorizationMiddlewareResultHandler(),
    };
}

/// <summary>
/// A gateway's endpoint: its name, the path it was mapped at and who reads its schema. The mark the schema key looks
/// for.
/// </summary>
/// <param name="Name">The gateway's name.</param>
/// <param name="Path">The path it was mapped at, without the prefix of a group it was mapped in.</param>
/// <param name="Readers">Who reads its schema.</param>
internal sealed record GatewayEndpoint(string Name, PathString Path, SchemaReaders Readers)
{
    /// <summary>The route value that holds what a request asks for under the gateway's path: <c>schema.graphql</c>, say.</summary>
    internal const string Rest = "slug";

    /// <summary>
    /// The path the gateway answers <paramref name="context"/> at: <see cref="Path"/> behind the prefix of the group
    /// it was mapped in, <c>/api/graphql</c> for <c>/graphql</c> in <c>MapGroup("/api")</c>.
    /// </summary>
    public PathString MatchedPath(HttpContext context) => Prefix(context).Add(Path);

    /// <summary>
    /// What a group of routes put in front of <see cref="Path"/> for <paramref name="context"/>: the request's path
    /// before the gateway's own and what the route matched under it. Empty outside a group, and wherever the two do
    /// not line up.
    /// </summary>
    public PathString Prefix(HttpContext context)
    {
        var requested = (context.Request.Path.Value ?? string.Empty).TrimEnd('/');
        var rest = (context.GetRouteValue(Rest) as string ?? string.Empty).TrimEnd('/');
        if (rest.Length > 0)
        {
            if (!requested.EndsWith("/" + rest, StringComparison.Ordinal))
            {
                return PathString.Empty;
            }

            requested = requested[..^(rest.Length + 1)];
        }

        var own = Path.Value ?? string.Empty;
        return requested.Length > own.Length && requested.EndsWith(own, StringComparison.OrdinalIgnoreCase)
            ? new PathString(requested[..^own.Length])
            : PathString.Empty;
    }
}
