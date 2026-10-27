using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Membership.EntityFramework;

// Where the generator in an application finds the registration it closes over the application's member
// classes and names after their resources, so the application writes
// services.AddDocumentMembership<TContext>(rules), once for each kind of resource it has members on.
[assembly: TemplateRegistrations(typeof(MembershipEntityFrameworkServiceCollectionExtensions))]

// What a context that calls AddDDDToolkitConventions() maps with no call of this package's: the members of a resource,
// and the role class of a resource whose roles a customer makes, where the context mapped neither by hand.
[assembly: DDDToolkit.EntityFramework.Conventions.ModelConvention(typeof(MembershipModelConvention))]
