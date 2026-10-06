// The toolkit's module is not declared here. The build declares it for every project in the module's folder, from
// Modules/Directory.Build.props; what this file holds is Tenancy's switch.
//
// The generator writes the package's classes this module adds nothing to, as the package ships them: the organization
// and the role, public, in this project's root namespace, Examples.Tenancy.Tenants.Domain, each with the documentation
// that says it was written and how to declare it yourself. The other four are declared under Aggregates, because each
// adds something: the tenant whether it is a demonstration, the unit its kind and cost centre, the seat a job title,
// and the invitation the account it made. A class declared here always wins, and the switch writes no second one.
//
// The ids are not written: Examples.Tenancy.Tenants.Contracts declares them, with the prefixes they are printed with,
// and the other modules store them from there, without a reference to the Tenancy package. The switch finds them
// there and takes them, so the organization is declared over the contracts' TenantId, the role over its RoleId.
[assembly: GenerateTenancyClasses]
