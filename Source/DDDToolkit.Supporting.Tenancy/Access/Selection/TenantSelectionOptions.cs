using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Which signed-in users may hold a seat: those whose token carries one of the listed roles. A host that gives
/// some of its users a token role of their own, analysts who look across tenants say, leaves that role off the
/// list, and such a user is nobody in every tenant, whatever seats their identity has.
/// <code>
/// services.AddTenancy&lt;ShopTenancyContext&gt;(options =>
/// {
///     ...
///     options.TenantSelection.SeatedTokenRoles.Add("member");
/// });
/// </code>
/// </summary>
public sealed class TenantSelectionOptions
{
    /// <summary>The role of a signed-in user whose token says nothing else, and of a caller made without a token.</summary>
    public const string AuthenticatedTokenRole = "authenticated";

    /// <summary>
    /// The token roles whose users are seated, <see cref="Caller.Role"/> as the token spells it:
    /// <c>authenticated</c> by default. A caller without a role counts as <c>authenticated</c>.
    /// </summary>
    public IList<string> SeatedTokenRoles { get; } = [AuthenticatedTokenRole];

    /// <summary>Whether a signed-in user whose token carries <paramref name="tokenRole"/> may hold a seat.</summary>
    internal bool Seats(string? tokenRole)
    {
        var role = tokenRole ?? AuthenticatedTokenRole;
        foreach (var seated in SeatedTokenRoles)
        {
            if (string.Equals(seated, role, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
