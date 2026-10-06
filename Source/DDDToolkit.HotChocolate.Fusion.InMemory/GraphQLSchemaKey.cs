namespace DDDToolkit.HotChocolate.Fusion.InMemory;

/// <summary>
/// The secret a tool sends to read a gateway's schema without a user's token: GraphQL Codegen, the Relay compiler's
/// schema download, a build step that writes <c>schema.graphql</c>.
/// </summary>
/// <remarks>
/// <para>
/// Such a tool asks the schema and nothing else: an introspection query, or the schema file
/// (<c>/graphql?sdl</c>, <c>/graphql/schema.graphql</c>). It has no user to sign in as, so a gateway that requires
/// authorization would refuse it. A request that carries the key in <see cref="HeaderName"/> reads the schema of
/// every gateway the application maps with <c>MapInMemoryFusionGateway</c>, token or none; it reads nothing else:
/// an ordinary operation still needs whatever the endpoint requires.
/// </para>
/// <para>
/// In Development a schema request needs no key: a developer's own codegen reads the schema of the application on
/// their machine as it is. Elsewhere one without the key is refused, from a signed-in user as well: the schema of
/// an application in production is no user's business, only its tools'. A gateway whose
/// <see cref="InMemoryFusionGatewayOptions.SchemaReaders"/> say otherwise has it otherwise.
/// </para>
/// <para>
/// The key is configuration and never code, read from <see cref="Setting"/>. A host on a developer's machine runs in
/// Development and needs none. A deployed host reads it from its own configuration: the environment variable
/// <c>GraphQL__SchemaKey</c>, or its secret store. The tool reads the same secret from its own environment, a CI
/// secret or an untracked <c>.env</c> file. It is compared in constant time and never logged: a wrong one is logged
/// as wrong, with the path it was sent to, and nothing of it.
/// </para>
/// <code>
/// openssl rand -base64 32                                  # once: the key, kept as one secret, GRAPHQL_SCHEMA_KEY say
/// GraphQL__SchemaKey=${{ secrets.GRAPHQL_SCHEMA_KEY }}     # the host's environment
/// GRAPHQL_SCHEMA_KEY=${{ secrets.GRAPHQL_SCHEMA_KEY }}     # the tool's, which sends it in X-GraphQL-Schema-Key
/// </code>
/// </remarks>
public static class GraphQLSchemaKey
{
    /// <summary>The header a tool sends the key in.</summary>
    public const string HeaderName = "X-GraphQL-Schema-Key";

    /// <summary>
    /// The configuration key the application's key is read from: <c>GraphQL:SchemaKey</c>, which an environment
    /// variable sets as <c>GraphQL__SchemaKey</c>. Without it no key is the key, and by default no request reads a
    /// schema outside Development.
    /// </summary>
    public const string Setting = "GraphQL:SchemaKey";

    /// <summary>
    /// The fewest characters a key has: 32, what <c>openssl rand -base64 24</c> writes. A shorter one fails the
    /// mapping of the gateway, since a key that can be guessed opens the schema to whoever guesses it.
    /// </summary>
    public const int MinimumLength = 32;
}
