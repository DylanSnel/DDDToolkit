namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Brings every tenant's roles up to the packs they were made from: the use case behind
/// <c>services.SyncRolePacks()</c>, for a host that runs it itself, from a deployment step or an operator's
/// endpoint.
/// <code>
/// var report = await services.GetRequiredService&lt;IRolePackSync&gt;().SyncAsync(cancellationToken);
/// </code>
/// <para>
/// It visits the active and the suspended tenants, one after the other, and in each runs
/// <c>RoleCommands.FollowPacksAsync</c> as Tenancy's system work in that tenant: it begins that work itself, so
/// whoever calls it needs no caller of its own, and what it may do in a tenant is what system work there may.
/// The events it raises name the system as who made the change. Each tenant is a unit of work of its own: a
/// tenant whose sync fails is named in the report and left for the next run, and the others go on. A tenant
/// another run changed at the same moment is tried again, and then finds nothing left to change.
/// </para>
/// <para>
/// Tenancy's storage package registers it with <c>AddTenancy</c>; a host on other storage registers its own.
/// </para>
/// </summary>
public interface IRolePackSync
{
    /// <summary>Makes every role of every active or suspended tenant follow the pack it was made from.</summary>
    /// <param name="cancellationToken">Cancels the work, between two tenants or inside one.</param>
    /// <returns>What it did, counted over the tenants, with every tenant it could not sync.</returns>
    Task<RolePackSyncReport> SyncAsync(CancellationToken cancellationToken);
}
