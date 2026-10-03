using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Catalogue;
using Examples.Tenancy.Projects.Infrastructure.Access;

// Tenancy's functions, policies and triggers go into the exported access files because this program says so: a
// contribution that is only offered writes nothing. So do the functions and the lock the Membership package writes
// for the projects, from the projects' rules, and the trigger Projects holds a project's unit with and the policy it
// holds a crew's seats to their tenant with, which are that module's own to offer.
[assembly: UseRowAccessContribution(typeof(SampleTenancyContribution))]
[assembly: UseRowAccessContribution(typeof(ProjectMembershipFunctions))]
[assembly: UseRowAccessContribution(typeof(UnitChangesWithItsKeys))]
[assembly: UseRowAccessContribution(typeof(CrewSeatsOfTheProjectsTenant))]

// There is nothing to run here. The project file turns the Supabase export on, and its build starts this program
// with the export asked for: code the build generated into it runs before Main, writes or compares
// Examples/Tenancy/supabase/migrations from every [SupabaseMigrations] factory this project references, and ends
// the process. Started any other way, it says so and stops.
Console.WriteLine("This program only exports: build it, and the build writes Examples/Tenancy/supabase/migrations.");
return 0;
