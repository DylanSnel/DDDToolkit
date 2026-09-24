using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;

namespace DDDToolkit.EntityFramework.Integration;

/// <summary>
/// Every payload shape this process can read, keyed by the pair a message carries on the wire: its
/// published name and its version. Plus the upcasters that turn an old shape into the current one.
/// <para>
/// <c>[DomainEventName]</c> keeps the name stable while you rename the class. Nothing kept the
/// <em>shape</em> stable, and the shape is the harder promise: once the outbox has published a payload,
/// somebody has stored it, queued it or is about to read it back. A payload written before a deployment
/// has to stay readable after it.
/// </para>
/// <para>
/// So register the old record next to the new one and say how to get from one to the other. The outbox
/// processor applies the chain when it reads a stored row, and the inbox applies it when a consumer
/// reads a delivered message, so both sides see the current type and neither has to branch on a version
/// number.
/// </para>
/// <code>
/// [IntegrationEvent("library.shelf-opened", Version = 1)]
/// public sealed record ShelfOpenedV1(string ShelfId, string Name);
///
/// [IntegrationEvent("library.shelf-opened", Version = 2)]
/// public sealed record ShelfOpenedV2(string ShelfId, string DisplayName, string Library);
///
/// options.MapIntegrationEvents(contracts => contracts
///     .UpcastFrom&lt;ShelfOpenedV1, ShelfOpenedV2&gt;(v1 =&gt; new ShelfOpenedV2(v1.ShelfId, v1.Name, "unknown")));
/// </code>
/// <para>
/// An upcaster invents the fields the old payload never had. That is unavoidable and it is the reason a
/// version bump is a decision: write the substitute value the consumer can tell apart, not a value that
/// looks like real data.
/// </para>
/// </summary>
public sealed class IntegrationEventContractRegistry
{
    private readonly Dictionary<(string Name, int Version), Type> _types = [];
    private readonly Dictionary<Type, Func<object, object>> _upcasters = [];

    /// <summary>
    /// How payloads are read and written. Defaults to the same options the outbox uses:
    /// case-insensitive, with the toolkit's single value object converter.
    /// </summary>
    public JsonSerializerOptions JsonOptions { get; set; } = IntegrationJson.CreateDefault();

    /// <summary>The (name, version) pairs that can be read back.</summary>
    public IReadOnlyCollection<(string Name, int Version)> Registered => _types.Keys;

    /// <summary>
    /// Registers <typeparamref name="TContract"/> under <paramref name="name"/> at <paramref name="version"/>,
    /// as the compiler read them off its <c>[IntegrationEvent]</c>. This is what the generated
    /// <c>contracts.Add{Module}IntegrationEvents()</c> calls for every contract a module's handlers read;
    /// nothing here asks an attribute.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, or another type already claims the same name and version.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is below 1.</exception>
    public IntegrationEventContractRegistry Register<TContract>(string name, int version) where TContract : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        return Add(typeof(TContract), (name, version));
    }

    /// <summary>Registers <typeparamref name="TContract"/> under its own name and version, read off its attributes.</summary>
    public IntegrationEventContractRegistry Register<TContract>() where TContract : class => Register(typeof(TContract));

    /// <summary>
    /// Registers <paramref name="contractType"/> under the name and version of
    /// <see cref="IntegrationEventContract"/>, read off its attributes at run time: <c>[IntegrationEvent]</c>,
    /// otherwise <c>[DomainEventName]</c> with version 1, otherwise the class name with version 1.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="contractType"/> is null.</exception>
    /// <exception cref="ArgumentException">The type is not concrete, or another type already claims the same name and version.</exception>
    public IntegrationEventContractRegistry Register(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);

        if (contractType.IsAbstract || contractType.IsInterface || contractType.IsGenericTypeDefinition)
        {
            throw new ArgumentException($"'{contractType}' is not a concrete type, so no payload can be read back as it.", nameof(contractType));
        }

        return Add(contractType, (IntegrationEventContract.NameOf(contractType), IntegrationEventContract.VersionOf(contractType)));
    }

    private IntegrationEventContractRegistry Add(Type contractType, (string Name, int Version) key)
    {
        if (_types.TryGetValue(key, out var existing) && existing != contractType)
        {
            throw new ArgumentException(
                $"Both '{existing}' and '{contractType}' are published as '{key.Name}' version {key.Version}. " +
                "Give one of them a different [IntegrationEvent] name or version.",
                nameof(contractType));
        }

        _types[key] = contractType;
        return this;
    }

    /// <summary>Registers every type in <paramref name="assembly"/> that carries <c>[IntegrationEvent]</c>, by reflection.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
    public IntegrationEventContractRegistry RegisterFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetTypes())
        {
            if (!type.IsAbstract && !type.IsInterface && !type.IsGenericTypeDefinition &&
                Attribute.IsDefined(type, typeof(IntegrationEventAttribute), inherit: false))
            {
                Register(type);
            }
        }

        return this;
    }

    /// <summary>Registers every <c>[IntegrationEvent]</c> type in the assembly that declares <typeparamref name="TMarker"/>.</summary>
    public IntegrationEventContractRegistry RegisterFromAssemblyContaining<TMarker>() => RegisterFromAssembly(typeof(TMarker).Assembly);

    /// <summary>
    /// Says how to read a payload written as <typeparamref name="TStored"/> as if it had been written as
    /// <typeparamref name="TNext"/>. Both types are registered, so the old payload stays resolvable by
    /// its own name and version.
    /// <para>
    /// Chains are followed: register v1 to v2 and v2 to v3, and a v1 payload arrives as v3. Register the
    /// step, never the whole jump, so adding v4 later is one more line rather than a rewrite of every
    /// upcaster you already have.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="upcast"/> is null.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TStored"/> already has an upcaster.</exception>
    public IntegrationEventContractRegistry UpcastFrom<TStored, TNext>(Func<TStored, TNext> upcast)
        where TStored : class
        where TNext : class
    {
        ArgumentNullException.ThrowIfNull(upcast);

        // Registered already, by the generated registration or an earlier call, is registered enough:
        // only a shape nothing has named yet is read off its attributes.
        if (!_types.ContainsValue(typeof(TStored)))
        {
            Register(typeof(TStored));
        }

        if (!_types.ContainsValue(typeof(TNext)))
        {
            Register(typeof(TNext));
        }

        if (!_upcasters.TryAdd(typeof(TStored), stored => upcast((TStored)stored)))
        {
            throw new ArgumentException($"'{typeof(TStored)}' already has an upcaster; a stored shape has one successor.", nameof(upcast));
        }

        return this;
    }

    /// <summary>The type registered for <paramref name="name"/> at <paramref name="version"/>.</summary>
    public bool TryResolve(string name, int version, [NotNullWhen(true)] out Type? contractType)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _types.TryGetValue((name, version), out contractType);
    }

    /// <summary>True when <paramref name="payloadType"/> has a successor registered.</summary>
    public bool HasUpcaster(Type payloadType)
    {
        ArgumentNullException.ThrowIfNull(payloadType);
        return _upcasters.ContainsKey(payloadType);
    }

    /// <summary>
    /// Applies every registered upcaster in turn until <paramref name="payload"/> is of a type that has
    /// no successor. A payload whose type has no upcaster is returned unchanged.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The upcasters form a cycle, or one of them returned null.</exception>
    public object Upcast(object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var seen = new HashSet<Type>();

        while (_upcasters.TryGetValue(payload.GetType(), out var upcast))
        {
            if (!seen.Add(payload.GetType()))
            {
                throw new InvalidOperationException(
                    $"The upcasters starting at '{payload.GetType()}' form a cycle. Every upcaster must move a payload forwards to a shape it has not been.");
            }

            payload = upcast(payload)
                ?? throw new InvalidOperationException($"The upcaster registered for '{payload.GetType()}' returned null. An upcaster converts a payload; it cannot drop it.");
        }

        return payload;
    }

    /// <summary>
    /// Reads <paramref name="message"/> as the current shape of its contract: resolve the type from the
    /// message's name and version, deserialize the payload as that type, then upcast.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Nothing is registered for that name and version.</exception>
    /// <exception cref="JsonException">The payload did not deserialize.</exception>
    public object Read(IntegrationEventMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!TryRead(message, out var contract))
        {
            throw new InvalidOperationException(
                $"No contract is registered under the name '{message.Name}' at version {message.Version}. " +
                $"Register it with contracts.{nameof(Register)}<T>() or say how to read it with contracts.{nameof(UpcastFrom)}<TOld, TNew>(...).");
        }

        return contract;
    }

    /// <summary>
    /// Reads <paramref name="message"/> as the current shape of its contract, or returns
    /// <see langword="false"/> when nothing is registered under its name and version.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="JsonException">The payload did not deserialize.</exception>
    public bool TryRead(IntegrationEventMessage message, [NotNullWhen(true)] out object? contract)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!TryResolve(message.Name, message.Version, out var storedType))
        {
            contract = null;
            return false;
        }

        contract = Upcast(Deserialize(message.Payload, storedType, message.Name, message.Version));
        return true;
    }

    /// <summary>
    /// Reads <paramref name="message"/> and hands it back as <typeparamref name="TContract"/>, which is
    /// the shape the upcasters end at.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Nothing is registered for that name and version, or the chain does not end at <typeparamref name="TContract"/>.</exception>
    public TContract Read<TContract>(IntegrationEventMessage message) where TContract : class
    {
        var contract = Read(message);

        return contract as TContract
            ?? throw new InvalidOperationException(
                $"'{message.Name}' version {message.Version} was read as '{contract.GetType()}', which is not a '{typeof(TContract)}'. " +
                $"Register an upcaster from '{contract.GetType()}' so the chain ends at the shape you asked for.");
    }

    /// <summary>
    /// Deserializes <paramref name="payload"/> as <paramref name="payloadType"/> and upcasts it, for a
    /// payload whose type you already resolved yourself.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> or <paramref name="payloadType"/> is null.</exception>
    public object ReadAs(string payload, Type payloadType, string name, int version)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(payloadType);

        return Upcast(Deserialize(payload, payloadType, name, version));
    }

    private object Deserialize(string payload, Type payloadType, string name, int version)
        => JsonSerializer.Deserialize(payload, payloadType, JsonOptions)
            ?? throw new JsonException($"The payload of '{name}' version {version} deserialized to null as '{payloadType}'.");
}
