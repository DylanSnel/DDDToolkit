using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.EntityFramework;

// Where the generator in an application finds the registrations it closes over the application's classes,
// so the application writes modelBuilder.AddTenancy() and services.AddTenancy<TContext>(...).
[assembly: TemplateRegistrations(typeof(TenancyModelBuilderExtensions))]
[assembly: TemplateRegistrations(typeof(TenancyEntityFrameworkServiceCollectionExtensions))]
[assembly: TemplateRegistrations(typeof(TenancyAttribution))]
[assembly: TemplateRegistrations(typeof(TenancyEventLogTable))]
