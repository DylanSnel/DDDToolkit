using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Projects.Contracts.Keys;

/// <summary>
/// The permission keys Projects asks for. A role grants them at a unit of the organization or through a
/// project's crew, except <see cref="Open"/> and <see cref="ChangeOwner"/>, which only a unit grants; see
/// <c>ProjectCatalogue</c> for what each one lets a seat do.
/// </summary>
/// <remarks>
/// Published as constants, not as the catalogue's records: another module names a key when it asks Projects a
/// question, and the host names them when it builds the role packs. Neither needs to know how Projects
/// describes them.
/// </remarks>
[ModuleContract]
public static class ProjectKeys
{
    /// <summary>See a project and its crew.</summary>
    public const string View = "projects.view";

    /// <summary>Open a project at a unit. Asked for at the unit, since there is no project yet.</summary>
    public const string Open = "projects.open";

    /// <summary>Rename a project, or move it to another unit.</summary>
    public const string Edit = "projects.edit";

    /// <summary>Close a project, and reopen a closed one.</summary>
    public const string Close = "projects.close";

    /// <summary>Add and remove crew members, and give and take their crew roles.</summary>
    public const string ManageCrew = "projects.crew.manage";

    /// <summary>Name a project's owner. Held at a unit only: a crew role that holds it gives nothing with it.</summary>
    public const string ChangeOwner = "projects.owner.change";
}
