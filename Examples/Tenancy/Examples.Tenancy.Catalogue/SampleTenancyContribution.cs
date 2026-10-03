using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Catalogue;
using Examples.Tenancy.Tenants.Contracts.TokenRoles;
using DDDToolkit.Supporting.Tenancy.Postgres;

[assembly: RowAccessContribution(typeof(SampleTenancyContribution))]

namespace Examples.Tenancy.Catalogue;

/// <summary>
/// What Tenancy writes into the exported access files for this application: its functions, the policies on its
/// own tables and on every table a module keeps to a tenant, and its triggers, written from the catalogue the
/// host runs with and for the token role of the application's operators.
/// </summary>
/// <remarks>
/// Offered here, next to the catalogue it is written from, and used by the project that exports, with
/// <c>[assembly: UseRowAccessContribution(typeof(SampleTenancyContribution))]</c>. The export makes it with
/// <c>new</c>, before any host exists, which is why the catalogue can be built without one
/// (<see cref="SampleCatalogue.Built"/>). The operators' token roles are the ones the Tenants module gives the
/// package's options: the host's start-up check compares the two, and stops a host whose database was written
/// for other operators than it runs with.
/// </remarks>
public sealed class SampleTenancyContribution() : TenancyRowAccessContribution(SampleCatalogue.Built, [SampleTokenRoles.Operator]);
