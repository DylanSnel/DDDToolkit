using DDDToolkit.Supporting.Tenancy.Access;
using HotChocolate;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// One of the caller's own seats as the schema shows it: the tenant it is in, and the seat.
/// </summary>
/// <remarks>
/// Declared over the row the package answers, which is flat: the tenant's values and the seat's side by side. The
/// schema shows the two as objects, so the row's own values are no fields, and the two resolvers put them
/// together: the seat as the <c>Seat</c> every other answer shows, the tenant as <see cref="TenantOfSeat"/>.
/// A resolver named as a value of the row takes its place; the others are left out one by one.
/// </remarks>
[ObjectType<SeatOfCaller<TenantId, SeatId>>]
internal static partial class SeatOfMineType
{
    static partial void Configure(IObjectTypeDescriptor<SeatOfCaller<TenantId, SeatId>> descriptor)
    {
        descriptor.Name("SeatOfMine");
        descriptor.Ignore(mine => mine.Slug);
        descriptor.Ignore(mine => mine.OrganizationName);
        descriptor.Ignore(mine => mine.TenantStatus);
        descriptor.Ignore(mine => mine.DisplayName);
        descriptor.Ignore(mine => mine.SeatStatus);
    }

    /// <summary>The tenant, with the slug a request selects it by.</summary>
    public static TenantOfSeat GetTenant([Parent] SeatOfCaller<TenantId, SeatId> mine)
        => new(mine.Tenant, mine.Slug, mine.OrganizationName, mine.TenantStatus);

    /// <summary>The caller's seat in it.</summary>
    public static TenantsTenancy.SeatSummary GetSeat([Parent] SeatOfCaller<TenantId, SeatId> mine)
        => new(mine.Seat, mine.DisplayName, mine.SeatStatus);
}
