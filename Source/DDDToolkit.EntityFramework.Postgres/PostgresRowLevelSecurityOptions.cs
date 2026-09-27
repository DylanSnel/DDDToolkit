using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// Which Postgres role each kind of <see cref="Caller"/> gets. The defaults are PostgREST's, which is
/// also what every Supabase project has, so policies written for Supabase's Data API apply unchanged, and
/// <see cref="PostgresRowAccess.SetupScript"/> makes the same two on a Postgres of your own.
/// </summary>
public sealed class PostgresRowLevelSecurityOptions
{
    /// <summary>The role PostgREST, and so Supabase, gives a signed-in user.</summary>
    public const string AuthenticatedRole = "authenticated";

    /// <summary>The role PostgREST, and so Supabase, gives a request without a user.</summary>
    public const string AnonRole = "anon";

    /// <summary>The role a signed-in user's queries run as. <c>authenticated</c> by default.</summary>
    public string UserRole { get; set; } = AuthenticatedRole;

    /// <summary>The role the queries of a request without a user run as. <c>anon</c> by default.</summary>
    public string AnonymousRole { get; set; } = AnonRole;

    /// <summary>
    /// The role background work runs as, or <see langword="null"/>, the default, to leave it the role
    /// the application logged in as.
    /// <para>
    /// Logged in as the tables' owner, as most applications are, <see langword="null"/> keeps background
    /// work as it was: row level security does not apply to the owner. Logged in as a role of its own
    /// that may do nothing but switch roles, set this to a role with <c>BYPASSRLS</c>, Supabase's
    /// <c>service_role</c> for one, and background work gets exactly that.
    /// </para>
    /// </summary>
    public string? SystemRole { get; set; }

    /// <summary>The role <paramref name="caller"/> runs as; <c>none</c> means the role the application logged in as.</summary>
    internal string RoleOf(Caller caller) => caller.Kind switch
    {
        CallerKind.User => UserRole,
        CallerKind.Anonymous => AnonymousRole,
        _ => SystemRole ?? "none",
    };

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(UserRole, nameof(UserRole));
        ArgumentException.ThrowIfNullOrWhiteSpace(AnonymousRole, nameof(AnonymousRole));

        if (SystemRole is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(SystemRole, nameof(SystemRole));
        }
    }
}
