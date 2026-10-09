namespace DDDToolkit.HotChocolate.Fusion.InMemory;

/// <summary>
/// Who reads a gateway's schema: by introspection, and as the file at <c>{path}?sdl</c> and
/// <c>{path}/schema.graphql</c>. A gateway's <see cref="InMemoryFusionGatewayOptions.SchemaReaders"/>.
/// </summary>
/// <remarks>
/// <para>
/// One setting decides all three places a schema is read at, so the two ways to read it never disagree: the
/// endpoint's authorization, which lets a request that only reads the schema through for a reader without a token;
/// introspection, which the gateway allows such a reader for that one request; and the file, which HotChocolate
/// serves to whoever reaches it, and the gateway refuses with 403 to whoever is no reader.
/// </para>
/// <para>
/// It is the host's declaration, and nothing else overrules it: <c>DisableIntrospection</c> in
/// <see cref="InMemoryFusionGatewayOptions.ConfigureGateway"/> does not open or close a gateway's schema beside it.
/// </para>
/// </remarks>
public enum SchemaReaders
{
    /// <summary>
    /// In Development every request, elsewhere a request that carries the <see cref="GraphQLSchemaKey"/>, with a
    /// token or without one. The default: a developer's codegen reads the schema of the application on their machine
    /// as it is, and the schema of a deployed application is its tools' to read, not its users'.
    /// </summary>
    DevelopmentOrKey,

    /// <summary>
    /// A request that carries the <see cref="GraphQLSchemaKey"/>, in Development as well: for a host that runs in
    /// Development where others than its developers reach it.
    /// </summary>
    KeyOnly,

    /// <summary>
    /// Every request, wherever the application runs, past the endpoint's authorization as well: a public API, whose
    /// schema is no secret.
    /// </summary>
    Everyone,

    /// <summary>
    /// No request, key or not. The schema is read from the committed file a test writes with
    /// <see cref="InMemoryFusionSchemas"/>.
    /// </summary>
    Nobody,
}
