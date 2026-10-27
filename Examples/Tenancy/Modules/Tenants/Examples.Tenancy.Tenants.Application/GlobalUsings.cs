// What every feature of this project names: the module's ids, its access vocabulary, the port its features share,
// and the Tenancy package's refusals. The access vocabulary is the module's request interface, the toolkit's
// requirement types and Tenancy's cases of them, which every request declares one of. A feature that names another
// one says so in its own file, with a using of that feature's namespace, so what leans on what stays in plain sight.
//
// The module's classes are not among these. Two features of this project are called Tenant and Organization, so
// inside it those two words are the features' namespaces, whatever is imported: the classes of those names are
// reached through Tenancy's managers, and a file that names another of the classes imports its namespace itself.
//
// Those managers are not closed here either. The package's managers are generic over the module's classes and ids, and
// the toolkit's generator closes each over the classes in the domain project that declares them, under the package's
// own names. So a handler takes a SeatManager, and the invitations are the InvitationManager, closed over the module's
// invitation class as well. The stores the managers save through, for the few commands that change a field of the
// module's own on one of Tenancy's aggregates, are TenancyStore and InvitationStore: aliases the generator writes into
// every project that sees the module's classes, this one among them.
global using DDDToolkit.Access;
global using DDDToolkit.Supporting.Tenancy.Access;
global using Examples.Tenancy.Tenants.Application.Access;
global using Examples.Tenancy.Tenants.Application.StoredTenancy;
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;
global using DDDToolkit.Supporting.Tenancy;

