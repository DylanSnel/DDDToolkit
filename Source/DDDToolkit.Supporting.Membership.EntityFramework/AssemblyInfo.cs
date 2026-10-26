using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Membership.EntityFramework;

// Where the generator in an application finds the registration it closes over the application's member
// classes and names after their resources, so the application writes
// services.AddDocumentMembership<TContext>(rules), once for each kind of resource it has members on.
[assembly: TemplateRegistrations(typeof(MembershipEntityFrameworkServiceCollectionExtensions))]
