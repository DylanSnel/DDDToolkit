namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// The starter roles of another module in the tenant the current work acts in. Tenancy knows nothing of that module. The
/// module registers one of these, and <c>TenantManager.ProvisionAsync</c> and <see cref="IStarterRoleSync"/> run every
/// one that is registered. Membership's generator writes it for a member class whose roles are kept per resource, and
/// <c>AddBoardMembershipWithTenancy</c> registers it, so an application writes none for its boards.
/// <code>
/// public sealed class ReportTemplateStarterRoles(ReportRoles roles, TenancyAccessManager access) : ITenantStarterRoles
/// {
///     public async Task&lt;TenantStarterRolesFollowed&gt; FollowAsync(CancellationToken cancellationToken)
///     {
///         var made = await roles.MakeMissingAsync(access.CurrentTenant.Require().Id, cancellationToken);
///         return new TenantStarterRolesFollowed(made, RolesChanged: 0, RolesWithoutTheirStarterRole: 0);
///     }
/// }
/// </code>
/// <para>
/// Register it per scope, and take what it needs from the scope: <c>services.TryAddEnumerable(ServiceDescriptor.Scoped&lt;ITenantStarterRoles, ReportTemplateStarterRoles&gt;())</c>.
/// Each one runs as system work in the tenant, in a scope of its own that begins after that system work, so a context
/// it takes from the scope runs as that work. It must make what is missing and change what has to change, and save only
/// when something changed, because it runs again at every start of the host.
/// </para>
/// <para>
/// It runs in another save than the tenant's. A module has its own context, so the tenant is saved first. A failure in
/// one of these does not fail <c>ProvisionAsync</c>. It is logged as an error, and the next <c>services.SyncStarterRoles()</c>
/// runs it again.
/// </para>
/// </summary>
public interface ITenantStarterRoles
{
    /// <summary>Makes the starter roles the current tenant lacks, and makes the rest follow their starter roles.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many roles it made, how many changed, and how many have no starter role any more.</returns>
    Task<TenantStarterRolesFollowed> FollowAsync(CancellationToken cancellationToken);
}
