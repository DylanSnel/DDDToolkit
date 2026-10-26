using System.Collections.Immutable;
using HotChocolate.Execution;
using HotChocolate.Fusion.Composition;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Fusion.Connectors.InMemory;
using HotChocolate.Fusion.Execution.Clients;
using HotChocolate.Fusion.Logging;
using HotChocolate.Fusion.Options;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Fusion.InMemory;

/// <summary>
/// One gateway of the application: its name, the source schemas it composes, its options, and once it is mapped,
/// the service container it runs in. What <c>AddInMemoryFusionGateway</c> registers and <c>MapInMemoryFusionGateway</c>
/// finds by name.
/// </summary>
/// <param name="name">The gateway's name.</param>
/// <param name="listed">The source schemas it composes; <see langword="null"/> for every schema of the application.</param>
/// <param name="options">Its options.</param>
internal sealed class GatewayRegistration(string name, IReadOnlyList<string>? listed, InMemoryFusionGatewayOptions options)
{
    private readonly TaskCompletionSource _failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _building = new();

    /// <summary>The gateway's name, as it was registered.</summary>
    public string Name { get; } = name;

    /// <summary>The source schemas it composes, as they were listed; <see langword="null"/> for every schema of the application.</summary>
    public IReadOnlyList<string>? Listed { get; } = listed;

    public InMemoryFusionGatewayOptions Options { get; } = options;

    /// <summary>The gateway's own service container, once it is mapped.</summary>
    public IServiceProvider? Services { get; private set; }

    public Exception? CompositionError { get; private set; }

    /// <summary>
    /// The names of the source schemas it composes, in the order they were listed, or, for a gateway of every schema,
    /// the order the application registered them in.
    /// </summary>
    public IReadOnlyList<string> SourceSchemaNames(IRequestExecutorProvider? application)
        => Listed ?? (application is null ? [] : [.. application.SchemaNames]);

    /// <summary>The composer refused the source schemas. It does not compose again, so whoever waits can stop.</summary>
    public void Failed(Exception error)
    {
        CompositionError = error;
        _failed.TrySetResult();
    }

    /// <summary>The source schemas composed, so an error of an earlier attempt no longer describes them.</summary>
    public void Composed() => CompositionError = null;

    public IServiceProvider RequireServices()
        => Services ?? throw new InvalidOperationException(Name == InMemoryFusionGateway.DefaultName
            ? "Call MapInMemoryFusionGateway() on the application."
            : $"The in-memory gateway '{Name}' is registered and not mapped: call MapInMemoryFusionGateway(path, \"{Name}\") on the application.");

    /// <summary>
    /// Builds the gateway's service container over the application's source schemas, once: a gateway mapped at two
    /// paths is one gateway.
    /// </summary>
    /// <param name="application">The application's services, where the source schemas are.</param>
    /// <exception cref="InvalidOperationException">A listed schema is not registered, or there is no source schema.</exception>
    public IServiceProvider Build(IServiceProvider application)
    {
        lock (_building)
        {
            return Services ??= Create(application);
        }
    }

    private ServiceProvider Create(IServiceProvider application)
    {
        // A name listed that no schema has would compose without it, and its fields would be missing from an
        // endpoint without a word. So it fails the start, and says which names there are.
        var sourceSchemas = application.GetService<IRequestExecutorProvider>();
        var registered = sourceSchemas?.SchemaNames ?? [];
        if (Listed is not null)
        {
            var unknown = Listed.Where(listed => !registered.Contains(listed, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
            if (unknown.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The in-memory gateway '{Name}' composes " + string.Join(", ", unknown.Select(name => "'" + name + "'"))
                    + ", which no schema of the application is registered under. The schemas registered are "
                    + (registered.Length == 0 ? "none" : string.Join(", ", registered.Order(StringComparer.Ordinal).Select(name => "'" + name + "'")))
                    + ": name each one as AddGraphQLServer(\"name\") does, in the same case.");
            }
        }

        string[] names = [.. SourceSchemaNames(sourceSchemas)];
        if (sourceSchemas is null || names.Length == 0)
        {
            throw new InvalidOperationException(
                "No source schema is registered. Register each module's with AddGraphQLServer(\"name\").AddSourceSchemaDefaults().");
        }

        var sourceSchemaEvents = application.GetRequiredService<IRequestExecutorEvents>();
        var access = application.GetRequiredService<SchemaKeyAccess>();
        var clientScopes = application.GetRequiredService<ISourceSchemaClientScopeFactory>();

        var composition = new SchemaComposerOptions();
        // Relay across the source schemas: node(id:) answered by whichever one owns the type in the id.
        composition.Merger.EnableGlobalObjectIdentification = Options.EnableGlobalObjectIdentification;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();

        var builder = services
            .AddGraphQLGatewayServer()
            .AddConfigurationProvider(_ =>
            {
                // The connector starts composing in its constructor, and tells only who is listening at that
                // moment that a composition failed: it keeps nothing, and it does not try again. Source schemas
                // that are built without waiting for anything would be composed, and refused, before anybody
                // could subscribe. So the composer is handed no source schema until the listener is there.
                var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var provider = new InMemoryConfigurationProvider(names, new OnceListening(sourceSchemas, names, listening.Task), sourceSchemaEvents, composition);

                try
                {
                    provider.Subscribe(new CompositionErrors(this));
                }
                finally
                {
                    listening.SetResult();
                }

                return provider;
            });

        // The clients for the source schemas, the application's: the same whichever gateway asks (InMemoryClientScopes).
        services.AddSingleton(clientScopes);

        foreach (var name in names)
        {
            FusionSetupUtilities.Configure(builder,
                setup => setup.ClientConfigurationModifiers.Add(_ => new InMemorySourceSchemaClientConfiguration(name)));
        }

        Options.ConfigureGateway?.Invoke(builder);

        // Who reads the schema is the gateway's SchemaReaders' to say, for introspection as for the file, so the two
        // never disagree. HotChocolate's own default asks the host's environment, which this container has not. So
        // the gateway refuses introspection to every request, and the interceptor allows it for one that may read
        // the schema. Both last: DisableIntrospection in ConfigureGateway opens nothing beside the file, and the
        // interceptor wraps whichever the gateway ends up with, the host's own among them.
        builder.DisableIntrospection();
        builder.ConfigureSchemaServices((_, schemaServices) => SchemaKeyInterceptor.Wrap(schemaServices, access, Options.SchemaReaders));

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The gateway's executor once the source schemas are composed. A composition that fails does not
    /// always fail the request for the executor: when the gateway was not listening yet, it waits for a
    /// schema forever. So this stops waiting when the composer said no, or after
    /// <see cref="InMemoryFusionGatewayOptions.CompositionTimeout"/>, and says what is known.
    /// </summary>
    /// <exception cref="InvalidOperationException">The gateway is not mapped, or there is no composed schema.</exception>
    public async Task<IRequestExecutor> ComposedAsync(CancellationToken cancellationToken)
    {
        var executors = RequireServices().GetRequiredService<IRequestExecutorProvider>();
        var composing = executors.GetExecutorAsync(cancellationToken: cancellationToken).AsTask();
        var timeout = Options.CompositionTimeout;

        if (await Task.WhenAny(composing, _failed.Task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false) == composing)
        {
            try
            {
                return await composing.ConfigureAwait(false);
            }
            catch (SchemaCompositionException refused)
            {
                // The gateway was listening too, and heard the composer a moment before this did. It is
                // the same refusal, so it reads the same whichever of the two got there first.
                throw Refused(CompositionError ?? refused);
            }
        }

        // The request for the executor is given up on. If the gateway heard the composer as well, that
        // request fails later, and nobody is left to look at how.
        _ = composing.ContinueWith(
            static abandoned => _ = abandoned.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // Whoever stopped waiting gets a cancellation, not a story about composition.
        cancellationToken.ThrowIfCancellationRequested();

        throw CompositionError is { } error
            ? Refused(error)
            : new InvalidOperationException(
                $"The source schemas{Of()} were not composed into one within {timeout.TotalSeconds:0} seconds. "
                + "Composition failures are not always reported: check that every source schema's root query "
                + "type is called Query, that a type two schemas both declare shares its key, and that fields "
                + "several schemas return are @shareable.");
    }

    /// <summary>The composer's refusal as the application's start and a printed schema report it, with the composer's own exception inside.</summary>
    private InvalidOperationException Refused(Exception error)
        => new($"The source schemas{Of()} could not be composed into one: " + Describe(error), error);

    /// <summary>Which gateway's source schemas, where the application could have more than one.</summary>
    private string Of() => Name == InMemoryFusionGateway.DefaultName ? string.Empty : $" of the gateway '{Name}'";

    /// <summary>Every error the composer logged, where its own message names the first one only.</summary>
    private static string Describe(Exception error)
    {
        if (error is not SchemaCompositionException { CompositionLog: { } log })
        {
            return error.Message;
        }

        var errors = log.Where(static entry => entry.Severity == LogSeverity.Error).Select(static entry => entry.Message).ToArray();
        return errors.Length switch
        {
            0 => error.Message,
            1 => errors[0],
            _ => errors.Length + " errors." + string.Concat(errors.Select(static message => Environment.NewLine + "  - " + message)),
        };
    }

    /// <summary>Keeps the composition error the connector reports and forgets.</summary>
    private sealed class CompositionErrors(GatewayRegistration gateway) : IObserver<FusionConfiguration>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error) => gateway.Failed(error);

        public void OnNext(FusionConfiguration value) => gateway.Composed();
    }

    /// <summary>
    /// The application's source schemas, held back until whoever keeps the composer's errors is listening:
    /// the composer asks for them from its constructor, and is answered once <paramref name="listening"/>
    /// completes, however fast a schema is built. After that it is the application's provider and nothing more,
    /// with the schemas this gateway does not compose left out of its names.
    /// </summary>
    private sealed class OnceListening(IRequestExecutorProvider sourceSchemas, string[] names, Task listening) : IRequestExecutorProvider
    {
        public ImmutableArray<string> SchemaNames { get; } = [.. names];

        public ValueTask<IRequestExecutor> GetExecutorAsync(string? schemaName = null, CancellationToken cancellationToken = default)
            => listening.IsCompletedSuccessfully
                ? sourceSchemas.GetExecutorAsync(schemaName, cancellationToken)
                : WaitAsync(schemaName, cancellationToken);

        private async ValueTask<IRequestExecutor> WaitAsync(string? schemaName, CancellationToken cancellationToken)
        {
            await listening.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await sourceSchemas.GetExecutorAsync(schemaName, cancellationToken).ConfigureAwait(false);
        }
    }
}
