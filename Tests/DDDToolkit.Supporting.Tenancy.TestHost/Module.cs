using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.UseCases;

// The application's tenancy module. The package declares no module of its own: once an application
// references it, it is part of whichever module declares the classes, and that is this one.
[assembly: Module("Tenancy")]

// Tenancy's use cases closed over this module's classes, which the toolkit's generator writes here and every test
// project names them through. After the module it would be TenancyTenancy, so the module names it, once.
[assembly: TemplateFacade(typeof(TenancyUseCases<,,,,,,,,>), "HostTenancy")]
