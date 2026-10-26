using HotChocolate.Execution;
using HotChocolate.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Fusion.InMemory;

/// <summary>
/// The schemas of an application that composes its modules in memory, printed: for a test that compares each
/// with a committed file, so a change to what clients see, or to what a gateway composes by, shows up in a
/// review as a change to a file.
/// </summary>
/// <remarks>
/// <para>
/// A singleton in the application's container, registered by <c>AddInMemoryFusionGateway</c>:
/// </para>
/// <code>
/// var schemas = app.Services.GetRequiredService&lt;InMemoryFusionSchemas&gt;();
///
/// foreach (var gateway in schemas.GatewayNames)
/// {
///     (await schemas.PrintGatewayAsync(gateway)).Should().Be(File.ReadAllText($"{gateway}.graphql"));
/// }
///
/// foreach (var name in schemas.SourceSchemaNames)
/// {
///     (await schemas.PrintSourceAsync(name)).Should().Be(File.ReadAllText($"{name}.source.graphql"));
/// }
/// </code>
/// <para>
/// A gateway's schema is what its clients are offered. A module's source schema is what the module declares,
/// with the directives the gateways compose by, such as <c>@key</c>, <c>@lookup</c> and <c>@shareable</c>:
/// that is where a change to a key or to what is shared shows.
/// </para>
/// </remarks>
public sealed class InMemoryFusionSchemas
{
    private readonly IServiceProvider _application;
    private readonly IReadOnlyList<GatewayRegistration> _gateways;

    /// <summary>Created by the gateways' registration, which alone knows where each gateway's own services are.</summary>
    /// <param name="application">The application's container, which holds the source schemas.</param>
    /// <param name="gateways">The gateways the application registered.</param>
    internal InMemoryFusionSchemas(IServiceProvider application, IReadOnlyList<GatewayRegistration> gateways)
    {
        _application = application;
        _gateways = gateways;
    }

    /// <summary>
    /// The names of the gateways, in ordinal order: <see cref="InMemoryFusionGateway.DefaultName"/> for the one
    /// <c>AddInMemoryFusionGateway()</c> registers, and the name each named one was given.
    /// </summary>
    public IReadOnlyList<string> GatewayNames => [.. _gateways.Select(gateway => gateway.Name).Order(StringComparer.Ordinal)];

    /// <summary>
    /// The names of the source schemas, in ordinal order: every schema some gateway composes, once, whichever
    /// gateways compose it. A schema of the application no gateway lists is not among them.
    /// </summary>
    public IReadOnlyList<string> SourceSchemaNames
        => [.. _gateways.SelectMany(gateway => gateway.SourceSchemaNames(Executors)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private IRequestExecutorProvider? Executors => _application.GetService<IRequestExecutorProvider>();

    /// <summary>The names of the source schemas the gateway named <paramref name="gateway"/> composes, in ordinal order.</summary>
    /// <exception cref="ArgumentException">No gateway is registered under <paramref name="gateway"/>.</exception>
    public IReadOnlyList<string> SourceSchemaNamesOf(string gateway)
        => [.. Gateway(gateway).SourceSchemaNames(Executors).Order(StringComparer.Ordinal)];

    /// <summary>
    /// The composed schema of the application's one gateway, as its clients see it: the text its endpoint serves
    /// for <c>?sdl</c>. An application with several gateways names the one it prints.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting for the composed schema.</param>
    /// <exception cref="InvalidOperationException">
    /// The application has several gateways; the gateway is not mapped yet; or its source schemas do not compose:
    /// the message says why where the composer did.
    /// </exception>
    public ValueTask<string> PrintGatewayAsync(CancellationToken cancellationToken = default)
        => _gateways.Count == 1
            ? PrintAsync(_gateways[0], cancellationToken)
            : throw new InvalidOperationException(
                $"The application has {_gateways.Count} gateways, {string.Join(" and ", GatewayNames.Select(name => "'" + name + "'"))}: name the one to print.");

    /// <summary>
    /// The composed schema of the gateway named <paramref name="gateway"/>, as its clients see it: the text its
    /// endpoint serves for <c>?sdl</c>.
    /// </summary>
    /// <param name="gateway">The name the gateway was registered under.</param>
    /// <param name="cancellationToken">Stops waiting for the composed schema.</param>
    /// <exception cref="ArgumentException">No gateway is registered under <paramref name="gateway"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The gateway is not mapped yet, or its source schemas do not compose: the message says why where the
    /// composer did.
    /// </exception>
    public ValueTask<string> PrintGatewayAsync(string gateway, CancellationToken cancellationToken = default)
        => PrintAsync(Gateway(gateway), cancellationToken);

    /// <summary>One module's source schema, with the directives the gateways compose by.</summary>
    /// <param name="name">The name the module registered its schema under, one of <see cref="SourceSchemaNames"/>.</param>
    /// <param name="cancellationToken">Stops waiting for the schema to be built.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a source schema's name.</exception>
    public async ValueTask<string> PrintSourceAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var names = SourceSchemaNames;
        if (!names.Contains(name, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                names.Count == 0
                    ? $"'{name}' is not a source schema of this application: it has none."
                    : $"'{name}' is not a source schema of this application. Its source schemas are: {string.Join(", ", names)}.",
                nameof(name));
        }

        var executors = _application.GetRequiredService<IRequestExecutorProvider>();
        var executor = await executors.GetExecutorAsync(name, cancellationToken).ConfigureAwait(false);
        return executor.Schema.ToString()!;
    }

    private static async ValueTask<string> PrintAsync(GatewayRegistration gateway, CancellationToken cancellationToken)
    {
        var executor = await gateway.ComposedAsync(cancellationToken).ConfigureAwait(false);

        // As HotChocolate's endpoint prints a schema: without the directives that are its own bookkeeping.
        return SchemaFormatter.FormatAsString(executor.Schema, new SchemaFormatterOptions { IncludeInternalDirectives = false });
    }

    private GatewayRegistration Gateway(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return _gateways.FirstOrDefault(gateway => gateway.Name == name)
            ?? throw new ArgumentException(
                $"'{name}' is not a gateway of this application. Its gateways are: {string.Join(", ", GatewayNames)}.", nameof(name));
    }
}
