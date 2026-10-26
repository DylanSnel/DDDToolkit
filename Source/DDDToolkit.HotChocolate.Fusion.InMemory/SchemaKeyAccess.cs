using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.HotChocolate.Fusion.InMemory;

/// <summary>
/// Whether a request may read a gateway's schema, as the gateway's <see cref="SchemaReaders"/> say: by default in
/// Development every request, elsewhere one that carries the application's <see cref="GraphQLSchemaKey"/>. Asked by
/// the three places that decide something about a schema request: authorization, the schema file and introspection.
/// </summary>
/// <remarks>
/// Whether a request carries the key is kept on the request, so a request is judged once, and a wrong key is logged
/// once, whoever asks first. The key is read from configuration on every judgement, so a key that is rotated in a
/// store that reloads is the key from then on; its hash is kept until it changes. Nothing of the key, the one sent or
/// the one configured, is logged or put in a message.
/// </remarks>
internal sealed class SchemaKeyAccess(IConfiguration? configuration, IHostEnvironment? environment, ILogger? logger)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private ConfiguredKey? _configured;

    /// <summary>
    /// Whether the application runs in Development. Without an environment it is taken not to: a schema then takes
    /// the key, which is the safe side to err on.
    /// </summary>
    public bool InDevelopment => environment?.IsDevelopment() == true;

    /// <summary>
    /// Whether <paramref name="context"/> may read the schema of a gateway whose readers are <paramref name="readers"/>.
    /// A value no member of <see cref="SchemaReaders"/> has reads nothing.
    /// </summary>
    public bool MayReadSchema(HttpContext context, SchemaReaders readers) => readers switch
    {
        SchemaReaders.DevelopmentOrKey => InDevelopment || CarriesTheKey(context),
        SchemaReaders.KeyOnly => CarriesTheKey(context),
        SchemaReaders.Everyone => true,
        _ => false,
    };

    /// <summary>
    /// Fails when the configured key is too short to be a secret. Called when a gateway is mapped, so a weak key
    /// stops the start rather than opening the schema; the message says how long it is and nothing else of it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The key is shorter than <see cref="GraphQLSchemaKey.MinimumLength"/>.</exception>
    public void EnsureUsable()
    {
        if (configuration?[GraphQLSchemaKey.Setting]?.Trim() is { Length: > 0 and < GraphQLSchemaKey.MinimumLength } weak)
        {
            throw new InvalidOperationException(
                $"{GraphQLSchemaKey.Setting} is {weak.Length} characters long; a schema key has at least {GraphQLSchemaKey.MinimumLength}, since whoever guesses it reads the schema. "
                + "Make one with `openssl rand -base64 32`, keep it where the host reads its secrets, the environment variable GraphQL__SchemaKey or its secret store, "
                + "and give the tool the same secret.");
        }
    }

    private bool CarriesTheKey(HttpContext context)
    {
        if (context.Features.Get<Judged>() is { } judged)
        {
            return judged.CarriesTheKey;
        }

        var carries = Judge(context);
        context.Features.Set(new Judged(carries));
        return carries;
    }

    private bool Judge(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(GraphQLSchemaKey.HeaderName, out var sent))
        {
            return false;
        }

        var configured = Configured();
        if (configured is null)
        {
            _logger.LogWarning(
                "A request to {Path} sent {Header}, and the application has no key at {Setting}: it is answered as a request without one.",
                context.Request.Path.Value, GraphQLSchemaKey.HeaderName, GraphQLSchemaKey.Setting);
            return false;
        }

        if (sent.Count != 1 || !CryptographicOperations.FixedTimeEquals(Hash(sent[0]?.Trim() ?? string.Empty), configured.Hash))
        {
            _logger.LogWarning(
                "A request to {Path} sent a {Header} that is not the key at {Setting}: it is answered as a request without one.",
                context.Request.Path.Value, GraphQLSchemaKey.HeaderName, GraphQLSchemaKey.Setting);
            return false;
        }

        return true;
    }

    /// <summary>The configured key's hash, or <see langword="null"/> when there is no key, or none long enough.</summary>
    private ConfiguredKey? Configured()
    {
        var value = configuration?[GraphQLSchemaKey.Setting]?.Trim();
        if (value is not { Length: >= GraphQLSchemaKey.MinimumLength })
        {
            return null;
        }

        var configured = _configured;
        if (configured is null || !string.Equals(configured.Value, value, StringComparison.Ordinal))
        {
            configured = new ConfiguredKey(value, Hash(value));
            _configured = configured;
        }

        return configured;
    }

    /// <summary>
    /// Both keys are compared as their hashes, so the comparison takes as long whatever their lengths, and a wrong
    /// key's length is not told by how fast it is refused.
    /// </summary>
    private static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    /// <summary>The configured key and its hash. A class and no record, so nothing prints the key by accident.</summary>
    private sealed class ConfiguredKey(string value, byte[] hash)
    {
        public string Value { get; } = value;

        public byte[] Hash { get; } = hash;
    }

    /// <summary>The judgement of one request's key, kept on it.</summary>
    private sealed record Judged(bool CarriesTheKey);
}
