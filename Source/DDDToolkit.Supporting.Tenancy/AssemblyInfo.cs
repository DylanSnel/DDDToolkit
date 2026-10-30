using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Managers;

// The project that declares a module's classes with Tenancy's templates gets a manager per aggregate closed over them,
// each a class named as the package's is without its type parameters, which the container makes: TenantManager,
// OrganizationManager, SeatManager and RoleManager, whatever the module is called, and InvitationManager where the
// module declares an invitation class, the one class an application may leave out. Every project that sees them takes a
// SeatManager and names SeatManager.SeatOverview without writing the nine types. TenantToProvision, the record a tenant
// is provisioned from, is written beside them, so code makes one without type arguments. An application whose two
// modules both declare the classes names one module's with [assembly: TemplateFacadeName].
[assembly: TemplateFacade(typeof(TenantManager<,,,,,,,,>))]
[assembly: TemplateFacade(typeof(OrganizationManager<,,,,,,,,>))]
[assembly: TemplateFacade(typeof(SeatManager<,,,,,,,,>))]
[assembly: TemplateFacade(typeof(RoleManager<,,,,,,,,>))]
[assembly: TemplateFacade(typeof(InvitationManager<,,,,,,,,,,>), OnlyWith = typeof(InvitationAggregateAttribute<>))]
[assembly: TemplateFacade(typeof(TenantToProvision<,,,,,,>))]

// The stores the managers load and save through are interfaces the storage package implements, so no class can stand for
// them: the same projects get a global alias of each instead, named without its I, closed over the same classes. An alias
// holds in its own project only, so the project that declares the classes gets one and so does every project above it
// that sees them: TenancyStore, and InvitationStore where the module declares an invitation class. A command of the
// application's that changes a field of its own on one of Tenancy's aggregates takes the store by that name.
[assembly: TemplateFacade(typeof(ITenancyStore<,,,,,,,,>))]
[assembly: TemplateFacade(typeof(IInvitationStore<,,,,,>), OnlyWith = typeof(InvitationAggregateAttribute<>))]

// And it gets the access manager closed over its four ids, TenancyAccessManager, a class the container makes: who acts,
// in which tenant, and the work that is not the caller's own. A project of another module that sees only the ids says
// which they are with [assembly: TenancyIds<...>], and gets the class in its root namespace.
[assembly: TemplateFacade(typeof(TenancyAccessManager<,,,>))]

// The questions a row access rule asks of Tenancy, closed over the module's ids in every project of a module that declares
// the classes or says which ids are Tenancy's with [assembly: TenancyIds<...>]: user.CurrentSeatId() for
// user.CurrentSeatId<SeatId>(). A wrapper is internal, and a rule may sit in any project.
[assembly: TemplateRegistrations(typeof(TenancyRowAccess))]

// A request that requires a permission and marks no member of where it is held, [RequiresPermission(permission)] on its own, requires it
// for the whole tenant: Tenancy's case, so Tenancy says it. A permission at a unit is marked [AtUnit], and one on a resource
// with members [OnResource], which Membership ships. A permission held for the whole tenant is held by a seat in it or by system
// work there, so [InTenant] beside it says nothing more.
[assembly: PermissionWithoutMarker(typeof(TenancyAccess), nameof(TenancyAccess.AtTenant), Implies = [typeof(InTenantAttribute)])]

// A request about the caller's own seat, [OrOwn] SeatId Seat, compares it with the caller's seat in the tenant: the handle
// AddTenancy registers says so, over the seat id the application declares its seat class with, which the generator reads
// from the template here. Only a seat in the tenant has a seat of its own there, and a seat is a signed-in user, so
// [InTenant] or [SignedIn] beside [OwnOnly] with [Own] on the seat id says nothing more, and neither does [InTenant]
// beside a permission of Tenancy's with [OrOwn] on it.
[assembly: OwnedByCurrentUser(IdOf = typeof(SeatAggregateAttribute<>), Implies = [typeof(InTenantAttribute), typeof(SignedInAttribute)])]
