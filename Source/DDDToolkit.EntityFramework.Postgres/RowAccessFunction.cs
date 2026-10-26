using System.Text;
using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// One <c>[AccessFunction&lt;TAggregate&gt;]</c> as its SQL function is written from it: its name, the aggregate
/// it is about, the SQL the generator translated its <c>Allows</c> method into, the parameters it takes after
/// the key, and whether it answers about one row or with the keys of every row it allows.
/// <para>
/// Like <see cref="RowAccessRule"/>, you rarely build one yourself: the Supabase build writes one per access
/// function it finds in the modules a host references. Build one by hand, from the class's generated
/// <c>Name</c>, <c>RowAccessSql</c>, <c>RowAccessParameters</c> and <c>RowAccessShape</c> constants, to hand to
/// <see cref="PostgresRowAccess.Script(Microsoft.EntityFrameworkCore.DbContext, IEnumerable{RowAccessRule}, IEnumerable{RowAccessFunction}?, RowAccessExport)"/>.
/// </para>
/// </summary>
/// <remarks>
/// A name is <c>schema.name</c>, which the function is created as; a logical <c>owner/name</c>; or a bare
/// name, relative to <see cref="Owner"/>. A function with a logical name is created in the schema of the
/// context that maps its aggregate, so a module's rules need not know what the host calls that schema, and
/// rules ask it by its logical name, <see cref="LogicalName"/>.
/// </remarks>
public sealed partial class RowAccessFunction
{
    private RowAccessFunction(string name, string logicalName, string? owner, string aggregateTypeName, string sql, string parameters, AccessFunctionShape shape)
    {
        Name = name;
        LogicalName = logicalName;
        Owner = owner;
        AggregateTypeName = aggregateTypeName;
        Sql = sql;
        Parameters = parameters;
        Shape = shape;
    }

    /// <summary>The function's name as given: <c>projects.is_member</c>, <c>projects/is_member</c> or <c>is_member</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// The name rules ask it by: <c>schema.name</c> for a name with its schema, and <c>owner/name</c> otherwise.
    /// </summary>
    public string LogicalName { get; }

    /// <summary>The owner a name without a schema is relative to, as a module's name is written in a file name; null for none.</summary>
    public string? Owner { get; }

    /// <summary>Whether the name is <c>schema.name</c>, which the function is created as, rather than a logical name.</summary>
    public bool IsQualified => !LogicalName.Contains('/', StringComparison.Ordinal);

    /// <summary>The aggregate root's CLR type name, as <see cref="Type.FullName"/> spells it.</summary>
    public string AggregateTypeName { get; }

    /// <summary>The SQL template the generator wrote, with the aggregate's entities in <c>{exists}</c> and its parameters as <c>{arg:n}</c>.</summary>
    public string Sql { get; }

    /// <summary>
    /// The SQL types of the parameters the function takes after the key, comma-separated, <c>text, integer</c>;
    /// empty for none.
    /// </summary>
    public string Parameters { get; }

    /// <summary>Whether the function answers about one row, given its key, or with the keys of every row it allows.</summary>
    public AccessFunctionShape Shape { get; }

    /// <summary>A function about <typeparamref name="TAggregate"/>, named <c>schema.name</c>.</summary>
    /// <exception cref="ArgumentException">The name is not <c>schema.name</c>, or the SQL is empty.</exception>
    public static RowAccessFunction For<TAggregate>(string name, string sql)
        => For(typeof(TAggregate).FullName!, name, sql);

    /// <summary>
    /// A function about <typeparamref name="TAggregate"/>: its name, relative to <paramref name="owner"/> when
    /// it has no schema, the SQL types of the parameters it takes after the key, and its shape.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The name is none of <c>schema.name</c>, <c>owner/name</c> or a name relative to an owner given, the SQL is
    /// empty, or a parameter is not an SQL type name.
    /// </exception>
    public static RowAccessFunction For<TAggregate>(string name, string sql, string? owner, string? parameters = null, AccessFunctionShape shape = AccessFunctionShape.Row)
        => For(typeof(TAggregate).FullName!, name, sql, owner, parameters, shape);

    /// <summary>A function about the aggregate named <paramref name="aggregateTypeName"/>, which is how generated code names it.</summary>
    /// <exception cref="ArgumentException">The name is not <c>schema.name</c> or <c>owner/name</c>, or the SQL is empty.</exception>
    public static RowAccessFunction For(string aggregateTypeName, string name, string sql)
        => For(aggregateTypeName, name, sql, owner: null);

    /// <summary>
    /// A function about the aggregate named <paramref name="aggregateTypeName"/>, which is how generated code
    /// names it: its name, relative to <paramref name="owner"/> when it has no schema, the SQL types of the
    /// parameters it takes after the key, and its shape.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The name is none of <c>schema.name</c>, <c>owner/name</c> or a name relative to an owner given, the SQL is
    /// empty, or a parameter is not an SQL type name.
    /// </exception>
    public static RowAccessFunction For(
        string aggregateTypeName,
        string name,
        string sql,
        string? owner,
        string? parameters = null,
        AccessFunctionShape shape = AccessFunctionShape.Row)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var normalizedOwner = string.IsNullOrWhiteSpace(owner) ? null : RowAccessNames.NormalizeOwner(owner);
        var logical = RowAccessNames.Logical(name, normalizedOwner)
            ?? throw new ArgumentException(
                $"'{name}' is not a function name: write it with its schema, 'projects.is_member', as a logical name, 'projects/is_member', or relative to an owner you give, 'is_member'. Each part is letters, digits and underscores.",
                nameof(name));

        if (shape is not (AccessFunctionShape.Row or AccessFunctionShape.Set))
        {
            throw new ArgumentException($"{shape} is not a shape of an access function.", nameof(shape));
        }

        return new(name, logical, logical.Contains('/', StringComparison.Ordinal) ? logical[..logical.IndexOf('/', StringComparison.Ordinal)] : normalizedOwner, aggregateTypeName, sql, TypesOf(parameters), shape);
    }

    /// <inheritdoc />
    public override string ToString() => $"{LogicalName} ({AggregateTypeName})";

    /// <summary>
    /// The parameters' SQL types, each checked, since they are written into the function's signature as they
    /// are: a type name, possibly with its schema, its length and <c>[]</c>.
    /// </summary>
    private static string TypesOf(string? parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return "";
        }

        var types = new StringBuilder();
        foreach (var part in parameters.Split(','))
        {
            var type = part.Trim();
            if (!SqlType().IsMatch(type))
            {
                throw new ArgumentException($"'{type}' in '{parameters}' is not an SQL type such as text, uuid or integer.", nameof(parameters));
            }

            types.Append(types.Length == 0 ? "" : ", ").Append(type);
        }

        return types.ToString();
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?( [A-Za-z_][A-Za-z0-9_]*)*(\([0-9]+(, ?[0-9]+)?\))?(\[\])*$", RegexOptions.CultureInvariant)]
    internal static partial Regex SqlType();
}
