using Examples.Tenancy.Tenants.Application.Seats;
using HotChocolate.Types;
using HotChocolate.Types.Composite;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// A seat as the schema shows it: the name it is shown by and whether it counts, never an identity. The type is
/// declared over the record the module's queries answer, <see cref="SeatListing"/>, so nothing is copied: the
/// seat's id, the name this application keeps on its own seat class, which Tenancy has none of, and its status.
/// </summary>
/// <remarks>
/// Other modules name a seat by its id, and the gateway fills in the rest from here. The record says a name is
/// never missing, and within this module's own answers it never is; as a field of an entity it may be
/// <see langword="null"/> all the same, which the host's conventions say once for every type with a key: a seat
/// that is not there, or not the caller's to see, arrives as its id with nothing else, and without an error.
/// </remarks>
[ObjectType<SeatListing>]
[EntityKey("id")]
internal static partial class SeatType
{
    static partial void Configure(IObjectTypeDescriptor<SeatListing> descriptor) => descriptor.Name("Seat");
}
