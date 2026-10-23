using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.UseCases;

// The project that declares a module's classes with Tenancy's templates gets the use cases closed over them, as a
// class named as this one is without its type parameters: TenancyUseCases, whatever the module is called. Every project
// that sees it names TenancyUseCases.SeatCommands and TenancyUseCases.SeatOverview without writing the nine types. An
// application whose two modules both declare the classes names one of them with [assembly: TemplateFacadeName].
[assembly: TemplateFacade(typeof(TenancyUseCases<,,,,,,,,>))]

// A request that requires a key and marks no member of where it is held, [RequiresKey(key)] on its own, requires it
// for the whole tenant: Tenancy's case, so Tenancy says it. A key at a unit is marked [AtUnit], and one on a resource
// with members [OnResource], which Membership ships.
[assembly: KeyWithoutMarker(typeof(TenancyAccess), nameof(TenancyAccess.ForTheWholeTenant))]
