using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.Postgres;

// Tenancy's functions, policies and triggers, which the Supabase export of every application that references this
// package writes into its migrations: Tenancy on Postgres does not work without them. The build makes the class in the
// project that runs the export, from the catalogue the application marks [TenancyCatalogue], the keys its modules mark
// [TenancyPermissions] and the operators it marks [TenancyOperators] (see the constructor that says so).
[assembly: RowAccessContribution(typeof(TenancyRowAccessContribution))]
