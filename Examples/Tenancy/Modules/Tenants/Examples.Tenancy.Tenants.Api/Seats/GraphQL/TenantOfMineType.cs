using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// The tenant the calling seat is in, as the schema shows it: declared over the record the seat's own overview
/// answers, under the name a client knows it by.
/// </summary>
[ObjectType<SampleTenancy.TenantSummary>]
internal static partial class TenantOfMineType
{
    static partial void Configure(IObjectTypeDescriptor<SampleTenancy.TenantSummary> descriptor) => descriptor.Name("TenantOfMine");
}
