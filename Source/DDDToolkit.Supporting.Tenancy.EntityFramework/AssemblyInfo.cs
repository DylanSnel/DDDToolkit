using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.EntityFramework;

// Where the generator in an application finds the registrations it closes over the application's classes,
// so the application writes modelBuilder.AddTenancy(), services.AddTenancy<TContext>(...) and
// outbox.AddTenancyDomainEvents(), and names none of its ids again.
[assembly: TemplateRegistrations(typeof(TenancyModelBuilderExtensions))]
[assembly: TemplateRegistrations(typeof(TenancyEntityFrameworkServiceCollectionExtensions))]
[assembly: TemplateRegistrations(typeof(TenancyAttribution))]
[assembly: TemplateRegistrations(typeof(TenancyEventLogTable))]
[assembly: TemplateRegistrations(typeof(TenancyEventLogExtensions))]
[assembly: TemplateRegistrations(typeof(TenancyOutboxExtensions))]
