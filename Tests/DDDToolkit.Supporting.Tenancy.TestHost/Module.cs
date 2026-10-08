using DDDToolkit.Abstractions.Attributes;

// The application's tenancy module. The package declares no module of its own: once an application
// references it, it is part of whichever module declares the classes, and that is this one.
[assembly: Module("Tenancy")]

// Tenancy's use cases closed over this module's classes, which the toolkit's generator writes here and every test
// project names them through. They would be TenancyUseCases; this host gives them a name of its own, HostTenancy, with
// the line an application writes when two of its modules declare Tenancy's classes, so that line is built and used
// through a reference in all three of Tenancy's test projects.
[assembly: TemplateFacadeName("TenancyUseCases", "HostTenancy")]
