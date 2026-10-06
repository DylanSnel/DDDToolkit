using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// The one mapping of Tenancy's rows. <c>AddTenancy</c> maps the tables with it, <c>AddTenancyReadModel</c>
/// the views another module reads those tables through, and <c>AddTenancyReadFunctions</c> the functions a
/// module asks where it reads no table of Tenancy's, so neither a view nor a function's row can describe a
/// column differently from the table it comes from: the column settings a table and its row share are written
/// once, by name, and applied through whichever builder holds the column, an owned type in Tenancy's model or a
/// keyless row in a consumer's.
/// <para>
/// A row has fewer columns than its table: it carries what an access rule reads, and no text that is shown to
/// people. So a seat's display name, a unit's name and a role's name are mapped with the tables alone,
/// and the shared settings name only what both have.
/// </para>
/// <list type="bullet">
/// <item>Every enum is stored as its name, at most <see cref="EnumLength"/> characters.</item>
/// <item>Every timestamp is stored as its UTC instant in a <see cref="DateTime"/> column, which SQLite can compare in SQL.</item>
/// <item>A role's keys are a primitive collection, a JSON document on SQLite and SQL Server.</item>
/// <item>Columns are named after the properties.</item>
/// <item>Every row that has a tenant gets the tenant filter, through <see cref="TenancyModelBuilderExtensions.ScopeToTenant"/>.</item>
/// <item>Every unique index says which of Tenancy's refusals a save that breaks it gets, so a rule the use case
/// checked first and a racing save broke anyway is answered the same way, on any database.</item>
/// </list>
/// The ids are converted by the application's own converters, which its context registers in
/// <c>ConfigureConventions</c> as for any other id.
/// </summary>
internal static class TenancyMapping
{
    /// <summary>The longest enum name a column holds.</summary>
    internal const int EnumLength = 32;

    /// <summary>
    /// The annotation <see cref="TenancyModelBuilderExtensions.ScopeToTenant"/> puts on an entity type: the name of
    /// the property that holds its tenant, which the save check reads.
    /// </summary>
    internal const string TenantPropertyAnnotation = "DDDToolkit:Tenancy:TenantProperty";

    /// <summary>
    /// The column of a unit that holds its organization's id, which is the tenant's, and of a placement that
    /// holds its seat's tenant: a shadow property of each.
    /// </summary>
    internal const string TenantColumn = "TenantId";

    /// <summary>The column of a placement and of a grant that holds their seat's id: a shadow property.</summary>
    internal const string SeatColumn = "SeatId";

    /// <summary>
    /// The column of a role that holds its name as <see cref="NormalizedRoleName"/> gives it: a shadow property the
    /// save fills in, which makes a tenant's role names unique ignoring case in the database, whatever its collation.
    /// </summary>
    internal const string NormalizedNameColumn = "NormalizedName";

    /// <summary>
    /// The annotation on the row of <see cref="TenancyFunctionNames.SeatsHoldingAt"/>: the schema Tenancy's
    /// functions are in, where it is not the model's own default schema, as in a consumer's model.
    /// </summary>
    internal const string FunctionSchemaAnnotation = "DDDToolkit:Tenancy:FunctionSchema";

    private const string UnitColumn = "UnitId";

    private static readonly ValueConverter<TenantSlug, string> SlugConverter = new(
        static slug => slug.Value,
        static value => TenantSlug.Create(value));

    /// <summary>
    /// A role's name as it is compared: in capitals, as the invariant culture writes them, so two names that differ
    /// only in case, accented letters included, are the same name. Done here rather than in SQL, because a
    /// database's own upper and lower case, SQLite's among them, may leave letters outside ASCII as they are.
    /// </summary>
    internal static string NormalizedRoleName(string name) => name.ToUpperInvariant();

    /// <summary>The tenants' table.</summary>
    internal static void Tenants<TTenant, TTenantId>(EntityTypeBuilder<TTenant> tenant, TenancyTableNames tables)
        where TTenant : TenantAggregate<TTenantId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    {
        tenant.ToTable(tables.Tenants);
        tenant.HasKey(row => row.Id);
        tenant.Property(row => row.Id).ValueGeneratedNever();
        tenant.Property(row => row.Slug).HasConversion(SlugConverter).HasMaxLength(TenantSlug.MaxLength);
        Refuses(tenant.HasIndex(row => row.Slug).IsUnique(), TenancyRefusals.SlugTaken);
        AsName(tenant.Property(row => row.Status));
        AsName(tenant.Property(row => row.Shape));
        tenant.Property(row => row.StatusReason).HasMaxLength(TenantAggregate<TTenantId>.MaxReasonLength);
        tenant.ScopeToTenant(row => row.Id);
    }

    /// <summary>
    /// The organizations' table, and their units as an owned collection in a table of their own. With
    /// <paramref name="unique"/>, the units' table also has a unique index on the tenant over the units with no
    /// parent, so each tenant keeps a single root in the database as well as in the organization.
    /// </summary>
    internal static void Organizations<TOrganization, TTenantId, TUnit, TUnitId>(EntityTypeBuilder<TOrganization> organization, TenancyTableNames tables, UniqueFilters? unique)
        where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnit : OrganizationUnitEntity<TUnitId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    {
        organization.ToTable(tables.Organizations);
        organization.HasKey(row => row.Id);
        organization.Property(row => row.Id).ValueGeneratedNever();
        organization.Property(row => row.Name).HasMaxLength(OrganizationAggregate<TTenantId, TUnit, TUnitId>.MaxNameLength);

        organization.OwnsMany(row => row.Units, unit =>
        {
            unit.ToTable(tables.Units);

            // The organization's id is its tenant's, so the owner's key is the unit's tenant column.
            unit.WithOwner().HasForeignKey(TenantColumn);
            unit.HasKey(row => row.Id);
            unit.Property(row => row.Id).ValueGeneratedNever();
            unit.HasIndex(TenantColumn);
            unit.HasIndex(row => row.ParentId);
            unit.Property(row => row.Name).HasMaxLength(OrganizationUnitEntity<TUnitId>.MaxNameLength);
            UnitColumns<TTenantId, TUnitId>(name => unit.Property(name));

            // Last, once the table and its columns have their names: the index's condition names a column.
            if (unique is not null)
            {
                Refuses(
                    unit.HasIndex([TenantColumn], RootIndexName(tables)).HasDatabaseName(RootIndexName(tables))
                        .IsUnique().HasFilter(unique.Root(ColumnOf(unit.Property(row => row.ParentId)))),
                    TenancyRefusals.OneRoot);
            }
        });

        organization.Navigation(row => row.Units).UsePropertyAccessMode(PropertyAccessMode.Field);
        organization.ScopeToTenant(row => row.Id);
    }

    /// <summary>
    /// The seats' table, their placements as an owned collection, and each placement's grants as an owned
    /// collection of the placement. A placement is keyed by its seat and unit, a grant by its placement and role.
    /// <list type="bullet">
    /// <item>A seat's identity is unique within its tenant, and the index leads on the identity, because finding a
    /// person's seats by it, across tenants, is the one lookup every request makes.</item>
    /// <item>A placement keeps its seat's tenant as well, so its view has a tenant to filter on.</item>
    /// <item>A grant is indexed on its role, for the save that rewrites the rights of a role's holders.</item>
    /// <item>With <paramref name="unique"/>, a seat keeps a single primary placement in the database as well: a
    /// unique index on the seat over the primary placements.</item>
    /// </list>
    /// </summary>
    internal static void Seats<TSeat, TSeatId, TTenantId, TUnitId, TRoleId>(EntityTypeBuilder<TSeat> seat, TenancyTableNames tables, UniqueFilters? unique)
        where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        seat.ToTable(tables.Seats);
        seat.HasKey(row => row.Id);
        seat.Property(row => row.Id).ValueGeneratedNever();
        seat.HasIndex(row => row.TenantId);
        Refuses(seat.HasIndex(row => new { row.Identity, row.TenantId }).IsUnique(), TenancyRefusals.IdentityHasSeat);
        seat.Property(row => row.DisplayName).HasMaxLength(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>.MaxDisplayNameLength);
        SeatColumns<TSeatId, TTenantId>(name => seat.Property(name));

        seat.OwnsMany(row => row.Placements, placement =>
        {
            placement.ToTable(tables.Placements);
            placement.WithOwner().HasForeignKey(SeatColumn);
            placement.HasKey(SeatColumn, nameof(Placement<TSeatId, TUnitId, TRoleId>.UnitId));
            placement.Property<TTenantId>(TenantColumn);
            placement.Property(row => row.PlacedAt).HasConversion(new UtcDateTimeOffsetConverter());

            // Once the table and its columns have their names: the index's condition names a column.
            if (unique is not null)
            {
                Refuses(
                    placement.HasIndex([SeatColumn], PrimaryIndexName(tables)).HasDatabaseName(PrimaryIndexName(tables))
                        .IsUnique().HasFilter(unique.Primary(ColumnOf(placement.Property(row => row.IsPrimary)))),
                    TenancyRefusals.SecondPrimary);
            }

            placement.OwnsMany(row => row.Grants, grant =>
            {
                grant.ToTable(tables.Grants);
                grant.WithOwner().HasForeignKey(SeatColumn, UnitColumn);
                grant.HasKey(SeatColumn, UnitColumn, nameof(RoleGrant<TSeatId, TRoleId>.RoleId));
                grant.HasIndex(nameof(RoleGrant<TSeatId, TRoleId>.RoleId));
                grant.Property(row => row.StartsAt).HasConversion(new UtcDateTimeOffsetConverter());
                grant.Property(row => row.EndsAt).HasConversion(new NullableUtcDateTimeOffsetConverter());
                grant.Property(row => row.Reason).HasMaxLength(SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>.MaxReasonLength);
            });

            placement.Navigation(row => row.Grants).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        seat.Navigation(row => row.Placements).UsePropertyAccessMode(PropertyAccessMode.Field);
        seat.ScopeToTenant(row => row.TenantId);
    }

    /// <summary>
    /// The roles' table. A tenant's role names are unique ignoring case, through the normalized name the save
    /// fills in, rather than through the name, which a case-sensitive collation would compare exactly. What its
    /// pack gave a role is the table's alone, a column that may be empty: a role made by hand has no pack, and a
    /// role stored before the column was there remembers nothing yet.
    /// </summary>
    internal static void Roles<TRole, TRoleId, TTenantId>(EntityTypeBuilder<TRole> role, TenancyTableNames tables)
        where TRole : RoleAggregate<TRoleId, TTenantId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    {
        role.ToTable(tables.Roles);
        role.HasKey(row => row.Id);
        role.Property(row => row.Id).ValueGeneratedNever();
        role.Property<string>(NormalizedNameColumn).IsRequired().HasMaxLength(RoleAggregate<TRoleId, TTenantId>.MaxNameLength);
        Refuses(role.HasIndex(nameof(RoleAggregate<TRoleId, TTenantId>.TenantId), NormalizedNameColumn).IsUnique(), TenancyRefusals.RoleNameTaken);
        role.Property(row => row.Name).HasMaxLength(RoleAggregate<TRoleId, TTenantId>.MaxNameLength);
        role.Property(row => row.Description).HasMaxLength(RoleAggregate<TRoleId, TTenantId>.MaxDescriptionLength);
        role.PrimitiveCollection(row => row.KeysFromPack);
        RoleColumns<TRoleId, TTenantId>(role);
        role.ScopeToTenant(row => row.TenantId);
    }

    /// <summary>
    /// The keys seats hold where: a table in Tenancy's model, and a row of the read model, with
    /// <paramref name="read"/>, in a consumer's. The table is indexed on the role as well as on the seat, for the
    /// save that rewrites the rows of a role that changed.
    /// </summary>
    internal static void Rights<TTenantId, TSeatId, TUnitId, TRoleId>(EntityTypeBuilder<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>> right, TenancyTableNames tables, ReadModel? read)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        if (read is null)
        {
            right.ToTable(tables.Rights);
            right.HasKey(row => new { row.SeatId, row.UnitId, row.RoleId, row.Key });
            right.HasIndex(row => row.TenantId);
            right.HasIndex(row => new { row.SeatId, row.Key });
            right.HasIndex(row => row.RoleId);
        }
        else
        {
            read.Map(right, tables.Rights, TenancyFunctionNames.CallerRights);
        }

        right.Property(row => row.Key).HasMaxLength(Permission.MaxKeyLength);
        right.Property(row => row.StartsAt).HasConversion(new UtcDateTimeOffsetConverter());
        right.Property(row => row.EndsAt).HasConversion(new NullableUtcDateTimeOffsetConverter());
        right.ScopeToTenant(row => row.TenantId);
        read?.NameColumns(right);
    }

    /// <summary>
    /// The closure of every organization's tree: a table in Tenancy's model, and a row of the read model, with
    /// <paramref name="read"/>, in a consumer's.
    /// </summary>
    internal static void UnitPaths<TTenantId, TUnitId>(EntityTypeBuilder<OrganizationUnitPath<TTenantId, TUnitId>> path, TenancyTableNames tables, ReadModel? read)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    {
        if (read is null)
        {
            path.ToTable(tables.UnitPaths);
            path.HasKey(row => new { row.AncestorId, row.DescendantId });
            path.HasIndex(row => row.TenantId);
            path.HasIndex(row => row.DescendantId);
        }
        else
        {
            read.Map(path, tables.UnitPaths, TenancyFunctionNames.TenantUnitPaths);
        }

        path.ScopeToTenant(row => row.TenantId);
        read?.NameColumns(path);
    }

    /// <summary>The tenants' access revisions.</summary>
    internal static void AccessRevisions<TTenantId>(EntityTypeBuilder<TenancyAccessRevision<TTenantId>> revision, TenancyTableNames tables)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    {
        revision.ToTable(tables.AccessRevisions);
        revision.HasKey(row => row.TenantId);
        revision.Property(row => row.TenantId).ValueGeneratedNever();
        revision.Property(row => row.Revision).IsConcurrencyToken();
        revision.ScopeToTenant(row => row.TenantId);
    }

    /// <summary>
    /// The invitations' table. An invitation is found by its tenant with the state and the end it is listed by.
    /// What it offers is fixed once the row is there: Entity Framework refuses a save that changed one of those
    /// columns, and the privileges an export writes from the policies leave them out of <c>UPDATE</c>.
    /// </summary>
    internal static void Invitations<TInvitation, TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>(EntityTypeBuilder<TInvitation> invitation, TenancyTableNames tables)
        where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
        where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        invitation.ToTable(tables.Invitations);
        invitation.HasKey(row => row.Id);
        invitation.Property(row => row.Id).ValueGeneratedNever();
        invitation.HasIndex(row => new { row.TenantId, row.State, row.ExpiresAt });
        invitation.Property(row => row.Address).HasMaxLength(InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>.MaxAddressLength);
        invitation.Property(row => row.DisplayName).HasMaxLength(InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>.MaxDisplayNameLength);
        AsName(invitation.Property(row => row.State));

        invitation.Property(row => row.UnitId).IsFixedAfterInsert();
        invitation.Property(row => row.RoleId).IsFixedAfterInsert();
        invitation.Property(row => row.GrantUntil).HasConversion(new NullableUtcDateTimeOffsetConverter()).IsFixedAfterInsert();
        invitation.Property(row => row.IssuedAt).HasConversion(new UtcDateTimeOffsetConverter()).IsFixedAfterInsert();
        invitation.Property(row => row.ExpiresAt).HasConversion(new UtcDateTimeOffsetConverter()).IsFixedAfterInsert();
        invitation.Property(row => row.IssuedBy).IsFixedAfterInsert();
        invitation.Property(row => row.IssuedAsSystem).IsFixedAfterInsert();
        invitation.Property(row => row.AcceptedAt).HasConversion(new NullableUtcDateTimeOffsetConverter());
        invitation.Property(row => row.ClosedAt).HasConversion(new NullableUtcDateTimeOffsetConverter());
        invitation.ScopeToTenant(row => row.TenantId);
    }

    /// <summary>
    /// The table of the digests of the invitations' tokens: one row per invitation, keyed by it and written after
    /// it, with a unique index on the digest, which is what a token is found by. A row never changes.
    /// </summary>
    internal static void InvitationDigests<TInvitation, TInvitationId, TTenantId>(EntityTypeBuilder<TenancyInvitationDigest<TInvitationId, TTenantId>> digest, TenancyTableNames tables)
        where TInvitation : class
        where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    {
        digest.ToTable(tables.InvitationDigests);
        digest.HasKey(row => row.InvitationId);
        digest.Property(row => row.InvitationId).ValueGeneratedNever();
        digest.Property(row => row.Digest).HasMaxLength(Security.BearerTokens.DigestLength).IsFixedAfterInsert();
        digest.HasIndex(row => row.Digest).IsUnique();
        digest.HasIndex(row => row.TenantId);

        // The invitation is saved first, and takes its digest with it when it goes.
        digest.HasOne<TInvitation>().WithOne().HasForeignKey<TenancyInvitationDigest<TInvitationId, TTenantId>>(row => row.InvitationId).OnDelete(DeleteBehavior.Cascade);
        digest.ScopeToTenant(row => row.TenantId);
    }

    /// <summary>
    /// The invitation a token's digest is for, as <see cref="TenancyFunctionNames.InvitationOfDigest"/> answers it:
    /// mapped to nothing, and read from the SQL that asks the function, under the column names the function gives.
    /// </summary>
    internal static void InvitationsOfDigests<TTenantId, TInvitationId, TSeatId>(EntityTypeBuilder<InvitationOfDigestRow<TTenantId, TInvitationId, TSeatId>> found)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        ToNothing(found);
        found.Property(row => row.TenantId).HasColumnName(nameof(InvitationOfDigestRow<TTenantId, TInvitationId, TSeatId>.TenantId));
        found.Property(row => row.InvitationId).HasColumnName(nameof(InvitationOfDigestRow<TTenantId, TInvitationId, TSeatId>.InvitationId));
        found.Property(row => row.IssuedBy).HasColumnName(nameof(InvitationOfDigestRow<TTenantId, TInvitationId, TSeatId>.IssuedBy));
    }

    /// <summary>
    /// The units, read as rows, without their names: from a view over the units' table, or from
    /// Tenancy's function.
    /// </summary>
    internal static void UnitRows<TTenantId, TUnitId>(EntityTypeBuilder<OrganizationUnitRow<TTenantId, TUnitId>> unit, TenancyTableNames tables, ReadModel read)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    {
        read.Map(unit, tables.Units, TenancyFunctionNames.TenantUnits);
        UnitColumns<TTenantId, TUnitId>(name => unit.Property(name));
        unit.ScopeToTenant(row => row.TenantId);
        read.NameColumns(unit);
    }

    /// <summary>
    /// The roles, read as rows, without their names: from a view over the roles' table, or from Tenancy's function.
    /// </summary>
    internal static void RoleRows<TTenantId, TRoleId>(EntityTypeBuilder<RoleRow<TTenantId, TRoleId>> role, TenancyTableNames tables, ReadModel read)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        read.Map(role, tables.Roles, TenancyFunctionNames.TenantRoles);
        RoleColumns<TRoleId, TTenantId>(role);
        role.ScopeToTenant(row => row.TenantId);
        read.NameColumns(role);
    }

    /// <summary>
    /// The placements, read as rows: from a view over the placements' table, or from Tenancy's function. The row
    /// names no tenant, since the questions reach it through a seat's id, but the table keeps the seat's; the row
    /// reads that column as a shadow property and gets the tenant filter on it, so reading the set on its own
    /// shows no other tenant's.
    /// </summary>
    internal static void PlacementRows<TTenantId, TSeatId, TUnitId>(EntityTypeBuilder<PlacementRow<TSeatId, TUnitId>> placement, TenancyTableNames tables, ReadModel read)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    {
        read.Map(placement, tables.Placements, TenancyFunctionNames.TenantPlacements);
        placement.Property<TTenantId>(TenantColumn);
        placement.ScopeToTenantThrough(row => EF.Property<TTenantId>(row, TenantColumn), TenantColumn);
        read.NameColumns(placement);
    }

    /// <summary>
    /// The seats, read as rows, without the identity and the display name: from a view over the seats' table, or
    /// from Tenancy's function.
    /// </summary>
    internal static void SeatRows<TTenantId, TSeatId>(EntityTypeBuilder<SeatRow<TTenantId, TSeatId>> seat, TenancyTableNames tables, ReadModel read)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        read.Map(seat, tables.Seats, TenancyFunctionNames.TenantSeats);
        SeatColumns<TSeatId, TTenantId>(name => seat.Property(name));
        seat.ScopeToTenant(row => row.TenantId);
        read.NameColumns(seat);
    }

    /// <summary>
    /// A tenant's administrators, as <see cref="TenancyFunctionNames.TenantAdministrators"/> answers them: mapped
    /// to nothing, and read from the SQL that asks the function. The columns are named as the function names them,
    /// whatever a naming convention would make of the properties.
    /// </summary>
    internal static void TenantAdministrators<TSeatId, TRoleId>(EntityTypeBuilder<TenantAdministratorRow<TSeatId, TRoleId>> administrator)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        ToNothing(administrator);
        administrator.Property(row => row.SeatId).HasColumnName(nameof(TenantAdministratorRow<TSeatId, TRoleId>.SeatId));
        administrator.Property(row => row.RoleId).HasColumnName(nameof(TenantAdministratorRow<TSeatId, TRoleId>.RoleId));
    }

    /// <summary>
    /// The rights a move changes, as <see cref="TenancyFunctionNames.RightsAMoveChanges"/> answers them: mapped to
    /// nothing, and read from the SQL that asks the function, under the column names the function gives.
    /// </summary>
    internal static void MoveReaches<TUnitId>(EntityTypeBuilder<MoveReach<TUnitId>> reach)
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    {
        ToNothing(reach);
        reach.Property(row => row.UnitId).HasColumnName(nameof(MoveReach<TUnitId>.UnitId));
        reach.Property(row => row.Key).HasColumnName(nameof(MoveReach<TUnitId>.Key));
        reach.Property(row => row.EndsAt).HasColumnName(nameof(MoveReach<TUnitId>.EndsAt)).HasConversion(new NullableUtcDateTimeOffsetConverter());
        reach.Property(row => row.Parent).HasColumnName(nameof(MoveReach<TUnitId>.Parent));
        reach.Property(row => row.OfCaller).HasColumnName(nameof(MoveReach<TUnitId>.OfCaller));
    }

    /// <summary>
    /// The tenants a round of system work visits, as <see cref="TenancyFunctionNames.TenantsToSweep"/> answers
    /// them: mapped to nothing, and read from the SQL that asks the function, which names its one column.
    /// </summary>
    internal static void TenantsToSweep<TTenantId>(EntityTypeBuilder<TenantToSweepRow<TTenantId>> tenant)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    {
        ToNothing(tenant);
        tenant.Property(row => row.TenantId).HasColumnName(nameof(TenantToSweepRow<TTenantId>.TenantId));
    }

    /// <summary>
    /// The seats that hold a key at a unit, as <see cref="TenancyFunctionNames.SeatsHoldingAt"/> answers them:
    /// mapped to nothing, and read from the SQL that asks the function, in the schema <paramref name="read"/> names.
    /// </summary>
    internal static void SeatHolders<TSeatId>(EntityTypeBuilder<SeatHolderRow<TSeatId>> holder, ReadModel read)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        ToNothing(holder);
        holder.Property(row => row.SeatId).HasColumnName(nameof(SeatHolderRow<TSeatId>.SeatId));
        holder.HasAnnotation(FunctionSchemaAnnotation, read.Schema);
    }

    /// <summary>
    /// Maps <paramref name="row"/>, which has no key, to no table and no view: it is read from SQL that asks one of
    /// Tenancy's functions, and a migration makes nothing for it. The table is taken away as well as the view,
    /// because a naming convention gives every entity type a table name as it is added to the model, and that
    /// name would otherwise stand.
    /// </summary>
    private static void ToNothing<TRow>(EntityTypeBuilder<TRow> row)
        where TRow : class
        => row.HasNoKey().ToTable((string?)null).ToView(null);

    /// <summary>
    /// The columns a unit's owned type and its row share: the status. The name is the table's alone, and so is
    /// every column of the application's own unit class.
    /// </summary>
    private static void UnitColumns<TTenantId, TUnitId>(Func<string, PropertyBuilder> column)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        => AsName(column(nameof(OrganizationUnitRow<TTenantId, TUnitId>.Status)));

    /// <summary>The columns a seat and its row share: the status. The display name is the table's alone.</summary>
    private static void SeatColumns<TSeatId, TTenantId>(Func<string, PropertyBuilder> column)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        => AsName(column(nameof(SeatRow<TTenantId, TSeatId>.Status)));

    /// <summary>
    /// The columns a role and its row share: the pack, the status and the keys. The name is the table's alone.
    /// </summary>
    private static void RoleColumns<TRoleId, TTenantId>(EntityTypeBuilder role)
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    {
        role.Property(nameof(RoleRow<TTenantId, TRoleId>.FromPack)).HasMaxLength(RolePack.MaxKeyLength);
        AsName(role.Property(nameof(RoleRow<TTenantId, TRoleId>.Status)));
        role.PrimitiveCollection(nameof(RoleRow<TTenantId, TRoleId>.Keys));
    }

    /// <summary>
    /// The name of the unique index on a tenant's root, in the model and in the database: given to both, since a
    /// naming convention names an index after its table and columns, which would be the name of the plain index on
    /// the same column.
    /// </summary>
    internal static string RootIndexName(TenancyTableNames tables) => tables.RootIndex ?? "IX_" + tables.Units + "_TenantId_WhereRoot";

    /// <summary>The name of the unique index on a seat's primary placement, in the model and in the database.</summary>
    internal static string PrimaryIndexName(TenancyTableNames tables) => tables.PrimaryIndex ?? "IX_" + tables.Placements + "_SeatId_WherePrimary";

    /// <summary>Stores an enum as its name.</summary>
    private static void AsName(PropertyBuilder property) => property.HasConversion<string>().HasMaxLength(EnumLength);

    /// <summary>
    /// The column <paramref name="property"/> is stored in, as the model names it when <c>AddTenancy</c> maps it:
    /// after the property, unless the application named the column before, or its context has a naming convention,
    /// which names a column as soon as its property is there. The filter of an index is SQL the provider runs as
    /// written, so it names the column it reads, and does not follow a name the column is given afterwards.
    /// </summary>
    private static string ColumnOf(PropertyBuilder property) => property.Metadata.GetColumnName();

    /// <summary>
    /// Says which of Tenancy's refusals a save that breaks the unique <paramref name="index"/> is answered with:
    /// the code, its kind and its English text as <see cref="TenancyRefusals"/> has them, so the index and the use
    /// case that checks the same rule first give one answer. What the text names, <c>{Slug}</c> or <c>{Name}</c>,
    /// is filled from the row that broke the index.
    /// </summary>
    private static void Refuses(IndexBuilder index, string code)
        => index.RefusesAs(code, TenancyRefusals.TemplateOf(code), TenancyRefusals.KindOf(code));

    /// <summary>
    /// The conditions of the two filtered unique indexes, in the SQL of the provider they are for: a tenant's
    /// organization has a single root, and a seat a single primary placement. The aggregates keep both rules; the indexes keep them for a
    /// write that goes past the aggregates, such as SQL of the application's own. A provider this does not know
    /// gets neither index, since a filter is SQL the provider runs as written.
    /// </summary>
    /// <param name="Root">Which units are roots, those with no parent, given the column that holds a unit's parent.</param>
    /// <param name="Primary">Which placements are primary, given the column that says so.</param>
    internal sealed record UniqueFilters(Func<string, string> Root, Func<string, string> Primary)
    {
        /// <summary>The conditions for the provider named <paramref name="providerName"/>, or <see langword="null"/> for one without them.</summary>
        internal static UniqueFilters? For(string? providerName) => providerName switch
        {
            "Npgsql.EntityFrameworkCore.PostgreSQL" => new(parent => Quoted(parent) + " IS NULL", Quoted),
            "Microsoft.EntityFrameworkCore.Sqlite" => new(parent => Quoted(parent) + " IS NULL", primary => Quoted(primary) + " = 1"),
            "Microsoft.EntityFrameworkCore.SqlServer" => new(parent => Bracketed(parent) + " IS NULL", primary => Bracketed(primary) + " = CAST(1 AS bit)"),
            _ => null,
        };

        /// <summary>A column's name as Postgres and SQLite quote it.</summary>
        private static string Quoted(string column) => "\"" + column.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

        /// <summary>A column's name as SQL Server quotes it.</summary>
        private static string Bracketed(string column) => "[" + column.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    /// <summary>
    /// Where a model reads the rows of the read model from: views over Tenancy's tables, each named as its table,
    /// or Tenancy's read functions (<see cref="TenancyFunctionNames"/>), each of which answers one of the rows.
    /// </summary>
    /// <param name="Schema">The schema Tenancy's tables and functions are in, or <see langword="null"/> for the model's default.</param>
    /// <param name="ThroughFunctions">Whether the rows are read from the functions rather than from views.</param>
    internal sealed record ReadModel(string? Schema, bool ThroughFunctions = false)
    {
        /// <summary>Maps <paramref name="row"/>, which has no key, to the view named <paramref name="table"/> or to <paramref name="function"/>.</summary>
        public void Map<TRow>(EntityTypeBuilder<TRow> row, string table, string function)
            where TRow : class
        {
            row.HasNoKey();
            if (!ThroughFunctions)
            {
                row.ToView(table, Schema);
            }
            else if (Schema is null)
            {
                row.ToFunction(function);
            }
            else
            {
                row.ToFunction(function, mapped => mapped.HasSchema(Schema));
            }
        }

        /// <summary>
        /// Names every column of a row read from a function after its property, a shadow one included: the names
        /// the function answers under, which are the same whatever Tenancy's tables call their columns, and which a
        /// naming convention of the consumer's context would otherwise rename. Called once the row's properties are
        /// all there. A view's columns are the table's, and follow the context's naming as the table's do.
        /// </summary>
        public void NameColumns<TRow>(EntityTypeBuilder<TRow> row)
            where TRow : class
        {
            if (!ThroughFunctions)
            {
                return;
            }

            foreach (var property in row.Metadata.GetProperties())
            {
                property.SetColumnName(property.Name);
            }
        }
    }
}
