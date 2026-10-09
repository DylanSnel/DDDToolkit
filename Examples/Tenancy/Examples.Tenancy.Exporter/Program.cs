using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Projects.Infrastructure.Access;

// The Projects module's own SQL: the trigger it holds a project's unit with and the policy it holds a crew's seats to
// their tenant with. It is the application's, so the module offers it and this program lists it; without these
// lines the build warns that the module's SQL is missing (DDD00069). Tenancy's functions, policies and triggers,
// and the functions and the lock the Membership package writes for the projects, are not listed: the modules this
// program references reference those packages, which write their SQL into it by themselves, from what the catalogue
// and the Tenants module mark. The build writes the classes that make them into
// DDDToolkit.RowAccessContributionsOfPackages.g.cs, among this project's generated files.
[assembly: UseRowAccessContribution(typeof(UnitChangesWithItsKeys))]
[assembly: UseRowAccessContribution(typeof(CrewSeatsOfTheProjectsTenant))]

// There is nothing to run here. The project file turns the Supabase export on, and its build starts this program
// with the export asked for: code the build generated into it runs before Main, writes or compares
// Examples/Tenancy/supabase/migrations from every [SupabaseMigrations] context this project references, and ends
// the process. Started any other way, it says so and stops.
Console.WriteLine("This program only exports: build it, and the build writes Examples/Tenancy/supabase/migrations.");
return 0;
