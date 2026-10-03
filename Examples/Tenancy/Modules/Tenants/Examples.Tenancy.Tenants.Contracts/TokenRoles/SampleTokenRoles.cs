using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Tenants.Contracts.TokenRoles;

/// <summary>
/// The roles a token may carry besides a signed-in user's, by the names the tokens spell them with. A token's role
/// is no seat and no permission: it says which kind of caller this is, and which database role the
/// caller's queries run as.
/// </summary>
/// <remarks>
/// Published because every side names the same string: the Tenants module tells the package which token roles are
/// operators', the host maps each to a database role, and the project that exports the policies writes them for
/// that role.
/// </remarks>
[ModuleContract]
public static class SampleTokenRoles
{
    /// <summary>
    /// The application's own staff, who look across tenants and hold no seat in any: in the database a role
    /// of the same name, which reads and never writes.
    /// </summary>
    public const string Operator = "tenancy_operator";

    /// <summary>
    /// Whether <paramref name="caller"/> is one of the application's operators: a signed-in user whose token carries
    /// <see cref="Operator"/>. The host's own policy for the operators' routes asks this. A request of the
    /// operators' own is held to the same rule by the Tenancy package, which the Tenants module told that this is
    /// the operators' token role, so both mean the same caller by it. System work is never one, whoever it is
    /// recorded as.
    /// </summary>
    /// <param name="caller">Who is calling, as the host verified it.</param>
    public static bool IsOperator(Caller? caller)
        => caller is { Kind: CallerKind.User, UserId: not null } && string.Equals(caller.Role, Operator, StringComparison.Ordinal);
}
