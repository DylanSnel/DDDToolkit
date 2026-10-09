using System.Text;

namespace Examples.Tenancy.Host.Auth;

/// <summary>
/// Keeps the dev login where it belongs. The dev login hands anyone a signed access token for any of the
/// demonstration people, which is the point on a developer's machine and a hole anywhere else.
/// </summary>
/// <remarks>
/// With <c>DevLogin:Enabled</c> set, the host refuses to start unless all of these hold:
/// <list type="bullet">
/// <item>the environment is Development;</item>
/// <item><c>Supabase:JwtSecret</c> is set, and at least <see cref="MinimumSecretBytes"/> bytes long, since the
/// tokens are signed with it;</item>
/// <item>the host of <c>Supabase:Url</c> is this machine, so the tokens name a local project as their issuer
/// and no real project's API accepts them.</item>
/// </list>
/// With the dev login off, none of this is asked: a real Supabase project, with its URL and no secret (the
/// bearer then checks tokens against the project's published keys), starts in any environment.
/// </remarks>
public static class DevLoginGuard
{
    /// <summary>The setting that turns the dev login on.</summary>
    public const string EnabledSetting = "DevLogin:Enabled";

    /// <summary>The shortest secret HS256 is given: its key is as long as its hash, 256 bits.</summary>
    public const int MinimumSecretBytes = 32;

    /// <summary>
    /// Whether the dev login is on. Throws when it is on and any condition above fails, naming every one that
    /// does, so the host does not start.
    /// </summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="environment">The host's environment.</param>
    /// <exception cref="InvalidOperationException">The dev login is on where it must not be.</exception>
    public static bool Check(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (!configuration.GetValue<bool>(EnabledSetting))
        {
            return false;
        }

        var problems = ProblemsWith(configuration, environment);
        return problems.Count == 0
            ? true
            : throw new InvalidOperationException(
                "The dev login is on (" + EnabledSetting + "), and it signs a token for anyone who asks, so the host does not start while "
                + string.Join("; ", problems) + ". Turn the dev login off, or run it on a developer's machine against the local Supabase stack.");
    }

    /// <summary>Everything that makes the dev login unsafe here, one clause each; empty when it is safe.</summary>
    private static List<string> ProblemsWith(IConfiguration configuration, IHostEnvironment environment)
    {
        var problems = new List<string>();

        if (!environment.IsDevelopment())
        {
            problems.Add("the environment is " + environment.EnvironmentName + ", not Development");
        }

        var secret = configuration[SampleAuthentication.JwtSecretSetting];
        if (string.IsNullOrEmpty(secret))
        {
            problems.Add(SampleAuthentication.JwtSecretSetting + " is not set, and the tokens are signed with it");
        }
        else if (Encoding.UTF8.GetByteCount(secret) < MinimumSecretBytes)
        {
            problems.Add(SampleAuthentication.JwtSecretSetting + " is shorter than " + MinimumSecretBytes + " bytes");
        }

        var url = configuration[SampleAuthentication.UrlSetting];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var project) || !project.IsLoopback)
        {
            problems.Add(SampleAuthentication.UrlSetting + " (" + (url ?? "not set") + ") is not on this machine");
        }

        return problems;
    }
}
