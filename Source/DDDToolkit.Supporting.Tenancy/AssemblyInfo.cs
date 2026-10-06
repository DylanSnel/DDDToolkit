using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.UseCases;

// The project that declares a module's classes with Tenancy's templates gets the use cases closed over them, as a
// class named after the module: the module Shop gets ShopTenancy, and every project that sees it names
// ShopTenancy.SeatCommands and ShopTenancy.SeatOverview without writing the nine types.
[assembly: TemplateFacade(typeof(TenancyUseCases<,,,,,,,,>), "{Module}Tenancy")]
