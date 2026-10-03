namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// The names of Tenancy's tables. The tables live in the default schema of the context that maps them.
/// <para>
/// A module that reads them through <c>AddTenancyReadModel</c> maps views over the same names, so an
/// application that renames a table passes the same names there too; a view over a name nobody created
/// fails on its first query rather than reading something else.
/// </para>
/// <para>
/// The tables are named here, and the two indexes <c>AddTenancy</c> names itself (<see cref="RootIndex"/> and
/// <see cref="PrimaryIndex"/>). Every column, key and other index is named as the context names everything else,
/// by Entity Framework or by a naming convention the context uses, and every SQL statement Tenancy writes reads
/// those names from the model.
/// </para>
/// </summary>
/// <param name="Tenants">The tenants.</param>
/// <param name="Organizations">Each tenant's organization.</param>
/// <param name="Units">The organizations' units.</param>
/// <param name="UnitPaths">The closure of each organization's tree.</param>
/// <param name="Seats">The seats.</param>
/// <param name="Placements">Where each seat is placed.</param>
/// <param name="Grants">The roles each placement grants.</param>
/// <param name="Rights">The keys each seat holds where, written from the seats and the roles.</param>
/// <param name="Roles">The roles.</param>
/// <param name="AccessRevisions">One row per tenant that every change of rights takes first.</param>
/// <param name="Invitations">The invitations, in a context that maps them with <c>AddTenancyInvitations</c>.</param>
/// <param name="InvitationDigests">The digests of the invitations' tokens, kept apart from the invitations.</param>
public sealed record TenancyTableNames(
    string Tenants = "Tenants",
    string Organizations = "Organizations",
    string Units = "OrganizationUnits",
    string UnitPaths = "OrganizationUnitPaths",
    string Seats = "Seats",
    string Placements = "SeatPlacements",
    string Grants = "SeatRoleGrants",
    string Rights = "SeatRights",
    string Roles = "Roles",
    string AccessRevisions = "TenancyAccessRevisions",
    string Invitations = "Invitations",
    string InvitationDigests = "InvitationDigests")
{
    /// <summary>The names every call uses when it is given none.</summary>
    public static TenancyTableNames Default { get; } = new();

    /// <summary>
    /// The unique index that keeps a tenant's organization to a single root, which <c>AddTenancy</c> makes when
    /// it is given the context's database. Named after the units' table when not given:
    /// <c>IX_OrganizationUnits_TenantId_WhereRoot</c>. It is on the same column as the plain index on a unit's
    /// tenant, so it has a name of its own, which a naming convention leaves as it is.
    /// </summary>
    public string? RootIndex { get; init; }

    /// <summary>
    /// The unique index that keeps a seat to a single primary placement, made with <see cref="RootIndex"/>. Named
    /// after the placements' table when not given: <c>IX_SeatPlacements_SeatId_WherePrimary</c>.
    /// </summary>
    public string? PrimaryIndex { get; init; }

    /// <summary>
    /// The same tables, and the two indexes, in snake_case, for a context that names its columns that way with a
    /// naming convention, such as <c>UseSnakeCaseNamingConvention()</c>. A convention renames only what was left
    /// to it, and <c>AddTenancy</c> gives these their names, so they are named here:
    /// <code>
    /// modelBuilder.AddTenancy(TenancyTableNames.SnakeCase, Database);
    /// </code>
    /// </summary>
    public static TenancyTableNames SnakeCase { get; } = new(
        Tenants: "tenants",
        Organizations: "organizations",
        Units: "organization_units",
        UnitPaths: "organization_unit_paths",
        Seats: "seats",
        Placements: "seat_placements",
        Grants: "seat_role_grants",
        Rights: "seat_rights",
        Roles: "roles",
        AccessRevisions: "tenancy_access_revisions",
        Invitations: "invitations",
        InvitationDigests: "invitation_digests")
    {
        RootIndex = "ix_organization_units_tenant_id_where_root",
        PrimaryIndex = "ix_seat_placements_seat_id_where_primary",
    };
}
