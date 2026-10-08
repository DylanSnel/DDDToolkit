using DDDToolkit.HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Types;

namespace Examples.Tenancy.Host.GraphQL;

/// <summary>
/// What this host gives the GraphQL source schema of every module, in one place, so the schemas cannot disagree
/// about a type they all declare or about what a caller needs.
/// </summary>
/// <remarks>
/// Each module registers a schema of its own from its API project, and the host's gateways compose them
/// (<see cref="SampleGateways"/>): the user's at <c>/graphql</c>, the administration's at <c>/admin/graphql</c>. The
/// modules know nothing of the host's choices: they are handed them through <c>ModuleHost.WithGraphQL</c>, for every
/// schema they register, Tenancy's administration's too.
/// </remarks>
internal static class SampleGraphQL
{
    /// <summary>A root field a caller without a seat may ask: its own seats, from which a tenant is picked.</summary>
    public const string SeatsOfMine = "seatsOfMine";

    /// <summary>The other one: accepting an invitation, which is how a person comes by a seat.</summary>
    public const string InvitationAccept = "invitationAccept";

    /// <summary>
    /// The root fields of the application's own staff, who hold no seat: every tenant, and one tenant's access
    /// history, its projects and a project's inspections, each in the <c>Operators</c> feature of the module
    /// that owns it. They ask for an operator instead.
    /// </summary>
    public static readonly IReadOnlyList<string> OperatorFields = ["tenants", "tenantAccessHistory", "tenantProjects", "tenantProjectInspections"];

    /// <summary>
    /// The most fields the document of one request may have, counted where the document is read. A page of
    /// projects with everything a screen shows of each is some eighty; a request needs far more only to ask the
    /// same lists again and again under other names.
    /// </summary>
    public const int MostFields = 200;

    /// <summary>
    /// How deep a request may go. The deepest a screen asks is a project's inspections by their edges, with who
    /// recorded each: seven levels. A project names its inspections and an inspection its project, so without a
    /// bound a request could go round as often as it liked, a page of rows wider each time.
    /// </summary>
    public const int DeepestRequest = 10;

    /// <summary>
    /// Bounds a request at a gateway, before it is planned and before any module is asked: its depth, and how
    /// many fields its document has. Each gateway has options of its own, so each is given these.
    /// </summary>
    /// <remarks>
    /// Each module's schema estimates what an operation costs from the largest page each of its lists may hold,
    /// and refuses one over its limit. That is per module, and per operation the gateway sends it; and the gateway
    /// asks a field another module adds to a type once for every row. So a request that is wide, the list of
    /// projects twenty times under twenty names, each with the list of its inspections twenty times, can pass
    /// every estimate and still ask for hundreds of thousands of rows. The whole request is seen only at its
    /// gateway, so that is where it is bounded. The database stops a statement that runs long on a user's behalf as
    /// well (<c>SampleStorage.UserStatementTimeout</c>).
    /// <para>
    /// The depth is a bound on data. Introspection is bounded by HotChocolate's own rule for it, which every schema
    /// has, so the depth leaves it out: the query GraphQL Codegen sends nests <c>ofType</c> seven times, and is
    /// answered to a tool that may read the schema.
    /// </para>
    /// </remarks>
    /// <param name="gateway">A gateway over the modules' schemas.</param>
    public static IFusionGatewayBuilder AddSampleRequestBounds(this IFusionGatewayBuilder gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        return gateway
            .AddMaxExecutionDepthRule(DeepestRequest, skipIntrospectionFields: true)
            .ModifyParserOptions(parser => parser.MaxAllowedFields = MostFields);
    }

    /// <summary>
    /// Typed errors in every mutation's payload and coded errors for queries; enum values spelled as the REST API
    /// spells them, a refusal's kind in a query's error included; a scope of services per query field, since the fields of a query run side by side and each
    /// reads on a context of its own, and the request's scope for a mutation, which runs alone; the check
    /// that the caller has a seat in the tenant the request names, in front of every root field but
    /// <see cref="SeatsOfMine"/> and <see cref="InvitationAccept"/>, which need none, and
    /// <see cref="OperatorFields"/>, which need an operator; and the two conventions that let a module declare
    /// its types over the records its application layer answers: every field of a type with a key may be null
    /// but the key, so a reference its owner answers nothing for is its key and no error, and a field's
    /// <c>[Authorize("&lt;key&gt;")]</c> is a permission key, asked of the module that owns the field's type.
    /// </summary>
    /// <param name="graphql">A module's source schema.</param>
    public static IRequestExecutorBuilder AddSampleGraphQLConventions(this IRequestExecutorBuilder graphql)
    {
        ArgumentNullException.ThrowIfNull(graphql);

        return graphql
            .AddDDDToolkitErrors()
            .AddDDDToolkitMutationConventions()
            .AddDDDToolkitEnumValues(EnumValueSpelling.LowerSnakeCase)
            .AddDDDToolkitEntityNullability()
            .AddDDDToolkitKeyAuthorization()
            .ModifyOptions(options =>
            {
                options.DefaultQueryDependencyInjectionScope = DependencyInjectionScope.Resolver;
                options.DefaultMutationDependencyInjectionScope = DependencyInjectionScope.Request;
            })
            // One gate per schema: it remembers which type of that schema is the query type.
            .TryAddTypeInterceptor(new SeatGate(openFields: [SeatsOfMine, InvitationAccept], operatorFields: OperatorFields));
    }
}
