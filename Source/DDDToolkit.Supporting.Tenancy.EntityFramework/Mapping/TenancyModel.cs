using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// What a model built with <c>AddTenancy</c>, <c>AddTenancyReadModel</c>, <c>AddTenancyReadFunctions</c> and
/// <c>ScopeToTenant</c> says about tenancy, for a package that works from the model, such as one that writes
/// database policies: which entity types are Tenancy's own tables, which property holds the tenant of an entity
/// type kept to one, whether the model reads Tenancy through its functions, whether a module's model reads
/// more of Tenancy's than the access facts of its read model, which entity types keep who changed their rows,
/// and where Tenancy's access history is.
/// </summary>
public static class TenancyModel
{
    /// <summary>
    /// The annotation <c>AddTenancyReadFunctions</c> puts on a model: its read model is read from Tenancy's
    /// functions, and names no table of Tenancy's. <see cref="ReadsThroughFunctions"/> answers it.
    /// </summary>
    public const string ReadFunctionsAnnotation = "DDDToolkit:Tenancy:ReadFunctions";

    /// <summary>The package's generic classes, by the table they are mapped to.</summary>
    private static readonly Dictionary<Type, TenancyTable> Tables = new()
    {
        [typeof(TenantAggregate<>)] = TenancyTable.Tenant,
        [typeof(OrganizationAggregate<,,>)] = TenancyTable.Organization,
        [typeof(OrganizationUnitEntity<>)] = TenancyTable.Unit,
        [typeof(OrganizationUnitPath<,>)] = TenancyTable.UnitPath,
        [typeof(SeatAggregate<,,,>)] = TenancyTable.Seat,
        [typeof(Placement<,,>)] = TenancyTable.Placement,
        [typeof(RoleGrant<,>)] = TenancyTable.Grant,
        [typeof(SeatRight<,,,>)] = TenancyTable.Right,
        [typeof(RoleAggregate<,>)] = TenancyTable.Role,
        [typeof(TenancyAccessRevision<>)] = TenancyTable.AccessRevision,
        [typeof(InvitationAggregate<,,,,>)] = TenancyTable.Invitation,
        [typeof(TenancyInvitationDigest<,>)] = TenancyTable.InvitationDigest,
    };

    /// <summary>
    /// Whether <paramref name="model"/> reads Tenancy through its functions: its read model was mapped with
    /// <c>AddTenancyReadFunctions</c>, so each row the access questions read comes from the function of
    /// <see cref="TenancyFunctionNames"/> that answers it, and the model maps no table or view of Tenancy's.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public static bool ReadsThroughFunctions(IReadOnlyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.FindAnnotation(ReadFunctionsAnnotation)?.Value is true;
    }

    /// <summary>
    /// What a module's model reads of Tenancy's beyond the access facts, as one sentence per finding; empty when it
    /// reads nothing more. Two things are found: an entity type that is not one of this package's rows and is mapped
    /// onto a table or view of Tenancy's (<paramref name="tables"/> in <paramref name="schema"/>) or onto one of its
    /// functions (<see cref="TenancyFunctionNames"/>); and a property on one of this package's rows that the package
    /// did not give it, a shadow property included. A model that maps Tenancy's own aggregates is Tenancy's and is
    /// not judged. SQL written by hand is not a mapping and is not seen.
    /// <para>
    /// The rows of the read model carry what an access rule reads and no text that is shown to people, so a module
    /// whose model passes cannot show what a seat, a unit or a role is called from its own queries: it answers
    /// ids, and the names are the directory's. Assert it in a test over every module's model, built for each
    /// database the application runs on:
    /// </para>
    /// <code>
    /// TenancyModel.ReadsBeyondAccessFacts(projectsContext.Model).Should().BeEmpty();
    /// </code>
    /// <para>
    /// It judges the shape of a mapping, and is no lock on the database: what a caller may read there is the
    /// policies' to decide. Names are compared ignoring case, as some databases do. A database without schemas,
    /// SQLite among them, finds a table by its name alone, so a type mapped there under one of Tenancy's names
    /// reads Tenancy's table whichever schema its mapping names, and a check that is given Tenancy's schema does
    /// not find it. For the model built for such a database, ask once more for each schema the model names;
    /// <see langword="null"/> asks under the model's default schema, which is where a type mapped with no schema is.
    /// </para>
    /// </summary>
    /// <param name="model">The module's model.</param>
    /// <param name="schema">The schema Tenancy's tables and functions are in, or <see langword="null"/> for this model's default schema.</param>
    /// <param name="tables">The tables' names, the same as Tenancy's context was given; <see cref="TenancyTableNames.Default"/> when not given.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public static IReadOnlyList<string> ReadsBeyondAccessFacts(IReadOnlyModel model, string? schema = "tenancy", TenancyTableNames? tables = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        return TenancyModelFacts.BeyondAccessFacts(model, schema, tables ?? TenancyTableNames.Default);
    }

    /// <summary>
    /// Which of Tenancy's tables <paramref name="entityType"/> is: one of the package's classes, or the
    /// application's class declared with its template, mapped to a table. <see langword="null"/> for any other
    /// entity type, an entity of the application's own on a Tenancy class included, and for the rows a
    /// consumer's read model maps as views over Tenancy's tables or reads from its functions.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="entityType"/> is null.</exception>
    public static TenancyTable? TableOf(IEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return TableOf((IReadOnlyEntityType)entityType);
    }

    /// <summary><see cref="TableOf(IEntityType)"/> for a model of any kind, one still being built included.</summary>
    internal static TenancyTable? TableOf(IReadOnlyEntityType entityType)
    {
        if (entityType.GetViewName() is not null || entityType.GetTableName() is null)
        {
            return null;
        }

        for (var type = entityType.ClrType; type is not null && type != typeof(object); type = type.BaseType)
        {
            if (!type.IsGenericType)
            {
                continue;
            }

            var definition = type.GetGenericTypeDefinition();
            if (Tables.TryGetValue(definition, out var table))
            {
                return table;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="entityType"/> keeps who wrote its row first and who changed it last, in the six
    /// columns of <see cref="TenancyAttribution"/>: <c>RecordsWhoChanged</c> was called on it, or on a type it
    /// derives from.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="entityType"/> is null.</exception>
    public static bool RecordsWhoChanged(IReadOnlyEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        for (var type = entityType; type is not null; type = type.BaseType)
        {
            if (type.FindAnnotation(TenancyAttribution.Annotation)?.Value is true)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tenancy's access history in <paramref name="model"/>, the log <c>AddTenancyEventLogTable</c> maps, with
    /// whether its table lets a row go once it is old enough; <see langword="null"/> for a model without one.
    /// The row's tenant is its property <see cref="TenancyEventLogTable.TenantId"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public static (IEntityType Log, bool LetsRowsGo)? EventLogOf(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.FindAnnotation(TenancyEventLogTable.Annotation)?.Value is bool letsRowsGo)
            {
                return (entityType, letsRowsGo);
            }
        }

        return null;
    }

    /// <summary>
    /// The property that holds the tenant of <paramref name="entityType"/>, as <c>ScopeToTenant</c> or
    /// <c>AddTenancy</c> marked it: the property the tenant filter reads and the save check checks. For a type
    /// derived in a hierarchy, its root's. <see langword="null"/> for an entity type not kept to a tenant, such as
    /// an owned type, which is kept through its owner.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="entityType"/> is null.</exception>
    public static IProperty? TenantPropertyOf(IEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return entityType.GetRootType().FindAnnotation(TenancyMapping.TenantPropertyAnnotation)?.Value is string name
            ? entityType.FindProperty(name)
            : null;
    }
}
