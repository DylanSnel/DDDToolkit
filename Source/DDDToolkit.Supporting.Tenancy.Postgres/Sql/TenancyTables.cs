using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Tenancy.Postgres;

/// <summary>
/// Tenancy's tables as one model maps them: the entity type of each, and the names SQL gives them, their
/// columns and their stored values, read from the model rather than assumed, so the SQL follows an application
/// that renames a table, gives Tenancy another schema, or keys its tenants on a <c>long</c>.
/// </summary>
internal sealed class TenancyTables
{
    private TenancyTables(IModel model, IReadOnlyDictionary<TenancyTable, IEntityType> tables)
    {
        Schema = model.GetDefaultSchema() ?? PostgresRowAccess.DefaultSchema;
        Tenants = tables[TenancyTable.Tenant];
        Organizations = tables[TenancyTable.Organization];
        Units = tables[TenancyTable.Unit];
        UnitPaths = tables[TenancyTable.UnitPath];
        Seats = tables[TenancyTable.Seat];
        Placements = tables[TenancyTable.Placement];
        Grants = tables[TenancyTable.Grant];
        Rights = tables[TenancyTable.Right];
        Roles = tables[TenancyTable.Role];
        AccessRevisions = tables[TenancyTable.AccessRevision];
        Invitations = tables.GetValueOrDefault(TenancyTable.Invitation);
        InvitationDigests = tables.GetValueOrDefault(TenancyTable.InvitationDigest);
        History = TenancyModel.EventLogOf(model);
    }

    /// <summary>The schema of the context, where Tenancy's functions and trigger functions are made.</summary>
    public string Schema { get; }

    public IEntityType Tenants { get; }

    public IEntityType Organizations { get; }

    public IEntityType Units { get; }

    public IEntityType UnitPaths { get; }

    public IEntityType Seats { get; }

    public IEntityType Placements { get; }

    public IEntityType Grants { get; }

    public IEntityType Rights { get; }

    public IEntityType Roles { get; }

    public IEntityType AccessRevisions { get; }

    /// <summary>
    /// The invitations, where the context maps them (<c>AddTenancyInvitations</c>); <see langword="null"/> in an
    /// application without invitations. Mapped or not together with <see cref="InvitationDigests"/>, and no part
    /// of <see cref="All"/>.
    /// </summary>
    public IEntityType? Invitations { get; }

    /// <summary>The digests of the invitations' tokens, where the context maps invitations; <see langword="null"/> otherwise.</summary>
    public IEntityType? InvitationDigests { get; }

    /// <summary>
    /// Tenancy's access history, where the context maps one (<c>AddTenancyEventLogTable</c>), with whether its
    /// table lets a row go once it is old enough; <see langword="null"/> without one. It is the toolkit's table
    /// with Tenancy's column, and not one of <see cref="All"/>.
    /// </summary>
    public (IEntityType Log, bool LetsRowsGo)? History { get; }

    /// <summary>Tenancy's own tables, in the order a script secures them.</summary>
    public IReadOnlyList<IEntityType> All
        => [Tenants, Organizations, Units, UnitPaths, Seats, Placements, Grants, Rights, Roles, AccessRevisions];

    /// <summary>
    /// Tenancy's tables in <paramref name="model"/>, or <see langword="null"/> when it maps none of them as
    /// tables: a consumer's model, whose read model maps views over them.
    /// </summary>
    /// <exception cref="InvalidOperationException">The model maps some of Tenancy's tables, but not all of them.</exception>
    public static TenancyTables? Of(IModel model)
    {
        var found = new Dictionary<TenancyTable, IEntityType>();
        foreach (var entityType in model.GetEntityTypes())
        {
            if (TenancyModel.TableOf(entityType) is { } table)
            {
                found.TryAdd(table, entityType);
            }
        }

        if (found.Count == 0)
        {
            return null;
        }

        TenancyTable[] needed =
        [
            TenancyTable.Tenant, TenancyTable.Organization, TenancyTable.Unit, TenancyTable.UnitPath, TenancyTable.Seat,
            TenancyTable.Placement, TenancyTable.Grant, TenancyTable.Right, TenancyTable.Role, TenancyTable.AccessRevision,
        ];
        var missing = needed.Where(table => !found.ContainsKey(table)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "The model maps some of Tenancy's tables but not " + string.Join(", ", missing)
                + ", so no policies can be written for them. Map Tenancy's tables with modelBuilder.AddTenancy() in one context.");
        }

        // The two go together: the policies of each read the other.
        if (found.ContainsKey(TenancyTable.Invitation) != found.ContainsKey(TenancyTable.InvitationDigest))
        {
            throw new InvalidOperationException(
                "The model maps Tenancy's invitations without the digests of their tokens, or the digests without the invitations, so no policies can be written for them. "
                + "Map both with modelBuilder.AddTenancyInvitations<TInvitation, TInvitationId>().");
        }

        return new TenancyTables(model, found);
    }

    /// <summary>The table of <paramref name="entity"/>, with its schema: <c>"tenancy"."Seats"</c>.</summary>
    public static string Table(IEntityType entity) => RowAccessModel.Table(entity);

    /// <summary>The column of <paramref name="property"/>, quoted: <c>"TenantId"</c>.</summary>
    public static string Column(IEntityType entity, string property) => RowAccessModel.Column(entity, property);

    /// <summary>
    /// The name of the column of <paramref name="property"/>, as the database has it and without quotes: what a
    /// trigger is told a column is called, to read it from a row by name.
    /// </summary>
    /// <exception cref="InvalidOperationException">The entity has no such property, or it is stored in no column of the entity's table.</exception>
    public static string ColumnName(IEntityType entity, string property)
        => entity.FindProperty(property)?.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))
           ?? throw new InvalidOperationException($"{entity.DisplayName()} has no column for {property}.");

    /// <summary>
    /// The column of <paramref name="property"/> of the row a policy is on, named with its table, for a
    /// condition that reads other tables in a subquery: <c>"tenancy"."SeatRoleGrants"."RoleId"</c>. A column
    /// of the same name in the subquery's own tables, one an application added to its class, cannot take its place.
    /// </summary>
    public static string Own(IEntityType entity, string property) => Table(entity) + "." + Column(entity, property);

    /// <summary><paramref name="value"/> as the column of <paramref name="property"/> stores it: <c>'Active'</c>.</summary>
    public static string Stored(IEntityType entity, string property, object value) => RowAccessModel.Stored(entity, property, value);

    /// <summary>The store type of the column of <paramref name="property"/>, as a function returns it: <c>uuid</c>.</summary>
    public static string ColumnType(IEntityType entity, string property) => RowAccessModel.ColumnType(entity, property);

    /// <summary>
    /// A store type as a cast inside a function names it: the types Postgres's grammar spells itself, such as
    /// <c>bigint</c>, as they are, since those always mean Postgres's own; the others that are Postgres's own
    /// with <c>pg_catalog</c> in front, since with an empty search path a type name is still looked for in the
    /// session's temporary schema first. A type of an extension or of the application is left as it is.
    /// </summary>
    public static string Cast(string storeType)
        => storeType.Trim() switch
        {
            var own and ("uuid" or "text" or "int8" or "int4" or "int2" or "bool") => "pg_catalog." + own,
            var other => other,
        };
}
