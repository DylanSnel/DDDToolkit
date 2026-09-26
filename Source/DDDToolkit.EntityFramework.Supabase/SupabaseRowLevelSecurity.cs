using DDDToolkit.EntityFramework.Postgres;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// What is Supabase's about row level security, when the rest is Postgres's: the roles PostgREST switches
/// to, and the <c>auth</c> functions every project has, which the policies the build exports ask about the
/// caller.
/// </summary>
public static class SupabaseRowLevelSecurity
{
    /// <summary>The role PostgREST gives a signed-in user.</summary>
    public const string AuthenticatedRole = "authenticated";

    /// <summary>The role PostgREST gives a request without a user.</summary>
    public const string AnonRole = "anon";

    /// <summary>
    /// The role behind Supabase's secret keys, which bypasses row level security: the
    /// <see cref="PostgresRowLevelSecurityOptions.SystemRole"/> for an application that logs in as a role
    /// of its own and should do background work as a secret key would.
    /// </summary>
    public const string ServiceRole = "service_role";

    /// <summary><c>auth.uid()</c>, <c>auth.role()</c> and <c>auth.jwt()</c>: how a Supabase policy asks about the caller.</summary>
    public static PostgresCallerFunctions CallerFunctions { get; } = new("auth.uid()", "auth.role()", "auth.jwt()");
}
