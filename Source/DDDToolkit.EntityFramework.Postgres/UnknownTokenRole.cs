namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// What a signed-in user gets whose token carries a role the host gave no database role: neither the role of a
/// signed-in user nor one listed in <see cref="PostgresRowLevelSecurityOptions.TokenRoles"/>.
/// </summary>
public enum UnknownTokenRole
{
    /// <summary>
    /// Nothing: the caller is refused with <c>access.role-not-allowed</c> before a context connects for it. The
    /// default, so a role nobody listed never runs as a role somebody did.
    /// </summary>
    Refuse,

    /// <summary>
    /// What a request without a user gets: the anonymous caller's role, with the anonymous caller's claims, so the
    /// database sees no user at all. The settings of the modules, <see cref="IRowLevelSecuritySettings"/>, are
    /// asked for an anonymous caller as well. For a host that would rather show such a caller what everybody may
    /// see than refuse it.
    /// </summary>
    Anonymous,
}
