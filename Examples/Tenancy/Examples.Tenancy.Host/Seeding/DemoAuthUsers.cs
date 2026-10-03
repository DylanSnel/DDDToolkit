using DDDToolkit.Auth.Supabase;

namespace Examples.Tenancy.Host.Seeding;

/// <summary>
/// Makes the demonstration people users of Supabase Auth when <c>Sample:SeedAuthUsers</c> is on, each under the
/// fixed id their seats are found by, so a person who signs in there with a password is the person the dev
/// login signs in with a click.
/// </summary>
/// <remarks>
/// <para>
/// It goes through Auth's admin API, with the admin client of <c>DDDToolkit.Auth.Supabase</c>, and never
/// writes a row of Auth's by hand: a person who is not there yet is made, with their address counted as proven,
/// since nobody reads mail on <c>example.test</c>; one who is there gets the password again. Nobody is found by
/// their address: the id is what links a user to a seat.
/// </para>
/// <para>
/// Like the dev login, this is for a developer's machine: nine users who share one password are a hole
/// anywhere else. With the setting on, the host does not start outside Development, without Auth's secret
/// key, or with a password shorter than <see cref="MinimumPasswordLength"/> characters. The password comes
/// from the settings and is never written down here: the AppHost generates one, and a developer who runs the
/// host alone keeps one in user-secrets.
/// </para>
/// <para>
/// It awaits everything inside <see cref="StartAsync"/>, so the host answers no request before the people can
/// sign in.
/// </para>
/// </remarks>
/// <param name="auth">Auth's admin API.</param>
/// <param name="password">The password every demonstration person signs in with.</param>
/// <param name="logger">Says who was made.</param>
public sealed class DemoAuthUsers(SupabaseAuthAdmin auth, string password, ILogger<DemoAuthUsers> logger) : IHostedService
{
    /// <summary>The setting that turns this on.</summary>
    public const string Setting = "Sample:SeedAuthUsers";

    /// <summary>The setting that holds the password the demonstration people sign in with.</summary>
    public const string PasswordSetting = "Sample:DemoPassword";

    /// <summary>The setting that holds the key Auth's admin API takes: a project's secret key.</summary>
    public const string SecretKeySetting = "Supabase:SecretKey";

    /// <summary>The shortest password the demonstration people are given.</summary>
    public const int MinimumPasswordLength = 12;

    /// <summary>
    /// Registers the admin client and this service when <see cref="Setting"/> is on, and nothing otherwise.
    /// Throws when it is on where it must not be, naming everything that is wrong, so the host does not start.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="environment">The host's environment.</param>
    /// <exception cref="InvalidOperationException">The setting is on outside Development, or without a secret key or a long enough password.</exception>
    public static IServiceCollection AddTo(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (!configuration.GetValue<bool>(Setting))
        {
            return services;
        }

        var secretKey = configuration[SecretKeySetting];
        var password = configuration[PasswordSetting];

        var problems = new List<string>();
        if (!environment.IsDevelopment())
        {
            problems.Add("the environment is " + environment.EnvironmentName + ", not Development");
        }

        if (string.IsNullOrEmpty(secretKey))
        {
            problems.Add(SecretKeySetting + " is not set, and Auth's admin API takes nothing else");
        }

        if (password is null || password.Length < MinimumPasswordLength)
        {
            problems.Add(PasswordSetting + " is not set, or shorter than " + MinimumPasswordLength + " characters");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The demonstration people are to be made users of Supabase Auth (" + Setting + "), all with one password, so the host does not start while "
                + string.Join("; ", problems) + ". Turn the setting off, or run it on a developer's machine with the three settings in user-secrets.");
        }

        services.AddSupabaseAuthAdmin(SampleAuthentication.AuthUrlOf(configuration), secretKey!);
        services.AddHostedService(provider => new DemoAuthUsers(
            provider.GetRequiredService<SupabaseAuthAdmin>(),
            password!,
            provider.GetRequiredService<ILogger<DemoAuthUsers>>()));
        return services;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var made = new List<string>();
        foreach (var person in DemoPeople.All)
        {
            if (await auth.FindUserAsync(person.Id, cancellationToken) is not null)
            {
                await auth.UpdateUserAsync(person.Id, new SupabaseUserChange(Password: password), cancellationToken);
                continue;
            }

            await auth.CreateUserAsync(
                new SupabaseNewUser(
                    person.Email,
                    person.Id,
                    password,
                    EmailConfirmed: true,
                    UserMetadata: new Dictionary<string, object?> { ["name"] = person.Name }),
                cancellationToken);
            made.Add(person.Key);
        }

        logger.LogInformation(
            "The demonstration people can sign in at Supabase Auth with the password of {Setting}; made just now: {Made}.",
            PasswordSetting,
            made.Count == 0 ? "nobody, they were all there" : string.Join(", ", made));
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
