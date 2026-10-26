using System.Text.RegularExpressions;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure;

/// <summary>
/// How an application names what Tenancy keeps in its database, and how it stores a status. Tenancy's SQL names no
/// table, column or stored value of its own: it reads each from the model, and only a run under another naming
/// proves that it does. So the tests that depend on it run under both of these:
/// <list type="bullet">
/// <item><see cref="Default"/>: as Entity Framework names things, after the classes and their properties, with a
/// status stored as the name of its enum member.</item>
/// <item><see cref="SnakeCase"/>: the context uses <c>UseSnakeCaseNamingConvention()</c>, Tenancy's tables are
/// named with <see cref="TenancyTableNames.SnakeCase"/>, and every enum is stored as snake_case text.</item>
/// </list>
/// The convention is EFCore.NamingConventions', whose oldest Entity Framework is one patch newer than the oldest
/// the toolkit's packages run on. A run against those oldest versions cannot restore it, and there the same names
/// are given by hand (<see cref="SnakeCaseByHand"/>), which is the other way an application names its database.
/// </summary>
/// <remarks>
/// A test writes its own SQL once, in the default names, and <see cref="Sql"/> turns it into the names of the
/// database it runs on. Nothing there is guessed from a spelling: each name is looked up in the default model and
/// replaced by what this naming's model calls the same table, column, key or index, and each text that is how the
/// default model stores an enum value by how this one stores it. A statement that names none of the model's tables
/// is sent as it was written, since it asks Tenancy's functions, which answer under the same names and with the
/// same values whatever the tables are called.
/// </remarks>
public sealed partial class TenancyNaming
{
    private readonly Func<DbContextOptionsBuilder, DbContextOptionsBuilder> _configure;
    private readonly Lazy<Translation> _translation;

    private TenancyNaming(string name, string databasePrefix, TenancyTableNames tables, Func<DbContextOptionsBuilder, DbContextOptionsBuilder> configure)
    {
        Name = name;
        DatabasePrefix = databasePrefix;
        Tables = tables;
        _configure = configure;
        _translation = new Lazy<Translation>(() => Translation.Between(Default, this));
    }

    /// <summary>As Entity Framework names tables and columns, with an enum stored as the name of its member.</summary>
    public static TenancyNaming Default { get; } = new("the default names", "tenancy", TenancyTableNames.Default, static options => options);

    /// <summary>
    /// The same names without a naming convention: Tenancy's tables named in <c>OnModelCreating</c>, everything
    /// else named in a loop at its end, and the enums stored in snake_case (<see cref="SnakeCaseByHandModel"/>).
    /// </summary>
    public static TenancyNaming SnakeCaseByHand { get; } = new(
        "snake_case, named by hand",
        "tenancy_snake_by_hand",
        TenancyTableNames.SnakeCase,
        static options => options.ReplaceService<IModelCustomizer, SnakeCaseByHandModel>());

#if NAMING_CONVENTION
    /// <summary>
    /// What a host that keeps its database in snake_case does: the naming convention on every context, Tenancy's
    /// tables named in <c>OnModelCreating</c>, and its enums stored in snake_case (<see cref="SnakeCaseModel"/>).
    /// </summary>
    public static TenancyNaming SnakeCase { get; } = new(
        "snake_case",
        "tenancy_snake",
        TenancyTableNames.SnakeCase,
        static options => options.UseSnakeCaseNamingConvention().ReplaceService<IModelCustomizer, SnakeCaseModel>());
#else
    /// <summary>
    /// What a host that keeps its database in snake_case does. This build has no naming convention to put on the
    /// contexts, so it is <see cref="SnakeCaseByHand"/>.
    /// </summary>
    public static TenancyNaming SnakeCase => SnakeCaseByHand;
#endif

    /// <summary>What a failing test calls this naming.</summary>
    public string Name { get; }

    /// <summary>What the names of this naming's template databases begin with.</summary>
    public string DatabasePrefix { get; }

    /// <summary>What Tenancy's tables are called: what a context of a test's own passes to <c>AddTenancy</c> or <c>AddTenancyReadModel</c>.</summary>
    public TenancyTableNames Tables { get; }

    /// <summary>The naming the options of <paramref name="context"/> carry, for a context of a test's own to name Tenancy's tables by.</summary>
    public static TenancyNaming For(DbContext context)
        => context.GetService<IModelCustomizer>() switch
        {
            SnakeCaseByHandModel => SnakeCaseByHand,
            SnakeCaseModel => SnakeCase,
            _ => Default,
        };

    /// <summary>
    /// Adds this naming to a context's options. It is part of the options and of the model built from them, which
    /// are made once and shared by every instance of the context, one taken from a pool included.
    /// </summary>
    public DbContextOptionsBuilder Configure(DbContextOptionsBuilder options) => _configure(options);

    /// <summary>Tenancy's context on Npgsql under this naming, for its model: it never connects.</summary>
    public TestTenancyContext TenancyModel()
        => new(((DbContextOptionsBuilder<TestTenancyContext>)Configure(new DbContextOptionsBuilder<TestTenancyContext>().UseNpgsql("Host=model-only"))).Options);

    /// <summary>The widgets' context on Npgsql under this naming, for its model: it never connects.</summary>
    public WidgetContext WidgetModel()
        => new(((DbContextOptionsBuilder<WidgetContext>)Configure(new DbContextOptionsBuilder<WidgetContext>().UseNpgsql("Host=model-only"))).Options);

    /// <summary>
    /// <paramref name="written"/>, a statement in the default names, as it reads under this naming: every quoted
    /// name that is a table, a column, a key or an index of the default model, and every text that is a stored enum
    /// value there, replaced by this naming's. A statement that names no table of the model is returned as it is.
    /// </summary>
    public string Sql(string written) => ReferenceEquals(this, Default) ? written : _translation.Value.Sql(written);

    /// <summary>
    /// What this naming calls the table, column, key or index the default model calls <paramref name="name"/>,
    /// unquoted: for a name a test compares with, or passes as text, rather than writes into a statement.
    /// </summary>
    /// <exception cref="ArgumentException">The default model has nothing of that name.</exception>
    public string Of(string name) => ReferenceEquals(this, Default) ? name : _translation.Value.Of(name);

    /// <summary>
    /// <see cref="Of"/> as Postgres writes a name where it shows one, in a plan or in the text of a
    /// function's result: in double quotes only where the name needs them, <c>"SeatId"</c> and <c>seat_id</c>.
    /// </summary>
    public string Shown(string name) => Bare().IsMatch(Of(name)) ? Of(name) : "\"" + Of(name) + "\"";

    /// <summary>How this naming's model stores <paramref name="value"/>, which the default model stores as its name.</summary>
    /// <exception cref="ArgumentException">No column of the model stores that enum.</exception>
    public string Stored(Enum value) => ReferenceEquals(this, Default) ? value.ToString() : _translation.Value.Stored(value.ToString());

    /// <inheritdoc />
    public override string ToString() => Name;

    /// <summary>A name Postgres writes without quotes: all in lower case. None of these tables' names is a word it reserves.</summary>
    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex Bare();

    /// <summary>A text in single quotes, or a name in double quotes: a quote inside a text is part of the text.</summary>
    [GeneratedRegex("'(?<text>[^']*)'|\"(?<name>[^\"]+)\"")]
    private static partial Regex NamesAndTexts();

    /// <summary>What one naming calls everything another names, and how it stores what the other stores.</summary>
    private sealed class Translation
    {
        private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
        private readonly HashSet<string> _tables = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public static Translation Between(TenancyNaming written, TenancyNaming sent)
        {
            var translation = new Translation();
            using (var from = written.TenancyModel())
            using (var to = sent.TenancyModel())
            {
                translation.Add(from.Model, to.Model);
            }

            using (var from = written.WidgetModel())
            using (var to = sent.WidgetModel())
            {
                translation.Add(from.Model, to.Model);
            }

            return translation;
        }

        public string Sql(string written)
        {
            var matches = NamesAndTexts().Matches(written);
            return matches.Any(match => _tables.Contains(match.Groups["name"].Value))
                ? NamesAndTexts().Replace(written, match => match.Groups["name"].Success
                    ? "\"" + _names.GetValueOrDefault(match.Groups["name"].Value, match.Groups["name"].Value) + "\""
                    : "'" + _values.GetValueOrDefault(match.Groups["text"].Value, match.Groups["text"].Value) + "'")
                : written;
        }

        public string Of(string name)
            => _names.TryGetValue(name, out var named)
                ? named
                : throw new ArgumentException($"The default model has no table, column, key or index called '{name}'.", nameof(name));

        public string Stored(string value)
            => _values.TryGetValue(value, out var stored)
                ? stored
                : throw new ArgumentException($"No column of the default model stores '{value}'.", nameof(value));

        /// <summary>Every table of <paramref name="from"/> with its columns, keys, indexes and stored enum values, next to the same in <paramref name="to"/>.</summary>
        private void Add(IModel from, IModel to)
        {
            foreach (var written in from.GetEntityTypes())
            {
                // A view over a table has that table's names, and a function's row the function's own.
                if (written.GetTableName() is not { } tableName)
                {
                    continue;
                }

                var sent = to.FindEntityType(written.Name)
                    ?? throw new InvalidOperationException($"{written.DisplayName()} is mapped under the default names and not under the other naming.");
                var (table, sentTable) = (StoreObjectIdentifier.Table(tableName, written.GetSchema()), StoreObjectIdentifier.Table(sent.GetTableName()!, sent.GetSchema()));
                Name(tableName, sentTable.Name);
                _tables.Add(tableName);

                foreach (var property in written.GetProperties())
                {
                    var same = sent.FindProperty(property.Name)!;
                    Name(property.GetColumnName(table)!, same.GetColumnName(sentTable)!);
                    if (property.ClrType.IsEnum)
                    {
                        foreach (var value in Enum.GetValues(property.ClrType))
                        {
                            Value(StoredBy(property, value), StoredBy(same, value));
                        }
                    }
                }

                foreach (var key in written.GetKeys())
                {
                    Name(key.GetName()!, sent.FindKey([.. key.Properties.Select(property => sent.FindProperty(property.Name)!)])!.GetName()!);
                }

                foreach (var foreignKey in written.GetForeignKeys())
                {
                    var same = sent.GetForeignKeys().Single(candidate =>
                        candidate.PrincipalEntityType.Name == foreignKey.PrincipalEntityType.Name && SameProperties(candidate.Properties, foreignKey.Properties));
                    Name(foreignKey.GetConstraintName()!, same.GetConstraintName()!);
                }

                foreach (var index in written.GetIndexes())
                {
                    var same = sent.GetIndexes().Single(candidate =>
                        candidate.IsUnique == index.IsUnique && candidate.GetFilter() is null == index.GetFilter() is null && SameProperties(candidate.Properties, index.Properties));
                    Name(index.GetDatabaseName()!, same.GetDatabaseName()!);
                }
            }
        }

        private static bool SameProperties(IReadOnlyList<IProperty> one, IReadOnlyList<IProperty> other)
            => one.Select(property => property.Name).SequenceEqual(other.Select(property => property.Name), StringComparer.Ordinal);

        /// <summary>What the column of <paramref name="property"/> holds for <paramref name="value"/>, as text.</summary>
        private static string StoredBy(IProperty property, object value)
            => Convert.ToString(property.GetTypeMapping().Converter is { } converter ? converter.ConvertToProvider(value) : value, System.Globalization.CultureInfo.InvariantCulture)!;

        private void Name(string written, string sent) => Pair(_names, written, sent, "name");

        private void Value(string written, string sent) => Pair(_values, written, sent, "stored value");

        /// <summary>One spelling stands for one thing: a statement could not be rewritten name by name otherwise.</summary>
        private static void Pair(Dictionary<string, string> pairs, string written, string sent, string kind)
        {
            if (pairs.TryAdd(written, sent) || pairs[written] == sent)
            {
                return;
            }

            throw new InvalidOperationException(
                $"The {kind} '{written}' of the default model is both '{pairs[written]}' and '{sent}' under the other naming, so SQL written in the default names cannot be rewritten for it.");
        }
    }
}
