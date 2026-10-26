using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// The rows the access questions read, from one context: Tenancy's own, or a module's that maps the read model
/// with <c>AddTenancyReadModel</c> or <c>AddTenancyReadFunctions</c>. The questions built over it are queries of
/// that context, so a module that composes one into its own query gets one statement. Every set carries the
/// tenant filter, and none is tracked.
/// <para>
/// Where the database keeps the rights and shows a seat only its own (<see cref="TenancyStoreOptions.DatabaseKeepsRights"/>),
/// who holds a key at a unit is asked of the database's function of that name
/// (<see cref="TenancyFunctionNames.SeatsHoldingAt"/>) rather than worked out over the rows, which would find
/// nobody but the calling seat. That, too, is a query of the context.
/// </para>
/// </summary>
public sealed class EfTenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>
    : ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private readonly DbContext _context;
    private bool? _databaseKeepsRights;

    /// <summary>The rows of <paramref name="context"/>.</summary>
    /// <param name="context">
    /// The context to read from. Whether the database keeps the rights is read from the services the context was
    /// made by, the application's <see cref="TenancyStoreOptions"/>; a context made by hand has none, and is read
    /// as one whose database does not.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The database keeps the rights, and <paramref name="context"/> is a module's that maps the read model as views
    /// over Tenancy's tables: there a module asks Tenancy's functions, mapped with <c>AddTenancyReadFunctions</c>.
    /// </exception>
    public EfTenancyReadSource(DbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // What the model maps is worked out once per model; the option is looked up only for a model that could
        // be refused, so a context mapped as it should be pays for neither.
        if (TenancyModelFacts.Of(context.Model).ReadsTenancysTablesThroughViews && DatabaseKeepsRights)
        {
            throw new InvalidOperationException(
                "'" + context.GetType().Name + "' maps Tenancy's read model as views over Tenancy's tables, with AddTenancyReadModel, and the database keeps the rights "
                + "(TenancyStoreOptions.DatabaseKeepsRights, which AddTenancyPostgres() turns on): there, no module reads Tenancy's tables. A seat reads only its own rights, "
                + "and a module asks Tenancy through its functions. Map the read model with modelBuilder.AddTenancyReadFunctions<TTenantId, TSeatId, TUnitId, TRoleId>(schema) "
                + "in OnModelCreating where the context runs on such a database, as on Postgres, and keep AddTenancyReadModel for one that does not, such as SQLite.");
        }
    }

    /// <summary>The rows of <paramref name="context"/>, Tenancy's own, for a store that was given the options itself.</summary>
    internal EfTenancyReadSource(DbContext context, bool databaseKeepsRights)
    {
        _context = context;
        _databaseKeepsRights = databaseKeepsRights && context.Database.IsRelational();
    }

    /// <inheritdoc />
    public IQueryable<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>> SeatRights => Rows<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>>();

    /// <inheritdoc />
    public IQueryable<OrganizationUnitPath<TTenantId, TUnitId>> UnitPaths => Rows<OrganizationUnitPath<TTenantId, TUnitId>>();

    /// <inheritdoc />
    public IQueryable<OrganizationUnitRow<TTenantId, TUnitId>> Units => Rows<OrganizationUnitRow<TTenantId, TUnitId>>();

    /// <inheritdoc />
    public IQueryable<RoleRow<TTenantId, TRoleId>> Roles => Rows<RoleRow<TTenantId, TRoleId>>();

    /// <inheritdoc />
    public IQueryable<PlacementRow<TSeatId, TUnitId>> Placements => Rows<PlacementRow<TSeatId, TUnitId>>();

    /// <inheritdoc />
    public IQueryable<SeatRow<TTenantId, TSeatId>> Seats => Rows<SeatRow<TTenantId, TSeatId>>();

    /// <inheritdoc />
    /// <remarks>
    /// Answered by the database's function where it keeps the rights and the context's model maps the function's
    /// row, as <c>AddTenancy</c>, <c>AddTenancyReadModel</c> and <c>AddTenancyReadFunctions</c> do;
    /// <see langword="null"/> anywhere else.
    /// </remarks>
    public IQueryable<TSeatId>? SeatsHoldingAt(string key, TUnitId unit)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!DatabaseKeepsRights || _context.Model.FindEntityType(typeof(SeatHolderRow<TSeatId>)) is not { } holders)
        {
            return null;
        }

        var sql = TenancyFunctionSql.Select(
            _context,
            holders.FindAnnotation(TenancyMapping.FunctionSchemaAnnotation)?.Value as string,
            TenancyFunctionNames.SeatsHoldingAt,
            parameters: 2,
            nameof(SeatHolderRow<TSeatId>.SeatId));

        return _context.Set<SeatHolderRow<TSeatId>>()
            .FromSqlRaw(sql, key, TenancyFunctionSql.Stored(_context, unit))
            .Select(holder => holder.SeatId);
    }

    /// <summary>
    /// Whether the database of the context keeps the rights: the application's <see cref="TenancyStoreOptions"/>,
    /// on a relational database. Asked once per source.
    /// </summary>
    private bool DatabaseKeepsRights
        => _databaseKeepsRights ??= _context.Database.IsRelational()
                                    && _context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider
                                        ?.GetService<IOptions<TenancyStoreOptions>>()?.Value.DatabaseKeepsRights == true;

    /// <summary>
    /// The set, untracked. The questions only read, and in Tenancy's own context the rights and the paths are
    /// tables its save writes: a row a question tracked would sit in the change tracker next to the ones the
    /// save loads to change.
    /// </summary>
    private IQueryable<T> Rows<T>()
        where T : class
        => _context.Set<T>().AsNoTracking();
}
