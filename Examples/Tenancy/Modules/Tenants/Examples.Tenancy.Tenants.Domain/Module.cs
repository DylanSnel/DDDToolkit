// The toolkit's module is not declared here. The build declares it for every project in the module's folder, from
// Modules/Directory.Build.props; what this file holds is Tenancy's switch.
//
// The generator writes the package's classes this module adds nothing to, as the package ships them: the organization
// and the role, public, in this project's root namespace, Examples.Tenancy.Tenants.Domain, each with the documentation
// that says it was written and how to declare it yourself. The other four are declared under Aggregates, because each
// adds something: the tenant whether it is a demonstration, the unit its kind and cost centre, the seat the name it
// is shown by in its tenant and a job title, and the invitation the account it made. A class declared here always
// wins, and the switch writes no second one.
//
// The role written is the package's whole: each tenant's roles start as copies of the catalogue's starter roles and are the
// tenant's own to rename, give other permissions and archive, and a role made from a starter role remembers what the starter role gave it, so it
// follows the starter role when the host syncs the starter roles: a permission the starter role gains later reaches it, and what the tenant changed
// in it stays. It is a role of the organization: given at a unit, it counts for every unit below it. A role a
// project's crew holds is not one of these: it is a project role, kept by the Projects module beside its projects.
//
// The ids are not written: Examples.Tenancy.Tenants.Contracts declares them, with the prefixes they are printed with,
// and the other modules store them from there, without a reference to the Tenancy package. The switch finds them
// there and takes them, so the organization is declared over the contracts' TenantId, the role over its RoleId.
//
// With the classes complete, the generator also writes Tenancy's managers here, closed over them: TenantManager,
// OrganizationManager, SeatManager, RoleManager and InvitationManager, named as the package's classes are, which every
// project above takes. This is the one module with Tenancy's classes, so the names are free. A second one would meet
// this one's in the host, DDD00075, and would give its own a name each in its domain project, beside its switch:
// [assembly: TemplateFacadeName("SeatManager", "...")], with using DDDToolkit.Abstractions.Attributes at the top of that
// file, where the attribute is.
[assembly: GenerateTenancyClasses]
