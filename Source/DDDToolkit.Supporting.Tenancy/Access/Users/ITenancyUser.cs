namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// A <see cref="TenancyUser{TTenantId, TSeatId}"/> without its id types, for code that cannot name them:
/// a query filter that only compares the tenant, and the save check.
/// </summary>
public interface ITenancyUser
{
    /// <summary>Which kind of user this is.</summary>
    TenancyUserKind Kind { get; }

    /// <summary>The tenant a seat or system work in a tenant acts in, boxed; <see langword="null"/> for the others.</summary>
    object? TenantId { get; }

    /// <summary>The caller's seat, or the seat system work acts for, boxed; <see langword="null"/> when there is none.</summary>
    object? SeatId { get; }

    /// <summary>For <see cref="TenancyUserKind.Nobody"/>, the refusal code that says why; otherwise <see langword="null"/>.</summary>
    string? Refusal { get; }

    /// <summary>
    /// Who a change this user makes is recorded as, without its id type; <see langword="null"/> for nobody, who
    /// changes nothing.
    /// </summary>
    ITenancyActor? Actor { get; }
}
