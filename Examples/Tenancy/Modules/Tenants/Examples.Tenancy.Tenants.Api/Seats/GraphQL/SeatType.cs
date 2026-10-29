using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
using HotChocolate.Types;
using HotChocolate.Types.Composite;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// A seat as the schema shows it: the module's own <see cref="Seat"/>, the class the seat manager answers, with the
/// name this application keeps on it and whether it counts. Never an identity.
/// </summary>
/// <remarks>
/// The type is the class itself, so nothing is copied. Its fields are listed one by one: the class also holds the
/// person's identity, its tenant, its job title and its placements with their grants, and the schema shows what this
/// type chose. A seat's own placements are in its overview. Another person's roles are read in the administration's
/// schema, through <c>seatGrants</c>.
/// <para>
/// Other modules name a seat by its id, and the gateway fills in the rest from here. Every field but the key may be
/// <see langword="null"/> in the schema, by the host's convention for a type with a key: a seat that is not there, or
/// not the caller's to see, arrives as its id with nothing else, and without an error.
/// </para>
/// </remarks>
[ObjectType<Seat>]
[EntityKey("id")]
internal static partial class SeatType
{
    static partial void Configure(IObjectTypeDescriptor<Seat> descriptor)
    {
        descriptor.BindFieldsExplicitly();
        descriptor.Field(seat => seat.Id);
        descriptor.Field(seat => seat.DisplayName);
        descriptor.Field(seat => seat.Status);
    }
}
