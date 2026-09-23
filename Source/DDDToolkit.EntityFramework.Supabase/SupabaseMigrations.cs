using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// Turns a context's Entity Framework migrations into the SQL files the Supabase CLI applies, so that
/// <c>supabase db push</c>, <c>supabase db reset</c> and Supabase branching run the same schema changes
/// <c>dotnet ef database update</c> would.
/// <code>
/// // Write a file for every migration that does not have one yet.
/// SupabaseMigrations.Export(context, "supabase/migrations");
///
/// // In a test: fail when a migration was added and not exported.
/// SupabaseMigrations.EnsureInSync(context, "supabase/migrations");
/// </code>
/// <para>
/// One migration becomes one file, named after the migration id. Entity Framework ids start with a
/// <c>yyyyMMddHHmmss</c> timestamp, which is exactly the version Supabase reads from a file name, so
/// both histories sort the same way and a migration written with <c>supabase migration new</c> slots in
/// between them by date.
/// </para>
/// <para>
/// Each file is the script <c>dotnet ef migrations script</c> writes for that one step, with three
/// differences. It has no <c>START TRANSACTION</c> or <c>COMMIT</c>: the CLI already runs a file and
/// its own history row in one implicit transaction, and statements that cannot run in one, such as
/// <c>CREATE INDEX CONCURRENTLY</c>, are run on their own by the CLI. It turns on row level security
/// for new tables in exposed schemas; see <see cref="SupabaseMigrationOptions.RowLevelSecuritySchemas"/>.
/// And it starts with a comment naming the migration and its context, which is how an orphaned file is
/// recognised.
/// </para>
/// <para>
/// Several contexts can export into one directory, which is the usual shape for a modular monolith on
/// one Supabase project: each module's migrations, in its own schema, in one history. A context only
/// ever reports its own files as orphaned, and two migrations that share a timestamp are reported as
/// <see cref="SupabaseMigrationStatus.VersionTaken"/> rather than written.
/// </para>
/// <para>
/// The <c>__EFMigrationsHistory</c> insert stays in. After Supabase applies a file, Entity Framework
/// agrees that the migration is applied, so <c>GetPendingMigrations()</c> and <c>migrations list</c>
/// keep telling the truth. Let one of the two apply migrations, though, not both: Supabase does not
/// read Entity Framework's history, so a migration the application applied itself would be applied a
/// second time by the CLI.
/// </para>
/// <para>
/// Nothing here connects to a database. The context needs the Npgsql provider, because the SQL is
/// Postgres SQL, but a connection string that points nowhere is enough.
/// </para>
/// </summary>
public static class SupabaseMigrations
{
    /// <summary>Where the Supabase CLI looks for migrations, relative to the project root.</summary>
    public const string DefaultDirectory = "supabase/migrations";

    /// <summary>The provider whose SQL Supabase can run.</summary>
    public const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    // The first line of every exported file. It names the context as well as the migration, because
    // several contexts can export into one directory, and a file belongs to exactly one of them.
    private static readonly Regex Header = new(
        @"^-- Exported by DDDToolkit from the Entity Framework migration (?<id>\S+) of (?<context>\S+)\.$",
        RegexOptions.CultureInvariant);

    private static readonly Regex MigrationIdPattern = new(@"^[0-9]{14}_.+$", RegexOptions.CultureInvariant);

    // The Supabase CLI's own pattern for a migration file name.
    private static readonly Regex MigrationFilePattern = new(@"^([0-9]+)_(.*)\.sql$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The <c>supabase/migrations</c> directory of the Supabase project that contains
    /// <paramref name="start"/>: the nearest directory, from <paramref name="start"/> upwards, with a
    /// <c>supabase/config.toml</c> in it. That is how the Supabase CLI finds its project too, so the
    /// export lands where <c>supabase db push</c> looks, whether it runs from the repository root, from a
    /// project directory under <c>dotnet run</c>, or from a test's output directory.
    /// </summary>
    /// <param name="start">Where to start looking; the current directory when <see langword="null"/>.</param>
    /// <exception cref="DirectoryNotFoundException">No directory from <paramref name="start"/> up has a Supabase project.</exception>
    public static string FindDirectory(string? start = null)
    {
        for (var directory = new DirectoryInfo(start ?? Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "supabase", "config.toml")))
            {
                return Path.Combine(directory.FullName, "supabase", "migrations");
            }
        }

        throw new DirectoryNotFoundException(
            $"There is no supabase/config.toml in '{start ?? Environment.CurrentDirectory}' or any directory above it. " +
            "Run 'supabase init' in the directory that should hold the Supabase project, or pass the migrations directory explicitly.");
    }

    /// <summary>
    /// The file every migration of <paramref name="context"/> becomes, in migration order. Writes nothing.
    /// </summary>
    /// <param name="context">A context configured with the Npgsql provider; it is not opened.</param>
    /// <param name="options">How the files are written, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The context is not on Npgsql, or a migration id does not start with a 14 digit timestamp.
    /// </exception>
    public static IReadOnlyList<SupabaseMigrationFile> Generate(DbContext context, SupabaseMigrationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        options ??= new SupabaseMigrationOptions();

        var provider = context.Database.ProviderName;
        if (provider is not NpgsqlProviderName)
        {
            throw new InvalidOperationException(
                $"{context.GetType().Name} uses the provider '{provider}', and Supabase runs Postgres. " +
                $"Configure it with UseNpgsql to export its migrations; a connection string that points nowhere is enough, " +
                "because nothing is sent to a database.");
        }

        var migrations = context.GetService<IMigrationsAssembly>();
        var migrator = context.GetService<IMigrator>();
        var sql = context.GetService<ISqlGenerationHelper>();

        var files = new List<SupabaseMigrationFile>(migrations.Migrations.Count);
        var previous = Migration.InitialDatabase;

        foreach (var (id, type) in migrations.Migrations)
        {
            if (!MigrationIdPattern.IsMatch(id))
            {
                throw new InvalidOperationException(
                    $"The migration '{id}' does not start with a yyyyMMddHHmmss timestamp. Supabase orders migrations by " +
                    "that timestamp, so this one has no place in its history. Scaffolded migrations always have one; " +
                    "give a hand-written [Migration] id the same shape.");
            }

            var script = migrator.GenerateScript(previous, id, MigrationsSqlGenerationOptions.NoTransactions);
            var migration = migrations.CreateMigration(type, provider);

            files.Add(new SupabaseMigrationFile(id, Compose(id, context.GetType().Name, script, RowLevelSecurity(migration, options, sql))));
            previous = id;
        }

        return files;
    }

    /// <summary>
    /// Compares the migrations with the files in <paramref name="directory"/>. Writes nothing.
    /// </summary>
    /// <param name="context">A context configured with the Npgsql provider; it is not opened.</param>
    /// <param name="directory">The Supabase migrations directory; it need not exist yet.</param>
    /// <param name="options">How the files are written, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is empty or white space.</exception>
    public static SupabaseMigrationReport Compare(DbContext context, string directory = DefaultDirectory, SupabaseMigrationOptions? options = null)
        => Run(context, directory, options, write: false);

    /// <summary>
    /// Writes a file for every migration that has none, and reports on the rest. It never overwrites or
    /// deletes a file: once Supabase has applied a version it will not apply it again, so a rewritten file
    /// would change nothing on a database that already ran it and quietly diverge from one that did not.
    /// Check <see cref="SupabaseMigrationReport.IsInSync"/>, or call <see cref="EnsureInSync"/> afterwards.
    /// </summary>
    /// <param name="context">A context configured with the Npgsql provider; it is not opened.</param>
    /// <param name="directory">The Supabase migrations directory; it is created when missing.</param>
    /// <param name="options">How the files are written, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is empty or white space.</exception>
    public static SupabaseMigrationReport Export(DbContext context, string directory = DefaultDirectory, SupabaseMigrationOptions? options = null)
        => Run(context, directory, options, write: true);

    /// <summary>
    /// Throws unless every migration has its file, as generated, and no exported file has outlived its
    /// migration. Meant for a test, so that a migration nobody exported fails the build.
    /// </summary>
    /// <param name="context">A context configured with the Npgsql provider; it is not opened.</param>
    /// <param name="directory">The Supabase migrations directory.</param>
    /// <param name="options">How the files are written, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="SupabaseMigrationsOutOfSyncException">The directory does not match the migrations.</exception>
    public static void EnsureInSync(DbContext context, string directory = DefaultDirectory, SupabaseMigrationOptions? options = null)
    {
        var report = Compare(context, directory, options);
        if (!report.IsInSync)
        {
            throw new SupabaseMigrationsOutOfSyncException([report]);
        }
    }

    /// <summary>
    /// <see cref="Export(DbContext, string, SupabaseMigrationOptions?)"/> for every source, in order, into
    /// one directory. Needs no host: each context comes from its source's design-time factory, so this
    /// can run from a test, a small console app or the top of <c>Program.cs</c> without loading the
    /// application's configuration.
    /// </summary>
    /// <param name="sources">The contexts to export, typically one per module.</param>
    /// <param name="directory">The Supabase migrations directory, or <see langword="null"/> to <see cref="FindDirectory"/> it.</param>
    /// <param name="options">How the files are written, or <see langword="null"/> for the defaults.</param>
    /// <returns>One report per source, in the order given.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sources"/> is null.</exception>
    public static IReadOnlyList<SupabaseMigrationReport> Export(IEnumerable<SupabaseMigrationSource> sources, string? directory = null, SupabaseMigrationOptions? options = null)
        => RunAll(sources, directory, options, write: true);

    /// <summary>
    /// <see cref="Compare(DbContext, string, SupabaseMigrationOptions?)"/> for every source, in order,
    /// against one directory. Writes nothing and needs no host.
    /// </summary>
    /// <param name="sources">The contexts to compare, typically one per module.</param>
    /// <param name="directory">The Supabase migrations directory, or <see langword="null"/> to <see cref="FindDirectory"/> it.</param>
    /// <param name="options">How the files are written, or <see langword="null"/> for the defaults.</param>
    /// <returns>One report per source, in the order given.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sources"/> is null.</exception>
    public static IReadOnlyList<SupabaseMigrationReport> Compare(IEnumerable<SupabaseMigrationSource> sources, string? directory = null, SupabaseMigrationOptions? options = null)
        => RunAll(sources, directory, options, write: false);

    /// <summary>
    /// Throws unless every source's migrations have their files, as generated, and no source has an
    /// exported file that outlived its migration. One test for the whole application:
    /// <code>
    /// [Fact]
    /// public void Supabase_has_every_migration()
    ///     => SupabaseMigrations.EnsureInSync([OrderingModule.SupabaseMigrations, ShippingModule.SupabaseMigrations]);
    /// </code>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="sources"/> is null.</exception>
    /// <exception cref="SupabaseMigrationsOutOfSyncException">The directory does not match the migrations; the message covers every source.</exception>
    public static void EnsureInSync(IEnumerable<SupabaseMigrationSource> sources, string? directory = null, SupabaseMigrationOptions? options = null)
    {
        var reports = Compare(sources, directory, options);
        if (reports.Any(report => !report.IsInSync))
        {
            throw new SupabaseMigrationsOutOfSyncException(reports);
        }
    }

    private static List<SupabaseMigrationReport> RunAll(IEnumerable<SupabaseMigrationSource> sources, string? directory, SupabaseMigrationOptions? options, bool write)
    {
        ArgumentNullException.ThrowIfNull(sources);
        directory ??= FindDirectory();

        var reports = new List<SupabaseMigrationReport>();
        foreach (var source in sources)
        {
            using var context = source.CreateDesignTimeContext();
            reports.Add(Run(context, directory, options, write));
        }

        return reports;
    }

    private static SupabaseMigrationReport Run(DbContext context, string directory, SupabaseMigrationOptions? options, bool write)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var files = Generate(context, options);
        var existing = ExistingFiles(directory);
        var entries = new List<SupabaseMigrationEntry>(files.Count);

        foreach (var file in files)
        {
            var path = Path.Combine(directory, file.FileName);
            var sameVersion = existing.GetValueOrDefault(file.Version) ?? [];

            if (sameVersion.FirstOrDefault(p => Path.GetFileName(p) != file.FileName) is { } taken)
            {
                entries.Add(new(file.MigrationId, SupabaseMigrationStatus.VersionTaken, taken));
            }
            else if (sameVersion.Count == 0)
            {
                if (write)
                {
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(path, file.Sql, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }

                entries.Add(new(file.MigrationId, write ? SupabaseMigrationStatus.Created : SupabaseMigrationStatus.Missing, path));
            }
            else
            {
                var matches = string.Equals(
                    WithoutProductVersion(Normalize(File.ReadAllText(path)), file.MigrationId),
                    WithoutProductVersion(file.Sql, file.MigrationId),
                    StringComparison.Ordinal);
                entries.Add(new(file.MigrationId, matches ? SupabaseMigrationStatus.Unchanged : SupabaseMigrationStatus.Changed, path));
            }
        }

        var known = files.Select(f => f.MigrationId).ToHashSet(StringComparer.Ordinal);
        foreach (var path in existing.Values.SelectMany(p => p).Order(StringComparer.Ordinal))
        {
            if (ExportedFrom(path) is ({ } id, { } owner) && owner == context.GetType().Name && !known.Contains(id))
            {
                entries.Add(new(id, SupabaseMigrationStatus.Orphaned, path));
            }
        }

        return new SupabaseMigrationReport(directory, entries);
    }

    /// <summary>The <c>.sql</c> files the Supabase CLI would pick up, grouped by version.</summary>
    private static Dictionary<string, List<string>> ExistingFiles(string directory)
    {
        var byVersion = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (!Directory.Exists(directory))
        {
            return byVersion;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.sql"))
        {
            var match = MigrationFilePattern.Match(Path.GetFileName(path));
            if (match.Success)
            {
                var version = match.Groups[1].Value;
                if (!byVersion.TryGetValue(version, out var paths))
                {
                    byVersion[version] = paths = [];
                }

                paths.Add(path);
            }
        }

        return byVersion;
    }

    /// <summary>
    /// The migration a file was exported from and the context that owns it, or nulls for a file this
    /// toolkit did not write.
    /// </summary>
    private static (string? MigrationId, string? Context) ExportedFrom(string path)
    {
        using var reader = new StreamReader(path);
        var match = Header.Match(reader.ReadLine() ?? "");

        return match.Success ? (match.Groups["id"].Value, match.Groups["context"].Value) : (null, null);
    }

    /// <summary>
    /// <c>ENABLE ROW LEVEL SECURITY</c> for each table this migration creates in a schema that asks for it.
    /// </summary>
    private static IEnumerable<string> RowLevelSecurity(Migration migration, SupabaseMigrationOptions options, ISqlGenerationHelper sql)
    {
        if (options.RowLevelSecuritySchemas.Count == 0)
        {
            return [];
        }

        return migration.UpOperations
            .OfType<CreateTableOperation>()
            .Where(table => options.RowLevelSecuritySchemas.Contains(table.Schema ?? SupabaseMigrationOptions.PublicSchema))
            .Select(table => $"ALTER TABLE {sql.DelimitIdentifier(table.Name, table.Schema)} ENABLE ROW LEVEL SECURITY;");
    }

    private static string Compose(string id, string context, string script, IEnumerable<string> additions)
    {
        var file = new StringBuilder()
            .Append("-- Exported by DDDToolkit from the Entity Framework migration ").Append(id)
            .Append(" of ").Append(context).Append('.').Append('\n')
            .Append("-- Regenerate it with SupabaseMigrations.Export rather than editing it.").Append('\n')
            .Append('\n')
            .Append(Normalize(script).Trim('\n')).Append('\n');

        var first = true;
        foreach (var addition in additions)
        {
            if (first)
            {
                file.Append('\n').Append("-- Supabase serves this schema through its Data API, so its new tables get row level security.").Append('\n');
                first = false;
            }

            file.Append(addition).Append('\n');
        }

        return file.ToString();
    }

    /// <summary>
    /// The file with the Entity Framework version taken out of its history insert. That value is the
    /// version doing the exporting, not the one that wrote the migration, so without this every file
    /// would count as changed after an Entity Framework update that changed nothing else.
    /// </summary>
    private static string WithoutProductVersion(string sql, string migrationId)
        => Regex.Replace(
            sql,
            $@"(VALUES \('{Regex.Escape(migrationId)}', )'[^']*'\)",
            "$1'')",
            RegexOptions.CultureInvariant);

    /// <summary>
    /// <c>\n</c> line endings, so the same migration exports to the same bytes on Windows and on Linux,
    /// and a checkout that converted them still compares equal.
    /// </summary>
    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
