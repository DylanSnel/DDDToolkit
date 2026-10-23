using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.UseCases;

// The project that declares a module's classes with Tenancy's templates gets the use cases closed over them, as a
// class named as this one is without its type parameters: TenancyUseCases, whatever the module is called. Every project
// that sees it names TenancyUseCases.SeatCommands and TenancyUseCases.SeatOverview without writing the nine types. An
// application whose two modules both declare the classes names one of them with [assembly: TemplateFacadeName].
[assembly: TemplateFacade(typeof(TenancyUseCases<,,,,,,,,>))]

// A request that requires a key and marks no member of where it is held, [RequiresKey(key)] on its own, requires it
// for the whole tenant: Tenancy's case, so Tenancy says it. A key at a unit is marked [AtUnit], and one on a resource
// with members [OnResource], which Membership ships. A key held for the whole tenant is held by a seat in it or by system
// work there, so [InTenant] beside it says nothing more.
[assembly: KeyWithoutMarker(typeof(TenancyAccess), nameof(TenancyAccess.ForTheWholeTenant), Implies = [typeof(InTenantAttribute)])]

// A request about the caller's own seat, [OrOwn] SeatId Seat, compares it with the caller's seat in the tenant: the handle
// AddTenancy registers says so, over the seat id the application declares its seat class with, which the generator reads
// from the template here. Only a seat in the tenant has a seat of its own there, and a seat is a signed-in user, so
// [InTenant] or [SignedIn] beside [OwnOnly] with [Own] on the seat id says nothing more, and neither does [InTenant]
// beside a key of Tenancy's with [OrOwn] on it.
[assembly: CallersOwn(IdOf = typeof(SeatAggregateAttribute<>), Implies = [typeof(InTenantAttribute), typeof(SignedInAttribute)])]
