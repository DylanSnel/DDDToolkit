using System.ComponentModel;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// The way from <see cref="CustomRoleAggregate{TRoleId}"/> to the hooks of the application's role class, which the
/// package's generator writes onto that class, explicitly, beside the partial methods it declares:
/// <code>
/// partial void OnRenamed();
/// partial void OnPermissionsSet(RolePermissionsSet set);
/// partial void OnArchived();
/// </code>
/// The role calls each after a change that changed something, so the class raises its own events in the partial
/// method it writes, and writes none where it raises none. An application never implements it and never calls it.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ICustomRoleHooks
{
    /// <summary>The role was renamed, or described again.</summary>
    void Renamed();

    /// <summary>The role's permissions changed.</summary>
    /// <param name="set">The permissions that came in and the permissions that went out.</param>
    void PermissionsSet(RolePermissionsSet set);

    /// <summary>The role was archived.</summary>
    void Archived();
}
