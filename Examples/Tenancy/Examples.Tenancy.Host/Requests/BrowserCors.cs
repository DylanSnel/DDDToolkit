namespace Examples.Tenancy.Host.Requests;

/// <summary>
/// Lets a browser application on another origin call the API: the origins in <c>Sample:Cors:Origins</c>, and
/// nothing at all when that setting is empty.
/// </summary>
/// <remarks>
/// A browser asks before it sends a request with a header of its own, so the policy names what such a client
/// sends, the token, the tenant, a language and the version it read, and what it must be able to read from an
/// answer, the version and where a new thing is. No credentials: the token travels in a header the client sets,
/// not in a cookie the browser adds.
/// </remarks>
public static class BrowserCors
{
    /// <summary>The setting that lists the origins.</summary>
    public const string OriginsSetting = "Sample:Cors:Origins";

    /// <summary>The policy's name.</summary>
    public const string Policy = "tenancy-browser";

    /// <summary>The origins the configuration lists; none when the setting is absent.</summary>
    /// <param name="configuration">The host's configuration.</param>
    public static string[] Origins(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return [.. (configuration.GetSection(OriginsSetting).Get<string[]>() ?? []).Where(origin => !string.IsNullOrWhiteSpace(origin))];
    }

    /// <summary>Registers the policy for <paramref name="origins"/>; nothing when there are none.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="origins">The origins that may call.</param>
    public static IServiceCollection AddBrowserCors(this IServiceCollection services, string[] origins)
    {
        ArgumentNullException.ThrowIfNull(origins);

        return origins.Length == 0
            ? services
            : services.AddCors(options => options.AddPolicy(Policy, policy => policy
                .WithOrigins(origins)
                .WithHeaders("Authorization", TenantHeader.Name, "Accept-Language", "If-Match", "Content-Type")
                .WithMethods("GET", "POST", "PUT", "DELETE")
                .WithExposedHeaders("ETag", "Location")));
    }
}
