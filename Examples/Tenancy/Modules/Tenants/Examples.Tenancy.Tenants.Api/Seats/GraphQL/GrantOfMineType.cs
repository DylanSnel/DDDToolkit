using Examples.Tenancy.Tenants.Application.Seats;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// A role the calling seat holds at a unit, for a period, as the schema shows it: declared over the record the
/// seat's overview answers, <see cref="GrantListing"/>.
/// </summary>
[ObjectType<GrantListing>]
internal static partial class GrantOfMineType
{
    static partial void Configure(IObjectTypeDescriptor<GrantListing> descriptor) => descriptor.Name("GrantOfMine");
}
