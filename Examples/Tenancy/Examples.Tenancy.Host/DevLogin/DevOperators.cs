namespace Examples.Tenancy.Host.DevLogin;

/// <summary>
/// Whom the dev login signs in as an operator: the demonstration people the host's configuration lists under
/// <see cref="Setting"/>, by their keys. Their tokens carry the operators' role where everyone else's carry a
/// signed-in user's.
/// </summary>
/// <remarks>
/// Who is an operator is never the application's data: it is what the identity provider puts in a token. With
/// Supabase Auth that is a claim the project's owner sets for a member of staff; here, where the dev login stands
/// in for Auth, it is this setting. Nobody is listed unless a settings file says so, and the dev login exists
/// only in Development. A person who is listed and has a seat as well is an operator while signed in this way,
/// and so has no seat: a token role is one or the other.
/// </remarks>
/// <param name="keys">The keys of the people to sign in as operators.</param>
public sealed class DevOperators(IEnumerable<string> keys)
{
    /// <summary>The setting that lists them: <c>DevLogin:Operators</c>, an array of keys such as <c>orla</c>.</summary>
    public const string Setting = "DevLogin:Operators";

    private readonly HashSet<string> _keys = new(keys ?? throw new ArgumentNullException(nameof(keys)), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The operators the configuration lists. A key that names none of the demonstration people stops the host:
    /// a setting that silently made nobody an operator would look like a refusal to whoever tries it.
    /// </summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <exception cref="InvalidOperationException">A listed key is no demonstration person's.</exception>
    public static DevOperators From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var keys = configuration.GetSection(Setting).Get<string[]>() ?? [];
        return keys.FirstOrDefault(key => DemoPeople.Find(key) is null) is { } unknown
            ? throw new InvalidOperationException(Setting + " lists '" + unknown + "', who is not one of the demonstration people.")
            : new DevOperators(keys);
    }

    /// <summary>Whether the dev login signs <paramref name="person"/> in as an operator.</summary>
    public bool Includes(DemoPerson person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return _keys.Contains(person.Key);
    }
}
