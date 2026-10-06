using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// A role the calling seat holds at a unit, for a period, as the schema shows it: declared over the record the
/// seat's own overview answers.
/// </summary>
[ObjectType<TenantsTenancy.GrantSummary>]
internal static partial class GrantOfMineType
{
    static partial void Configure(IObjectTypeDescriptor<TenantsTenancy.GrantSummary> descriptor) => descriptor.Name("GrantOfMine");
}
