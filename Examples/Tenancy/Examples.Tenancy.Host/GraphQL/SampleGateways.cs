using DDDToolkit.HotChocolate.Fusion.InMemory;

namespace Examples.Tenancy.Host.GraphQL;

/// <summary>
/// The host's two GraphQL endpoints, each a gateway over the modules' source schemas: what a seat is offered at
/// <c>/graphql</c>, and the tenant's administration at <c>/admin/graphql</c>.
/// </summary>
/// <remarks>
/// <para>
/// A module says which schema a class of its fields belongs to (<c>[GraphQLSchema]</c>); the host says which
/// schemas form one endpoint. The two gateways compose the same Projects and Inspections, and each its own
/// Tenancy: the schema every seat is offered, or the administration's, which has another person's roles besides.
/// So the administration reads everything a seat reads, in one schema, and nothing of it reaches <c>/graphql</c>.
/// </para>
/// <para>
/// Both are endpoints, and require what the routes require of their callers: a token at <c>/graphql</c>, where each
/// module's schema checks the seat in front of its fields, and a seat at <c>/admin/graphql</c>, as the routes inside a
/// tenant do. A tool that reads the schema, GraphQL Codegen or the Relay compiler, has no token: in Development it
/// reads it without one, elsewhere with the key the host keeps at <see cref="GraphQLSchemaKey.Setting"/>, sent in
/// <see cref="GraphQLSchemaKey.HeaderName"/>.
/// </para>
/// </remarks>
public static class SampleGateways
{
    /// <summary>The gateway at <c>/graphql</c>: Tenancy's schema for a seat, Projects' and Inspections'.</summary>
    public const string User = "user";

    /// <summary>The gateway at <c>/admin/graphql</c>: Tenancy's administration's schema, Projects' and Inspections'.</summary>
    public const string Administration = "admin";

    /// <summary>Where <see cref="User"/> answers.</summary>
    public const string UserPath = "/graphql";

    /// <summary>Where <see cref="Administration"/> answers.</summary>
    public const string AdministrationPath = "/admin/graphql";
}
