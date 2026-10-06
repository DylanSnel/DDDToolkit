using HotChocolate.Execution;
using HotChocolate.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Fusion.InMemory;

/// <summary>
/// The schemas of an application that composes its modules in memory, printed: for a test that compares each
/// with a committed file, so a change to what clients see, or to what the gateway composes by, shows up in a
/// review as a change to a file.
/// </summary>
/// <remarks>
/// <para>
/// A singleton in the application's container, registered by
/// <see cref="InMemoryFusionGateway.AddInMemoryFusionGateway"/>:
/// </para>
/// <code>
/// var schemas = app.Services.GetRequiredService&lt;InMemoryFusionSchemas&gt;();
///
/// (await schemas.PrintGatewayAsync()).Should().Be(File.ReadAllText("schema.graphql"));
/// foreach (var name in schemas.SourceSchemaNames)
/// {
///     (await schemas.PrintSourceAsync(name)).Should().Be(File.ReadAllText($"{name}.graphql"));
/// }
/// </code>
/// <para>
/// The gateway's schema is what a client is offered. A module's source schema is what the module declares,
/// with the directives the gateway composes by, such as <c>@key</c>, <c>@lookup</c> and <c>@shareable</c>:
/// that is where a change to a key or to what is shared shows.
/// </para>
/// </remarks>
public sealed class InMemoryFusionSchemas
{
    private readonly IServiceProvider _application;
    private readonly Func<CancellationToken, Task<IRequestExecutor>> _gateway;
    private readonly IEnumerable<string> _servedApart;
    private IReadOnlyList<string>? _sourceSchemaNames;

    /// <summary>Created by the gateway's registration, which alone knows where the gateway's own services are.</summary>
    /// <param name="application">The application's container, which holds the source schemas.</param>
    /// <param name="gateway">Waits for the composed schema and answers the gateway's executor.</param>
    /// <param name="servedApart">The schemas the gateway leaves out, which are no source schemas of it.</param>
    internal InMemoryFusionSchemas(IServiceProvider application, Func<CancellationToken, Task<IRequestExecutor>> gateway, IEnumerable<string> servedApart)
    {
        _application = application;
        _gateway = gateway;
        _servedApart = servedApart;
    }

    /// <summary>
    /// The names of the source schemas, in ordinal order: every schema registered in the application, but those
    /// <see cref="InMemoryFusionGatewayOptions.ServedApart"/> names.
    /// </summary>
    public IReadOnlyList<string> SourceSchemaNames
        => _sourceSchemaNames ??= [.. InMemoryFusionGateway.SourceSchemaNames(_application.GetService<IRequestExecutorProvider>(), _servedApart).Order(StringComparer.Ordinal)];

    /// <summary>
    /// The composed schema as a client of the gateway sees it: the text the gateway's endpoint serves for
    /// <c>?sdl</c>.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting for the composed schema.</param>
    /// <exception cref="InvalidOperationException">
    /// <see cref="InMemoryFusionGateway.MapInMemoryFusionGateway"/> was not called yet, or the source schemas do
    /// not compose: the message says why where the composer did.
    /// </exception>
    public async ValueTask<string> PrintGatewayAsync(CancellationToken cancellationToken = default)
    {
        var gateway = await _gateway(cancellationToken).ConfigureAwait(false);

        // As HotChocolate's endpoint prints a schema: without the directives that are its own bookkeeping.
        return SchemaFormatter.FormatAsString(gateway.Schema, new SchemaFormatterOptions { IncludeInternalDirectives = false });
    }

    /// <summary>One module's source schema, with the directives the gateway composes by.</summary>
    /// <param name="name">The name the module registered its schema under, one of <see cref="SourceSchemaNames"/>.</param>
    /// <param name="cancellationToken">Stops waiting for the schema to be built.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a source schema's name.</exception>
    public async ValueTask<string> PrintSourceAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (!SourceSchemaNames.Contains(name, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                SourceSchemaNames.Count == 0
                    ? $"'{name}' is not a source schema of this application: it has none."
                    : $"'{name}' is not a source schema of this application. Its source schemas are: {string.Join(", ", SourceSchemaNames)}.",
                nameof(name));
        }

        var executors = _application.GetRequiredService<IRequestExecutorProvider>();
        var executor = await executors.GetExecutorAsync(name, cancellationToken).ConfigureAwait(false);
        return executor.Schema.ToString()!;
    }
}
