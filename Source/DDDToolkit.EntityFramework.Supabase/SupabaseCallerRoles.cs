using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// The database role each kind of caller runs as, as the project says it once, in <c>SupabaseRowAccessRoles</c>:
/// <c>user=…|anonymous=…|system-in=…|system=…</c> and a <c>token:&lt;role&gt;=…</c> pair for each token role the host
/// maps, each pair optional and each left out a default, <see cref="SupabaseRowLevelSecurity.DefaultRoles"/>. The
/// export writes the policies for these roles, and <c>AddSupabaseRowLevelSecurity</c> switches to them, so a project
/// that deviates from the defaults says so in one place and nowhere in code.
/// <para>
/// Two values of <c>system</c> say the system caller has no bookkeeping role of the application's own: <c>none</c>, it
/// runs as the role the application logs in as, and one of Postgres's or Supabase's own roles, <c>service_role</c> for
/// one, which it switches to and which bypasses the policies. Either way the access files make and grant no
/// bookkeeping role, so <see cref="Names"/> has none, and <see cref="SystemCaller"/> says what the system caller
/// runs as.
/// </para>
/// </summary>
internal sealed class SupabaseCallerRoles
{
    /// <summary>The project property the roles come from, as an error names it, and the key the build records them under.</summary>
    internal const string Property = "SupabaseRowAccessRoles";

    /// <summary>The value of <c>system</c> that says the system caller runs as the role the application logs in as.</summary>
    internal const string LoginRole = "none";

    /// <summary>A value that sets every key, for a problem to show.</summary>
    internal const string Example = "user=authenticated|anonymous=anon|system-in=ddd_system_in|system=ddd_system";

    /// <summary>What the key of a pair that maps a token role starts with, before the token role: <c>token:analyst=desk_analyst</c>.</summary>
    internal const string TokenKey = "token:";

    /// <summary>What the comment on the <c>ddd</c> schema starts with, before the roles the access files were written for.</summary>
    internal const string RecordPrefix = "DDDToolkit row access roles: ";

    private SupabaseCallerRoles(RowAccessRoleNames names, string? systemCaller, bool systemSaid)
    {
        Names = names;
        SystemCaller = systemCaller;
        SystemSaid = systemSaid;
    }

    /// <summary>The defaults: <see cref="SupabaseRowLevelSecurity.DefaultRoles"/>, the system caller as its bookkeeping role.</summary>
    internal static SupabaseCallerRoles Default { get; } = new(SupabaseRowLevelSecurity.DefaultRoles, SupabaseRowLevelSecurity.DefaultSystemRole, systemSaid: false);

    /// <summary>
    /// The roles the export writes for: <see cref="RowAccessRoleNames.System"/> is the bookkeeping role the access
    /// files make and give the toolkit's tables, or <see langword="null"/> where the system caller has none of the
    /// application's own.
    /// </summary>
    internal RowAccessRoleNames Names { get; }

    /// <summary>
    /// The role the system caller switches to, <see cref="PostgresRowLevelSecurityOptions.SystemRole"/>: the bookkeeping
    /// role, one of the platform's, or <see langword="null"/> for the role the application logs in as.
    /// </summary>
    internal string? SystemCaller { get; }

    /// <summary>Whether the property said what the system caller runs as, rather than leaving it the default.</summary>
    internal bool SystemSaid { get; }

    /// <summary>
    /// Reads the property's <paramref name="value"/>, every key it leaves out a default, or says what is wrong with
    /// it, naming the property: an unknown or repeated key, a pair without a key or a value, a role no policy can be
    /// for, a scoped system role or a bookkeeping role that is another caller's as well, or a token role mapped to a
    /// role a token must never run as. Empty or white space is the defaults.
    /// </summary>
    internal static bool TryParse(string? value, out SupabaseCallerRoles roles, out string problem)
    {
        roles = Default;
        if (!SupabaseMigrationBuild.TryReadPairs(Property, value, ["user", "anonymous", "system-in", "system"], Example, TokenKey, out var role, out var tokenRoles, out problem))
        {
            return false;
        }

        var defaults = SupabaseRowLevelSecurity.DefaultRoles;
        RowAccessRoleNames names;
        try
        {
            names = new RowAccessRoleNames(
                role.GetValueOrDefault("user") ?? defaults.User,
                role.GetValueOrDefault("anonymous") ?? defaults.Anonymous,
                role.GetValueOrDefault("system-in") ?? defaults.SystemIn);
        }
        catch (ArgumentException exception)
        {
            // The parameter the exception names is the key the property set; where the property left that key out,
            // its default clashes with a role the property did set, which is the one to name.
            var key = exception.ParamName switch
            {
                nameof(RowAccessRoleNames.User) => "user",
                nameof(RowAccessRoleNames.Anonymous) => "anonymous",
                _ => "system-in",
            };
            if (!role.ContainsKey(key))
            {
                key = role.First(pair => !string.Equals(pair.Key, "system", StringComparison.OrdinalIgnoreCase) && string.Equals(pair.Value, defaults.SystemIn, StringComparison.Ordinal)).Key;
            }

            var example = key.ToLowerInvariant() switch
            {
                "user" => PostgresRowLevelSecurityOptions.AuthenticatedRole,
                "anonymous" => PostgresRowLevelSecurityOptions.AnonRole,
                _ => PostgresRowLevelSecurityOptions.DefaultSystemInRole,
            };

            problem = $"{Property} has '{key}={role[key]}'. {SupabaseMigrationBuild.Reason(exception)} Use a role such as {example}.";
            return false;
        }

        try
        {
            // Resolving a role checks the token roles against the others: none is mapped to the scoped system role or
            // to the anonymous caller's.
            names = names with { TokenRoles = tokenRoles };
            names.Resolve(RowAccessRoles.User);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            problem = $"{Property} has a token role it cannot map. {(exception is ArgumentException argument ? SupabaseMigrationBuild.Reason(argument) : exception.Message)}";
            return false;
        }

        if (!role.TryGetValue("system", out var system))
        {
            // The bookkeeping role is the default then, which no caller may run as either: a role the property gives
            // a caller is checked against it as against one the property names.
            var withDefault = names with { System = defaults.System };
            try
            {
                withDefault.Resolve(RowAccessRoles.System);
            }
            catch (ArgumentException exception)
            {
                var pair = role.Where(each => string.Equals(each.Value, defaults.System, StringComparison.Ordinal)).Select(each => $"{each.Key}={each.Value}")
                    .Concat(tokenRoles.Where(each => string.Equals(each.Value, defaults.System, StringComparison.Ordinal)).Select(each => $"{TokenKey}{each.Key}={each.Value}"))
                    .First();
                problem = $"{Property} has '{pair}' and no 'system' pair, so the bookkeeping runs as its default, {defaults.System}. {SupabaseMigrationBuild.Reason(exception)} " +
                          $"Name another role there, or say what the bookkeeping runs as: system=<a role of its own>, or system={LoginRole}.";
                return false;
            }

            roles = new(withDefault, defaults.System, systemSaid: false);
            return true;
        }

        if (string.Equals(system, LoginRole, StringComparison.OrdinalIgnoreCase))
        {
            roles = new(names, systemCaller: null, systemSaid: true);
            return true;
        }

        // The role the system caller runs as is one of its own, which the other roles are known to tell by now; one
        // of the platform's is checked the same way, so no caller's role is the system's as well.
        RowAccessRoleNames withSystem;
        try
        {
            withSystem = names with { System = system };
        }
        catch (ArgumentException exception)
        {
            problem = $"{Property} has 'system={system}'. {SupabaseMigrationBuild.Reason(exception)} Use a role such as {SupabaseRowLevelSecurity.DefaultSystemRole}.";
            return false;
        }

        try
        {
            withSystem.Resolve(RowAccessRoles.System);
        }
        catch (ArgumentException exception)
        {
            problem = $"{Property} has 'system={system}'. {SupabaseMigrationBuild.Reason(exception)}";
            return false;
        }

        // One of Postgres's or Supabase's own roles, service_role for one, is the platform's: the system caller runs
        // as it, past the policies, and the access files neither make it nor give it the toolkit's tables.
        roles = SupabaseMigrations.IsPlatformRole(system)
            ? new(names, system, systemSaid: true)
            : new(withSystem, system, systemSaid: true);
        return true;
    }

    /// <summary>
    /// Sets <paramref name="options"/>' roles to these: the user's, the anonymous caller's, the scoped system role, the
    /// system caller's and the mapped token roles. What the host's own code sets afterwards wins.
    /// </summary>
    internal void ApplyTo(PostgresRowLevelSecurityOptions options)
    {
        options.UserRole = Names.User;
        options.AnonymousRole = Names.Anonymous;
        options.SystemInRole = Names.SystemIn;
        options.SystemRole = SystemCaller;
        foreach (var (tokenRole, role) in Names.TokenRoles)
        {
            options.TokenRoles[tokenRole] = role;
        }
    }

    /// <summary>
    /// The roles the build recorded in the application from its <c>SupabaseRowAccessRoles</c>, the defaults where it
    /// recorded none. It looks in <paramref name="calling"/>, the assembly whose code registers row level security,
    /// which is the host's where the host's own code does; then in the application the host's environment in
    /// <paramref name="services"/> names, <see cref="IHostEnvironment.ApplicationName"/>, which is the host's where a
    /// library of the application registers it, also where a test runs the host in a process of the test's, as
    /// <c>WebApplicationFactory</c> does, which names the host's assembly there; and last in the entry assembly, for
    /// services built without a host.
    /// </summary>
    /// <exception cref="InvalidOperationException">The recorded value is one the export would refuse; the message says why, and which assembly holds it.</exception>
    internal static SupabaseCallerRoles RecordedFor(Assembly? calling, IServiceCollection services)
    {
        foreach (var assembly in new[] { calling, ApplicationOf(services), Assembly.GetEntryAssembly() }.OfType<Assembly>().Distinct())
        {
            if (RecordedIn(assembly) is { } recorded)
            {
                return recorded;
            }
        }

        return Default;
    }

    /// <summary>
    /// The assembly the host's environment names as the application, where <paramref name="services"/> hold one:
    /// <c>WebApplication.CreateBuilder</c>, <c>Host.CreateApplicationBuilder</c> and a generic host put it there before
    /// the application's own code runs. <see langword="null"/> where they hold none, or name no assembly that loads.
    /// </summary>
    private static Assembly? ApplicationOf(IServiceCollection services)
    {
        var environment = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IHostEnvironment) && !descriptor.IsKeyedService)?.ImplementationInstance;
        if (environment is not IHostEnvironment { ApplicationName: { Length: > 0 } name })
        {
            return null;
        }

        if (AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, name, StringComparison.Ordinal)) is { } loaded)
        {
            return loaded;
        }

        try
        {
            return Assembly.Load(new AssemblyName(name));
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException or ArgumentException)
        {
            // A name of the host's own choosing, WebApplicationOptions.ApplicationName for one, that is no assembly.
            return null;
        }
    }

    /// <summary>
    /// The roles the build recorded in <paramref name="assembly"/>, or <see langword="null"/> where it recorded none:
    /// <c>[assembly: AssemblyMetadata("SupabaseRowAccessRoles", "…")]</c>, which the package's targets write into an
    /// application whose project sets the property.
    /// </summary>
    /// <exception cref="InvalidOperationException">The recorded value is one the export would refuse.</exception>
    internal static SupabaseCallerRoles? RecordedIn(Assembly assembly)
    {
        var value = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, Property, StringComparison.Ordinal))?.Value;
        if (value is null)
        {
            return null;
        }

        return TryParse(value, out var roles, out var problem)
            ? roles
            : throw new InvalidOperationException(
                $"The build recorded the roles of {Property} in {assembly.GetName().Name}, and they cannot be used: {problem} Fix the property in that project's file, and build it again.");
    }

    /// <summary>
    /// What an access file ends with: a block that records on the <c>ddd</c> schema, as its comment, the roles the file
    /// was written for, <paramref name="roles"/>, so the application can compare its own with them when it starts. The
    /// comment is set only where it says something else, as the rest of a file makes or grants only what is missing,
    /// and every role reads it from the catalogs without a privilege.
    /// </summary>
    internal static string RecordStatement(RowAccessRoleNames roles)
    {
        var recorded = RecordPrefix + RecordOf(roles);
        return new StringBuilder()
            .Append("-- The roles the policies and the privileges above are written for, as the project that exports says them in").Append('\n')
            .Append("-- SupabaseRowAccessRoles, recorded on the ddd schema: the application compares the roles it switches to with").Append('\n')
            .Append("-- them when it starts, in the start-up check supabase.roles-match-access-files.").Append('\n')
            .Append("DO $ddd$").Append('\n')
            .Append("DECLARE").Append('\n')
            .Append("    recorded constant text := ").Append('\'').Append(recorded.Replace("'", "''", StringComparison.Ordinal)).Append("';").Append('\n')
            .Append("BEGIN").Append('\n')
            .Append("    IF pg_catalog.to_regnamespace('ddd') IS NULL THEN").Append('\n')
            .Append("        CREATE SCHEMA ddd;").Append('\n')
            .Append("    END IF;").Append('\n')
            .Append("    IF pg_catalog.obj_description(pg_catalog.to_regnamespace('ddd')::pg_catalog.oid, 'pg_namespace') IS DISTINCT FROM recorded THEN").Append('\n')
            .Append("        EXECUTE 'COMMENT ON SCHEMA ddd IS ' || pg_catalog.quote_literal(recorded);").Append('\n')
            .Append("    END IF;").Append('\n')
            .Append("END").Append('\n')
            .Append("$ddd$;").Append('\n')
            .ToString();
    }

    /// <summary>
    /// <paramref name="roles"/> as the comment records them: an object of the keys of <c>SupabaseRowAccessRoles</c>,
    /// <c>system</c> <see langword="null"/> where the files make no bookkeeping role, and <c>token</c> an object of the
    /// mapped token roles in their order. JSON rather than the property's pairs, since a role written by hand may hold
    /// a <c>|</c> or an <c>=</c>.
    /// </summary>
    internal static string RecordOf(RowAccessRoleNames roles)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteString("user", roles.User);
            json.WriteString("anonymous", roles.Anonymous);
            json.WriteString("system-in", roles.SystemIn);
            if (roles.System is { } system)
            {
                json.WriteString("system", system);
            }
            else
            {
                json.WriteNull("system");
            }

            json.WriteStartObject("token");
            foreach (var (tokenRole, role) in roles.TokenRoles.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                json.WriteString(tokenRole, role);
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// The roles a comment on the <c>ddd</c> schema records, or <see langword="null"/> for a comment that records none:
    /// none at all, one of somebody else's, or one this version cannot read.
    /// </summary>
    internal static RecordedRoles? ReadRecord(string? comment)
    {
        if (comment is null || !comment.StartsWith(RecordPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(comment[RecordPrefix.Length..]);
            var root = document.RootElement;
            var tokens = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.Object)
            {
                foreach (var mapped in token.EnumerateObject())
                {
                    tokens[mapped.Name] = mapped.Value.GetString() ?? "";
                }
            }

            return new RecordedRoles(
                root.GetProperty("user").GetString() ?? "",
                root.GetProperty("anonymous").GetString() ?? "",
                root.GetProperty("system-in").GetString() ?? "",
                root.TryGetProperty("system", out var system) && system.ValueKind == JsonValueKind.String ? system.GetString() : null,
                tokens);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The roles the access files in a database were written for, as their comment on the <c>ddd</c> schema records them.</summary>
    /// <param name="User">The role a signed-in user's policies are for.</param>
    /// <param name="Anonymous">The role a caller without a token's policies are for.</param>
    /// <param name="SystemIn">The scoped system role.</param>
    /// <param name="System">The bookkeeping role the files made and gave the toolkit's tables, or <see langword="null"/> for none.</param>
    /// <param name="TokenRoles">The mapped token roles, each with its role.</param>
    internal sealed record RecordedRoles(string User, string Anonymous, string SystemIn, string? System, IReadOnlyDictionary<string, string> TokenRoles);
}
