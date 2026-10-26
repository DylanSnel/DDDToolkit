using System.ComponentModel;

namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// Marks the list of a project's permissions that Tenancy's generator writes from its <c>[Permissions]</c> classes,
/// <c>DeclaredPermissions.All</c>: what the project that composes the modules collects into the catalogue. A module
/// declares each permission as a constant of a static class marked <c>[Permissions]</c>, with its level, and writes no
/// list; one it marks with this by hand is DDD00095.
/// <para>
/// Tenancy's generator collects every marked list of the modules a project references into that project, in
/// <c>TenancyPermissionsOfModules</c>, in the namespace named after the project's assembly: <c>All</c>, every
/// module's keys, and <c>services.AddTenancyPermissionsOfModules()</c>, which adds them to the catalogue. It writes
/// them into every application, the program the modules are composed in, such as the host, which sees all of them,
/// and into every library that declares no module, with <c>DDD_Module</c> or <c>[assembly: Module]</c>, and into no
/// module's own libraries. The host registers the keys with that one call, and an export builds the same catalogue with
/// <c>TenancyCatalogue.Build(application, TenancyPermissionsOfModules.All)</c>, so a module that is added changes
/// neither. Both compile while no module declares a permission yet: <c>All</c> is then empty.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class TenancyPermissionsAttribute : Attribute;
