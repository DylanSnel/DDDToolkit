using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Membership.Postgres;

// The functions of a resource's membership, offered to the application's export. The application uses them by
// listing a class of its own for each kind of resource, derived from this one closed over that resource's
// member class, with its rules, in the project that runs the export.
[assembly: RowAccessContribution(typeof(MembershipRowAccessContribution<>))]
