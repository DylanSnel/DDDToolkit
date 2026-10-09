using Examples.Tenancy.Tenants.Api.Directory.GraphQL;
using Examples.Tenancy.Tenants.Application.History;
using Examples.Tenancy.Tenants.Application.Seats;
using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.History.GraphQL;

/// <summary>
/// One row of the access history as the schema shows it: the event, when it happened and who made the change.
/// Declared over the record the query answers.
/// </summary>
/// <remarks>
/// Who acted is a kind, <c>byKind</c>, and for a seat the seat itself, <c>bySeat</c>: the record names it by id,
/// and the resolver below reads it through the directory's data loader, so the seats of one batch, as a rule all
/// a page names, are one question. An operator and the application's own work are a kind and nothing else, as on
/// the route.
/// <c>details</c> is the event as it was stored, as JSON text.
/// <para>
/// The seat's id is a field of its own beside it, <c>bySeatId</c>, as the route gives it. The directory answers
/// a seat of the tenant, so the application's own staff, who read one tenant's history and hold no seat, read
/// who acted as the kind and that id.
/// </para>
/// </remarks>
[ObjectType<AccessHistoryEntry>]
internal static partial class AccessHistoryEntryType
{
    /// <summary>The id of the seat that made the change, or of the seat a token stands for; nothing when no seat did.</summary>
    public static SeatId? GetBySeatId([Parent] AccessHistoryEntry row) => row.BySeat;

    /// <summary>The seat that made the change, or the seat a token stands for; nothing when no seat did.</summary>
    [Cost(DirectoryQueries.LoadedForTheRequest)]
    public static async Task<SeatListing?> GetBySeatAsync([Parent] AccessHistoryEntry row, ISeatByIdDataLoader seats, CancellationToken cancellationToken)
        => row.BySeat is { } seat ? await seats.LoadAsync(seat, cancellationToken) : null;
}
