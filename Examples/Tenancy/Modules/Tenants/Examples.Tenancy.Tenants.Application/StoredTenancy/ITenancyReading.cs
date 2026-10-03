namespace Examples.Tenancy.Tenants.Application.StoredTenancy;

/// <summary>
/// Tenancy's rows for one query, on a context that query has to itself. Opened by
/// <see cref="ITenancyReads.Open"/> and disposed when the query has run.
/// </summary>
/// <remarks>
/// What the application depends on, though no reference says so: <see cref="Rows"/> and <see cref="Queries"/>
/// come from one provider and one context, so the access questions asked over the rows
/// (<c>answers.Over(reading.Rows, reading.Queries)</c>) are queries of that context, and a query composed of them
/// and the rows is translated and sent as one statement. Nothing is tracked: a reading only reads.
/// </remarks>
public interface ITenancyReading : IAsyncDisposable
{
    /// <summary>The rows Tenancy's access questions read: units, their paths, seats, roles and the rights seats hold.</summary>
    ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId> Rows { get; }

    /// <summary>Runs <see cref="Rows"/> and anything composed of them, where they are stored.</summary>
    IQueryExecutor Queries { get; }
}
