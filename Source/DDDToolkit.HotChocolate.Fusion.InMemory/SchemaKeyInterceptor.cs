using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Fusion.InMemory;

/// <summary>
/// Allows introspection for a request to a gateway that may read the schema, and tells one that may not how it
/// could: the gateway itself refuses <c>__schema</c> and <c>__type</c> to every other request.
/// </summary>
/// <remarks>
/// HotChocolate checks introspection when it validates a document, against a flag only the request's interceptor
/// can set. A gateway has one interceptor, so this one wraps whichever the gateway has: HotChocolate's default, or
/// one the host registered in <see cref="InMemoryFusionGatewayOptions.ConfigureGateway"/>, which still runs first.
/// </remarks>
internal sealed class SchemaKeyInterceptor(IHttpRequestInterceptor inner, SchemaKeyAccess access, SchemaReaders readers) : IHttpRequestInterceptor
{
    /// <summary>What a request that may not read the schema is told when it asks for it.</summary>
    internal const string NotAllowed =
        "This request may not read the schema: a tool reads it with the application's schema key, in the "
        + GraphQLSchemaKey.HeaderName + " header.";

    /// <summary>What a request is told at a gateway whose schema nobody reads.</summary>
    internal const string NobodyReads = "Nobody reads this endpoint's schema: it is read from the application's committed schema file.";

    /// <inheritdoc />
    public async ValueTask OnCreateAsync(HttpContext context, IRequestExecutor requestExecutor, OperationRequestBuilder requestBuilder, CancellationToken cancellationToken)
    {
        await inner.OnCreateAsync(context, requestExecutor, requestBuilder, cancellationToken).ConfigureAwait(false);

        if (access.MayReadSchema(context, readers))
        {
            requestBuilder.AllowIntrospection();
        }
        else
        {
            requestBuilder.SetIntrospectionNotAllowedMessage(readers == SchemaReaders.Nobody ? NobodyReads : NotAllowed);
        }
    }

    /// <summary>Puts this interceptor around the one a gateway's schema services hold.</summary>
    internal static void Wrap(IServiceCollection schemaServices, SchemaKeyAccess access, SchemaReaders readers)
    {
        var existing = schemaServices.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IHttpRequestInterceptor) && !descriptor.IsKeyedService);
        if (existing is not null)
        {
            schemaServices.Remove(existing);
        }

        schemaServices.AddSingleton<IHttpRequestInterceptor>(provider => new SchemaKeyInterceptor(Inner(existing, provider), access, readers));
    }

    private static IHttpRequestInterceptor Inner(ServiceDescriptor? existing, IServiceProvider provider) => existing switch
    {
        null => new DefaultHttpRequestInterceptor(),
        { ImplementationInstance: IHttpRequestInterceptor instance } => instance,
        { ImplementationFactory: { } factory } => (IHttpRequestInterceptor)factory(provider),
        { ImplementationType: { } type } => (IHttpRequestInterceptor)ActivatorUtilities.CreateInstance(provider, type),
        _ => new DefaultHttpRequestInterceptor(),
    };
}
