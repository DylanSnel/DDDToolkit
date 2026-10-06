namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// One of a person's seats, for a tenant picker: the application's own seat, whole, and only what is not on the seat
/// itself, the tenant it is in by what a picker shows of it. The seat carries its id, its tenant's id, its status and
/// every field the application added, such as the name the person is shown by in that tenant, so a picker shows a
/// seat by what the application chose, as every other answer about the seat does
/// (<see cref="TenantSelection{TTenantId, TSeatId}.SeatsOfAsync{TSeat}"/>).
/// </summary>
/// <remarks>
/// The seat was read for the picker and is tracked by nobody: nothing done to it is saved. It was read across
/// tenants, before one is picked, so where it is placed and the roles it holds are its tenant's to answer, in that
/// tenant: a database that keeps tenants apart hands them to nobody outside it, and its placements may be missing
/// here. What leaves is what the application selects: the seat has its identity.
/// </remarks>
/// <typeparam name="TSeat">The application's seat class, or a class it derives from.</typeparam>
/// <param name="Seat">The seat, the application's own class.</param>
/// <param name="Slug">The slug its tenant is selected by.</param>
/// <param name="OrganizationName">Its tenant's name.</param>
/// <param name="TenantStatus">Where its tenant is in its life.</param>
public sealed record SeatInTenant<TSeat>(TSeat Seat, string Slug, string OrganizationName, TenantStatus TenantStatus)
    where TSeat : class;
