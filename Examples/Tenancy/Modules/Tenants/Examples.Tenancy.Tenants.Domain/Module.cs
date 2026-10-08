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
// The role written is the package's whole: each tenant's roles start as copies of the catalogue's packs and are the
// tenant's own to rename, re-key and archive, and a role made from a pack remembers what the pack gave it, so it
// follows the pack when the host syncs the packs: a key the pack gains later reaches it, and what the tenant changed
// in it stays. It is a role of the organization: given at a unit, it counts for every unit below it. A role a
// project's crew holds is not one of these: it is a project role, kept by the Projects module beside its projects.
//
// The ids are not written: Examples.Tenancy.Tenants.Contracts declares them, with the prefixes they are printed with,
// and the other modules store them from there, without a reference to the Tenancy package. The switch finds them
// there and takes them, so the organization is declared over the contracts' TenantId, the role over its RoleId.
//
// With the classes complete, the generator also writes TenancyUseCases here: Tenancy's use cases closed over them,
// named as the package's class is, which every project above names a use case through. This is the one module with
// Tenancy's classes, so the name is free. A second one would meet this one's in the host, DDD00075, and would get a
// name of its own in its domain project, beside its switch: [assembly: TemplateFacadeName("TenancyUseCases", "...")],
// with using DDDToolkit.Abstractions.Attributes at the top of that file, where the attribute is.
[assembly: GenerateTenancyClasses]
