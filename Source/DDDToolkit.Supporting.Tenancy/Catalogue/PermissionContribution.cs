namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// The permission keys one module adds to the catalogue, so a module that owns keys declares them next to
/// the code that asks for them rather than in the application's catalogue. Contributions add up.
/// </summary>
/// <param name="permissions">The module's keys.</param>
public sealed class PermissionContribution(IEnumerable<Permission> permissions)
{
    /// <summary>The module's keys.</summary>
    public IReadOnlyList<Permission> Permissions { get; } = [.. permissions ?? throw new ArgumentNullException(nameof(permissions))];
}
