using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// Writing SQL from an Entity Framework model, the way <see cref="PostgresRowAccess"/> writes the policies of
/// the rules: for an <see cref="IRowAccessContribution"/>, whose functions and policies name the tables and
/// columns the application's model gives the package's entities.
/// <code>
/// var entries = context.Model.FindEntityType(typeof(AuditEntry))!;
/// var sql = $"SELECT {RowAccessModel.Column(entries, "Id")} FROM {RowAccessModel.Table(entries)} " +
///           $"WHERE {RowAccessModel.Column(entries, "Kind")} = {RowAccessModel.Stored(entries, "Kind", AuditKind.Change)}";
/// </code>
/// </summary>
/// <remarks>
/// Every name is quoted, so it means the table or the column exactly as the model spells it. A table the
/// model gives no schema is in <see cref="PostgresRowAccess.DefaultSchema"/>.
/// </remarks>
public static class RowAccessModel
{
    /// <summary>The table <paramref name="entity"/> is mapped to, with its schema: <c>"desk"."Tickets"</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="entity"/> is not mapped to a table.</exception>
    public static string Table(IEntityType entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var table = TableOf(entity);
        return Quote(table.Schema ?? PostgresRowAccess.DefaultSchema) + "." + Quote(table.Name);
    }

    /// <summary>
    /// The column <paramref name="property"/> of <paramref name="entity"/> is stored in: <c>"TenantId"</c>, or
    /// the column of a value object's property stored in the same table, <c>Period.EndsAt</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> or <paramref name="property"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The model maps no such property to a column of the entity's table.</exception>
    public static string Column(IEntityType entity, string property)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);

        var (found, table) = PropertyOf(entity, property);
        return Quote(found.GetColumnName(table)!);
    }

    /// <summary>The store type of the column <paramref name="property"/> is stored in, as a function's signature names it: <c>uuid</c>, <c>text[]</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> or <paramref name="property"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The model maps no such property to a column of the entity's table.</exception>
    public static string ColumnType(IEntityType entity, string property)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);

        var (found, table) = PropertyOf(entity, property);
        return found.GetColumnType(table);
    }

    /// <summary>
    /// <paramref name="value"/> as the column of <paramref name="property"/> stores it, as an SQL literal: an
    /// enum through the property's converter, <c>'Active'</c> or <c>1</c>, and anything else as the property's
    /// type mapping writes it.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> or <paramref name="property"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The model maps no such property to a column of the entity's table.</exception>
    public static string Stored(IEntityType entity, string property, object? value)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);

        var (found, _) = PropertyOf(entity, property);
        return found.GetRelationalTypeMapping().GenerateSqlLiteral(value);
    }

    /// <summary>
    /// Every table of <paramref name="root"/>'s entities that is not the root's own, with the way up to the
    /// root: a line's table to its order's, and a line's parts to the line and on to the order. The same
    /// chains the policies of an aggregate's entities are written from.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="root"/> is not mapped to a table.</exception>
    public static IReadOnlyList<EntityTableChain> EntityTablesOf(IEntityType root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return PostgresRowAccess.EntityTableChainsOf(root);
    }

    /// <summary><paramref name="text"/> as an SQL string literal: <c>'it''s'</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static string Literal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    /// <summary>An identifier in double quotes, a quote in it doubled.</summary>
    internal static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    internal static StoreObjectIdentifier TableOf(IEntityType entity)
        => entity.GetTableName() is { } name
            ? StoreObjectIdentifier.Table(name, entity.GetSchema())
            : throw new InvalidOperationException($"{entity.DisplayName()} is not mapped to a table, so no SQL can name one for it.");

    /// <summary>The property at <paramref name="path"/> of <paramref name="entity"/>, through value objects stored in its table, and that table.</summary>
    private static (IProperty Property, StoreObjectIdentifier Table) PropertyOf(IEntityType entity, string path)
    {
        var table = TableOf(entity);
        ITypeBase type = entity;
        var names = path.Split('.');

        for (var i = 0; i < names.Length - 1; i++)
        {
            if (type.FindComplexProperty(names[i]) is { } complex)
            {
                type = complex.ComplexType;
                continue;
            }

            if (type is IEntityType owner
                && owner.FindNavigation(names[i]) is { ForeignKey.IsOwnership: true } owned
                && owned.TargetEntityType.GetTableName() == table.Name
                && owned.TargetEntityType.GetSchema() == table.Schema)
            {
                type = owned.TargetEntityType;
                continue;
            }

            throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, "{0}.{1}: {2} is not a value object stored in {0}'s own table.", entity.DisplayName(), path, names[i]));
        }

        var property = type.FindProperty(names[^1]);
        if (property?.GetColumnName(table) is null)
        {
            throw new InvalidOperationException($"The Entity Framework model maps {entity.DisplayName()}.{path} to no column of {table.DisplayName()}.");
        }

        return (property, table);
    }
}
