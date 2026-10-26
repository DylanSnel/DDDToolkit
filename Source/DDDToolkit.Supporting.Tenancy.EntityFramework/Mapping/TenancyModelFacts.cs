using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// What the save check, the wiring check and the access questions need to know of a model, worked out once per
/// model: whether any entity type is kept to a tenant, whether every one of them still has the tenant filter,
/// and whether a module's model reads Tenancy's tables through views. A model is built once per context type
/// and then shared, so asking again per save or per question would repeat the same walk.
/// <para>
/// It also holds the walk behind <see cref="TenancyModel.ReadsBeyondAccessFacts"/>, kept per model and per
/// address of Tenancy's tables it was asked with.
/// </para>
/// </summary>
internal sealed class TenancyModelFacts
{
    private static readonly ConditionalWeakTable<IModel, TenancyModelFacts> Cache = new();

    /// <summary>The rows of the read model, which a consumer's model maps to views or to Tenancy's functions.</summary>
    private static readonly HashSet<Type> ReadRows =
    [
        typeof(SeatRight<,,,>), typeof(OrganizationUnitPath<,>), typeof(OrganizationUnitRow<,>),
        typeof(RoleRow<,>), typeof(PlacementRow<,>), typeof(SeatRow<,>),
    ];

    /// <summary>
    /// The rows Tenancy's database functions answer, which the store reads from SQL that asks the function: ids,
    /// keys and dates, held to their own properties as the read rows are.
    /// </summary>
    private static readonly HashSet<Type> FunctionRows =
    [
        typeof(SeatHolderRow<>), typeof(TenantAdministratorRow<,>), typeof(TenantToSweepRow<>), typeof(MoveReach<>),
        typeof(InvitationOfDigestRow<,,>),
    ];

    /// <summary>Every name of <see cref="TenancyFunctionNames"/>.</summary>
    private static readonly HashSet<string> FunctionNames = new(
        typeof(TenancyFunctionNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!),
        StringComparer.OrdinalIgnoreCase);

    private static readonly ConditionalWeakTable<IReadOnlyModel, ConcurrentDictionary<(string? Schema, TenancyTableNames Tables), IReadOnlyList<string>>> Beyond = new();

    private readonly string? _unfiltered;

    private readonly string? _attributedButUnscoped;

    private readonly string? _attributedBelowTheRoot;

    private TenancyModelFacts(IModel model)
    {
        var unfiltered = new List<string>();
        var unscoped = new List<string>();
        var belowTheRoot = new List<string>();
        var (ownTables, views) = (false, false);
        foreach (var entityType in model.GetEntityTypes())
        {
            ownTables |= TenancyModel.TableOf(entityType) is not null;
            KeepsAccessHistory |= entityType.FindAnnotation(TenancyEventLogTable.Annotation) is not null;
            views |= entityType.GetViewName() is not null && entityType.ClrType.IsGenericType && ReadRows.Contains(entityType.ClrType.GetGenericTypeDefinition());

            if (entityType.FindAnnotation(TenancyAttribution.Annotation) is not null)
            {
                RecordsWhoChanged = true;
                if (entityType.GetRootType().FindAnnotation(TenancyMapping.TenantPropertyAnnotation) is null)
                {
                    unscoped.Add(entityType.DisplayName());
                }
                else if (entityType.BaseType is { } parent && !TenancyModel.RecordsWhoChanged(parent) && SharesATable(entityType, parent))
                {
                    belowTheRoot.Add(entityType.DisplayName());
                }
            }

            if (entityType.FindAnnotation(TenancyMapping.TenantPropertyAnnotation) is null)
            {
                continue;
            }

            HasScopedTypes = true;
            if (entityType.FindDeclaredQueryFilter(TenancyQueryFilter.Name) is null)
            {
                unfiltered.Add(entityType.DisplayName());
            }
        }

        _unfiltered = unfiltered.Count == 0 ? null : string.Join(", ", unfiltered.Order(StringComparer.Ordinal));
        _attributedButUnscoped = unscoped.Count == 0 ? null : string.Join(", ", unscoped.Order(StringComparer.Ordinal));
        _attributedBelowTheRoot = belowTheRoot.Count == 0 ? null : string.Join(", ", belowTheRoot.Order(StringComparer.Ordinal));
        ReadsTenancysTablesThroughViews = views && !ownTables && !TenancyModel.ReadsThroughFunctions(model);
    }

    /// <summary>Whether any entity type of the model is kept to a tenant: Tenancy's own, its read model, or one of <c>ScopeToTenant</c>.</summary>
    public bool HasScopedTypes { get; }

    /// <summary>Whether any entity type of the model keeps who changed its rows: <c>RecordsWhoChanged</c> was called on it.</summary>
    public bool RecordsWhoChanged { get; }

    /// <summary>Whether the model maps Tenancy's access history: <c>AddTenancyEventLogTable</c> was called on it.</summary>
    public bool KeepsAccessHistory { get; }

    /// <summary>
    /// Whether the model is a module's, mapping none of Tenancy's tables itself, whose read model is views over
    /// them (<c>AddTenancyReadModel</c>) rather than Tenancy's functions (<c>AddTenancyReadFunctions</c>).
    /// </summary>
    public bool ReadsTenancysTablesThroughViews { get; }

    /// <summary>The facts of <paramref name="model"/>.</summary>
    public static TenancyModelFacts Of(IModel model) => Cache.GetValue(model, static built => new TenancyModelFacts(built));

    /// <summary>
    /// What <see cref="TenancyModel.ReadsBeyondAccessFacts"/> answers, worked out once per model and per address
    /// of Tenancy's tables.
    /// </summary>
    public static IReadOnlyList<string> BeyondAccessFacts(IReadOnlyModel model, string? schema, TenancyTableNames tables)
        => Beyond.GetValue(model, static _ => new())
            .GetOrAdd((schema, tables), static (address, built) => WalkBeyondAccessFacts(built, address.Schema, address.Tables), model);

    private static IReadOnlyList<string> WalkBeyondAccessFacts(IReadOnlyModel model, string? schema, TenancyTableNames tables)
    {
        var entityTypes = model.GetEntityTypes().ToArray();

        // Whose model it is. The rights and the closure are tables in Tenancy's own model and rows of the read
        // model everywhere else, so a module that mapped one of them onto the table itself does not pass for
        // Tenancy by it; every other table of Tenancy's is one of its aggregates, which only its own model maps.
        if (entityTypes.Any(entityType => TenancyModel.TableOf(entityType) is { } table and not (TenancyTable.Right or TenancyTable.UnitPath)))
        {
            return [];
        }

        var tenancys = schema ?? model.GetDefaultSchema();
        var names = new HashSet<string>(
            [
                tables.Tenants, tables.Organizations, tables.Units, tables.UnitPaths, tables.Seats, tables.Placements,
                tables.Grants, tables.Rights, tables.Roles, tables.AccessRevisions, tables.Invitations, tables.InvitationDigests,
            ],
            StringComparer.OrdinalIgnoreCase);

        var findings = new List<string>();
        foreach (var entityType in entityTypes)
        {
            var row = RowDefinitionOf(entityType.ClrType);
            if (row is null)
            {
                // A type of the module's own: it reads nothing of Tenancy's, whichever way it is mapped.
                foreach (var (kind, name, inSchema) in StoreObjectsOf(model, entityType))
                {
                    var isTenancys = string.Equals(inSchema, tenancys, StringComparison.OrdinalIgnoreCase)
                                     && (kind == "function" ? FunctionNames.Contains(name) : names.Contains(name));
                    if (isTenancys)
                    {
                        findings.Add(
                            $"{entityType.DisplayName()} is mapped onto the {kind} {Address(inSchema, name)}, which is Tenancy's. A module reads Tenancy through the rows of "
                            + "its read model alone (AddTenancyReadModel or AddTenancyReadFunctions), and asks the directory for names, by id.");
                    }
                }

                continue;
            }

            // One of the package's rows: exactly the properties its class has, and for a placement the tenant it
            // is kept to its tenant by. Anything more is a column the module reads past the read model.
            var allowed = new HashSet<string>(
                entityType.ClrType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name),
                StringComparer.Ordinal);
            if (row == typeof(PlacementRow<,>))
            {
                allowed.Add(TenancyMapping.TenantColumn);
            }

            foreach (var property in entityType.GetProperties().Where(property => !allowed.Contains(property.Name)))
            {
                findings.Add(
                    $"{entityType.DisplayName()} has the property {property.Name}, which is no part of Tenancy's read model. The rows carry access facts only: "
                    + "ids, keys, periods and statuses, and never a name. Ask the directory for names, by id.");
            }

            foreach (var navigation in entityType.GetNavigations().Cast<IReadOnlyNavigationBase>().Concat(entityType.GetSkipNavigations()))
            {
                findings.Add(
                    $"{entityType.DisplayName()} has the navigation {navigation.Name}, which is no part of Tenancy's read model. The rows are read on their own, or joined "
                    + "by id in the module's own query.");
            }
        }

        findings.Sort(StringComparer.Ordinal);
        return findings;
    }

    /// <summary>The generic definition of the package's row that <paramref name="type"/> is, or <see langword="null"/> for any other type.</summary>
    private static Type? RowDefinitionOf(Type type)
    {
        if (!type.IsGenericType)
        {
            return null;
        }

        var definition = type.GetGenericTypeDefinition();
        return ReadRows.Contains(definition) || FunctionRows.Contains(definition) ? definition : null;
    }

    /// <summary>
    /// Every table, view and function <paramref name="entityType"/> is mapped onto, with the schema each is in:
    /// its own, the ones it is split over, and a function by its name in the database rather than in the model.
    /// </summary>
    private static IEnumerable<(string Kind, string Name, string? Schema)> StoreObjectsOf(IReadOnlyModel model, IReadOnlyEntityType entityType)
    {
        if (entityType.GetTableName() is { } table)
        {
            yield return ("table", table, entityType.GetSchema() ?? model.GetDefaultSchema());
        }

        if (entityType.GetViewName() is { } view)
        {
            yield return ("view", view, entityType.GetViewSchema() ?? model.GetDefaultSchema());
        }

        if (entityType.GetFunctionName() is { } function)
        {
            // The entity type holds the function's name in the model; the model's function says what it is
            // called in the database, and in which schema.
            var mapped = model.FindDbFunction(function);
            yield return ("function", mapped?.Name ?? function, mapped?.Schema ?? model.GetDefaultSchema());
        }

        foreach (var fragment in entityType.GetMappingFragments())
        {
            var part = fragment.StoreObject;
            yield return (part.StoreObjectType == StoreObjectType.View ? "view" : "table", part.Name, part.Schema ?? model.GetDefaultSchema());
        }
    }

    private static string Address(string? schema, string name) => schema is null ? name : schema + "." + name;

    /// <summary>
    /// Throws when an entity type keeps who changed its rows and is not kept to a tenant. Who changed a row is
    /// the Tenancy caller of the save, and only a save of a tenant's row is sure to have one: on any other row the
    /// columns would be left blank, in silence.
    /// <para>
    /// Throws as well when such a type is stored in the table of the type it derives from, which does not keep
    /// it. The other rows of that table would keep nothing, and a database that holds callers to the columns, as
    /// Postgres does with Tenancy's trigger, would refuse every one of them.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// An entity type calls <c>RecordsWhoChanged</c> without <c>ScopeToTenant</c>, or calls it below the root of a
    /// hierarchy stored in one table.
    /// </exception>
    public void RequireAttributedTypesAreScoped()
    {
        if (_attributedButUnscoped is not null)
        {
            throw new InvalidOperationException(
                "These entity types keep who changed their rows, with RecordsWhoChanged, but are not kept to a tenant: " + _attributedButUnscoped
                + ". Who changed a row is the Tenancy caller of the save, which only a tenant's row is sure to have: call ScopeToTenant on the entity as well.");
        }

        if (_attributedBelowTheRoot is not null)
        {
            throw new InvalidOperationException(
                "These entity types keep who changed their rows, with RecordsWhoChanged, and are stored in the table of the type they derive from, which does not: " + _attributedBelowTheRoot
                + ". The other rows of that table would keep nothing: call RecordsWhoChanged on the type at the root of the hierarchy instead.");
        }
    }

    /// <summary>Whether two entity types of one hierarchy are stored in the same table.</summary>
    private static bool SharesATable(IEntityType entityType, IEntityType parent)
        => entityType.GetTableName() is { } table
           && string.Equals(table, parent.GetTableName(), StringComparison.Ordinal)
           && string.Equals(entityType.GetSchema(), parent.GetSchema(), StringComparison.Ordinal);

    /// <summary>
    /// Throws when an entity type kept to a tenant has lost the tenant filter, because its reads would then
    /// see every tenant's rows while its writes still passed the check.
    /// </summary>
    /// <exception cref="InvalidOperationException">An entity type kept to a tenant has no filter named <see cref="TenancyQueryFilter.Name"/>.</exception>
    public void RequireTenantFilters()
    {
        if (_unfiltered is not null)
        {
            throw new InvalidOperationException(
                "These entity types are kept to a tenant but have no query filter named '" + TenancyQueryFilter.Name + "': " + _unfiltered
                + ". Something in OnModelCreating removed or replaced it after ScopeToTenant or AddTenancy; give an application filter a name of its own.");
        }
    }
}
