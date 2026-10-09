using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Membership.Postgres;

// The functions and the lock of each kind of resource with members, which the Supabase export of every application
// that references this package writes into its migrations. The build makes the class in the project that runs the
// export once for every resource whose rules the application marks [MembershipRules<TMember>], closed over that
// member class.
[assembly: RowAccessContribution(typeof(MembershipRowAccessContribution<>))]
