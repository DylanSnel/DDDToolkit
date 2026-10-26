namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// Permissions added to the catalogue besides the application's own, so a module that owns permissions declares
/// them next to the code that asks for them rather than in the application's catalogue: the host's
/// <c>AddTenancyPermissionsOfModules()</c> adds every module's list as one, and <c>AddTenancyPermissions</c> a list
/// by hand. Contributions add up.
/// </summary>
/// <param name="permissions">The permissions.</param>
public sealed class PermissionContribution(IEnumerable<Permission> permissions)
{
    /// <summary>The permissions.</summary>
    public IReadOnlyList<Permission> Permissions { get; } = [.. permissions ?? throw new ArgumentNullException(nameof(permissions))];
}
