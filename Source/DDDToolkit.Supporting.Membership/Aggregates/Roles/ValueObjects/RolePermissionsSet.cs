namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// What setting a role's permissions did: the permissions that came in and the permissions that went out. An application raises
/// its own event from it, and says to whoever holds the role what changed for them.
/// </summary>
/// <param name="Added">The permissions the role gives now and did not before, in ordinal order.</param>
/// <param name="Removed">The permissions the role gave before and gives no more, in ordinal order.</param>
public sealed record RolePermissionsSet(IReadOnlyList<string> Added, IReadOnlyList<string> Removed)
{
    /// <summary>Whether anything changed: setting the permissions a role has already changes nothing.</summary>
    public bool Changed => Added.Count > 0 || Removed.Count > 0;
}
