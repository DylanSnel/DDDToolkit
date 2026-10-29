namespace DDDToolkit.Supporting.Membership.Managers;

/// <summary>
/// Whether the caller may change the roles a customer makes for one kind of resource: holds the permission the rules
/// name for it (<see cref="Access.Roles.ManagedWith"/>) for the whole of what the roles are kept for. The roles are a
/// customer's as a whole, not one resource's, so Membership cannot answer it from a resource's members: the role manager
/// asks this.
/// <para>
/// Optional. Without one only system work changes the roles. The join with an organization's package registers one
/// that asks the organization whether the caller holds the permission for the whole of it.
/// </para>
/// </summary>
/// <typeparam name="TResource">The resource the roles are of.</typeparam>
public interface ICustomRoleAccess<TResource>
    where TResource : class
{
    /// <summary>Requires the caller to hold <paramref name="permission"/> for the whole of what the roles are kept for.</summary>
    /// <param name="permission">The permission the rules name for changing the roles.</param>
    /// <param name="cancellationToken">Cancels the question.</param>
    /// <exception cref="Exceptions.RefusalException">The caller does not hold it.</exception>
    ValueTask RequireAsync(string permission, CancellationToken cancellationToken);
}
