namespace DDDToolkit.Access;

/// <summary>
/// What every level of a permission derives from: the attribute on a constant of a <see cref="PermissionsAttribute"/>
/// class that says where the permission is asked. A package ships its levels, Tenancy <c>[AtTenant]</c> and
/// <c>[AtUnit]</c>, Membership <c>[On&lt;TResource&gt;]</c>, and the settings every level has are these: what the
/// permission is for, which permissions come with it, and whether it is retired.
/// </summary>
/// <remarks>
/// A permission carries at least one level. Tenancy's two may stand together, for a permission asked for the whole tenant
/// and at a unit alike; a permission on a resource is asked there and nowhere else. The settings are read off the
/// first level that says them, so say each once.
/// </remarks>
public abstract class PermissionLevelAttribute : Attribute
{
    /// <summary>
    /// What holding the permission lets somebody do, for the screens that put permissions into roles: <c>"See a board
    /// and who is on it"</c>. Left out, the constant's <c>&lt;summary&gt;</c> is the description, which IntelliSense
    /// shows as well, so a permission is described once.
    /// <para>
    /// It is the English text, and the fallback: a reader in another language gets what the application's
    /// translations hold under the permission's name, in a resx it adds to its failure localizer, as refusal texts
    /// are translated (<c>IFailureLocalizer.Text(permission, description)</c>). A permission without either has an empty
    /// description in the catalogue.
    /// </para>
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The permissions that come with this one, such as the permission that sees a board with the permission that edits it: a role
    /// that holds this one holds those as well, wherever it holds this. One hop only: a permission another implies
    /// implies nothing itself. Empty unless said.
    /// <para>
    /// Consequence: a role made with only this permission does what the implied ones let it do too, so a screen that
    /// lists a role's permissions shows them as part of it.
    /// </para>
    /// </summary>
    public string[]? Implies { get; set; }

    /// <summary>
    /// Whether the permission is retired: known, so a role that holds it keeps it and no stored right breaks, but
    /// holding nowhere, and given to no role again. A permission is retired and never deleted. <see langword="false"/>
    /// unless said.
    /// </summary>
    public bool Retired { get; set; }
}
