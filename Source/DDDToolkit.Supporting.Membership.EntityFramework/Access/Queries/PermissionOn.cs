namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// A permission the caller holds on a resource: one row of what <c>db.Boards.WhereIHold([BoardPermissions.Edit,
/// BoardPermissions.Archive])</c> answers, for a list that shows what its caller may do with each row. A permission is one
/// row on a resource, however many ways the caller holds it there, and a resource the caller does not see is in no row.
/// <code>
/// var buttons = await db.Boards.WhereIHold([BoardPermissions.Edit, BoardPermissions.Archive])
///     .Select(pair =&gt; new { pair.Resource.Id, pair.Permission })
///     .ToListAsync(cancellationToken);
/// </code>
/// A row is part of a statement: a query goes on from it to what it reads of the resource, as above.
/// </summary>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
public sealed class PermissionOn<TResource>
    where TResource : class
{
    /// <summary>The resource.</summary>
    public required TResource Resource { get; init; }

    /// <summary>The permission held on it.</summary>
    public required string Permission { get; init; }
}
