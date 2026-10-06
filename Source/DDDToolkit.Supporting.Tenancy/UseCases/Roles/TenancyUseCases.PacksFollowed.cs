namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// What following the packs did in one tenant (<see cref="RoleCommands.FollowPacksAsync"/>). A role that follows
    /// its pack already, one made by hand and one that is archived are in none of the lists.
    /// </summary>
    /// <param name="Changed">
    /// The roles whose keys changed, each with a <see cref="RoleFollowedItsPack{TTenantId, TRoleId, TSeatId}"/> raised.
    /// </param>
    /// <param name="KeptForAnAdministrator">
    /// The roles left as they were, keys and record, because following their pack would take the administrator key
    /// from the tenant's last administrators. Each follows at a later run, once the tenant has an administrator
    /// through another role.
    /// </param>
    /// <param name="WithoutTheirPack">
    /// The roles made from a pack the catalogue no longer has. They keep their keys, and follow the pack again if
    /// the application declares it again under the same key.
    /// </param>
    public sealed record PacksFollowed(
        IReadOnlyList<TRoleId> Changed,
        IReadOnlyList<TRoleId> KeptForAnAdministrator,
        IReadOnlyList<TRoleId> WithoutTheirPack);
}
