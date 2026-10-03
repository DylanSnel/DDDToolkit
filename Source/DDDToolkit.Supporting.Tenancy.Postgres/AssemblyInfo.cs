using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.Postgres;

// Tenancy's functions, policies and triggers, offered to the application's export. The application uses them by
// listing a class of its own derived from this one, with its catalogue, in the project that runs the export.
[assembly: RowAccessContribution(typeof(TenancyRowAccessContribution))]
