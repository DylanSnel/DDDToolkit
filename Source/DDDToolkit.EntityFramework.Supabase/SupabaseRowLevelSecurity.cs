using DDDToolkit.EntityFramework.Postgres;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// What is Supabase's about row level security, when the rest is Postgres's: the roles PostgREST switches
/// to, the <c>auth</c> functions every project has, which the policies the build exports ask about the
/// caller, and the roles the export writes for and the application switches to unless the project says
/// otherwise.
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
    /// of its own and should do background work as a secret key would, <c>system=service_role</c> in the
    /// project's <c>SupabaseRowAccessRoles</c>.
    /// </summary>
    public const string ServiceRole = "service_role";

    /// <summary>
    /// The role the toolkit's own bookkeeping runs as unless the project says otherwise: the role the system caller
    /// switches to, <see cref="PostgresRowLevelSecurityOptions.SystemRole"/>, and the one the access files make and
    /// give the outbox, the inbox and the migration history, <see cref="RowAccessRoleNames.System"/>. A role of the
    /// application's own, which can neither log in nor bypass row level security, so an application that logs in as
    /// a role that owns nothing does its bookkeeping, and nothing else, when no caller is about.
    /// </summary>
    public const string DefaultSystemRole = "ddd_system";

    /// <summary><c>auth.uid()</c>, <c>auth.role()</c> and <c>auth.jwt()</c>: how a Supabase policy asks about the caller.</summary>
    public static PostgresCallerFunctions CallerFunctions { get; } = new("auth.uid()", "auth.role()", "auth.jwt()");

    /// <summary>
    /// The roles the export writes the policies for, and the roles <c>AddSupabaseRowLevelSecurity</c> switches to,
    /// where the project's <c>SupabaseRowAccessRoles</c> says nothing of them: <c>authenticated</c> for a signed-in
    /// user, <c>anon</c> for a caller without a token, <c>ddd_system_in</c> for the application's own work inside the
    /// policies, and <see cref="DefaultSystemRole"/> for its bookkeeping. No token role is mapped.
    /// <see cref="SupabaseMigrationOptions.Roles"/> starts from these when you export by hand.
    /// </summary>
    public static RowAccessRoleNames DefaultRoles { get; } = RowAccessRoleNames.Default with { System = DefaultSystemRole };
}
