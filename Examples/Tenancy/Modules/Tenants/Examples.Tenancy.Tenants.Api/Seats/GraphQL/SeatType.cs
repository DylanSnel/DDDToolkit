using HotChocolate.Types;
using HotChocolate.Types.Composite;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// A seat as the schema shows it: the name it is shown by and whether it counts, never an identity. The type is
/// declared over the record the directory answers, so nothing is copied.
/// </summary>
/// <remarks>
/// Other modules name a seat by its id, and the gateway fills in the rest from here. The record says a name is
/// never missing, and within Tenancy's own answers it never is; as a field of an entity it may be
/// <see langword="null"/> all the same, which the host's conventions say once for every type with a key: a seat
/// that is not there, or not the caller's to see, arrives as its id with nothing else, and without an error.
/// </remarks>
[ObjectType<TenantsTenancy.SeatSummary>]
[EntityKey("id")]
internal static partial class SeatType
{
    static partial void Configure(IObjectTypeDescriptor<TenantsTenancy.SeatSummary> descriptor) => descriptor.Name("Seat");
}
