using System.Text;
using System.Text.RegularExpressions;
using DDDToolkit.EntityFramework.Postgres;
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
/// Usually the build calls this for you: mark the design-time factory <see cref="SupabaseMigrationsAttribute"/>
/// and set <c>SupabaseMigrationsExport</c> in the project that references the modules. Call it yourself
/// from a test or a tool of your own.
/// </para>
/// <para>
/// One migration becomes one file, named after the migration id and the module. Entity Framework ids start with a
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

    // The first line of every file of row access rules, naming the context whose rules it holds.
    private static readonly Regex AccessHeader = new(
        @"^-- Written by DDDToolkit from the row access rules of (?<context>\S+)\.$",
        RegexOptions.CultureInvariant);

    // What a migration of a module with rules starts with: its rules come off, and the next access file puts them back.
    private const string AccessDropNote = "-- This module's row access rules come off before its schema changes, which a policy could stand in";

    private static readonly Regex AccessDropBlock = new(
        Regex.Escape(AccessDropNote) + @".*?\$ddd\$;\n\n",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex MigrationIdPattern = new(@"^[0-9]{14}_.+$", RegexOptions.CultureInvariant);

    // The Supabase CLI's own pattern for a migration file name.
    private static readonly Regex MigrationFilePattern = new(@"^([0-9]+)_(.*)\.sql$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The module name a context's files carry, as in <c>20260922120000_AddOrders.ordering.ddd.sql</c>:
    /// the name its assembly declares with <c>[assembly: Module("Ordering")]</c>, or else the context's
    /// name without a trailing <c>Context</c>; in lower case, with anything but letters and digits turned
    /// into a dash.
    /// <para>
    /// Code the build generates passes the name it read at compile time, so this is only consulted when a
    /// source or a context is exported by hand. It reads the attribute by name, which keeps this package
    /// free of a reference to <c>DDDToolkit.Abstractions</c>.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="contextType"/> is null.</exception>
    public static string ModuleNameOf(Type contextType)
    {
        ArgumentNullException.ThrowIfNull(contextType);

        var declared = contextType.Assembly.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType.FullName == "DDDToolkit.Abstractions.Attributes.ModuleAttribute")
            .Select(attribute => attribute.ConstructorArguments is [{ Value: string name }] ? name : null)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

        var name = declared
            ?? (contextType.Name.EndsWith("Context", StringComparison.Ordinal) && contextType.Name.Length > "Context".Length
                ? contextType.Name[..^"Context".Length]
                : contextType.Name);

        return NormalizeModuleName(name);
    }

    /// <summary>A module name as it appears in a file name: lower case, letters, digits and dashes.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public static string NormalizeModuleName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var normalized = new StringBuilder(name.Length);
        foreach (var character in name.Trim().ToLowerInvariant())
        {
            normalized.Append(char.IsAsciiLetterOrDigit(character) ? character : '-');
        }

        return normalized.ToString().Trim('-') is { Length: > 0 } result ? result : "module";
    }

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
        return Generate(context, ModuleNameOf(context.GetType()), options);
    }

    private static List<SupabaseMigrationFile> Generate(DbContext context, string module, SupabaseMigrationOptions? options)
    {
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

        // A module with rules starts every migration by taking its policies off, so no policy stops a column
        // it reads from being dropped or changed; the access file after the migration makes them again.
        var dropRules = PostgresRowAccess.RulesOf(context, options.RowAccessRules).Count > 0
            ? AccessDropNote + "\n-- the way of, and the file of row access rules after this one makes them again.\n" + PostgresRowAccess.DropStatement(context) + "\n"
            : string.Empty;

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

            files.Add(new SupabaseMigrationFile(id, module, Compose(id, context.GetType().Name, dropRules + script, RowLevelSecurity(migration, options, sql))));
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
        => Run(context, null, directory, options, write: false);

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
        => Run(context, null, directory, options, write: true);

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
    ///     => SupabaseMigrations.EnsureInSync([
    ///         SupabaseMigrationSource.For&lt;OrderingContext, OrderingContextFactory&gt;(),
    ///         SupabaseMigrationSource.For&lt;ShippingContext, ShippingContextFactory&gt;()]);
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
        foreach (var source in OwnersOfFunctionsFirst(sources, options))
        {
            using var context = source.CreateDesignTimeContext();
            reports.Add(Run(context, source.Module, directory, options, write));
        }

        return reports;
    }

    /// <summary>
    /// The sources, those whose context writes an access function first. A policy of another module that
    /// calls one is refused while the function does not exist, and access files written in one run are
    /// numbered in the order they are written.
    /// </summary>
    private static List<SupabaseMigrationSource> OwnersOfFunctionsFirst(IEnumerable<SupabaseMigrationSource> sources, SupabaseMigrationOptions? options)
    {
        var all = sources.ToList();
        if (options is null || options.RowAccessFunctions.Count == 0)
        {
            return all;
        }

        var owners = new HashSet<SupabaseMigrationSource>();
        foreach (var source in all)
        {
            using var context = source.CreateDesignTimeContext();
            if (PostgresRowAccess.FunctionsOf(context, options.RowAccessFunctions).Count > 0)
            {
                owners.Add(source);
            }
        }

        // OrderBy is stable, so the rest keep the order they came in.
        return [.. all.OrderBy(source => owners.Contains(source) ? 0 : 1)];
    }

    private static SupabaseMigrationReport Run(DbContext context, string? module, string directory, SupabaseMigrationOptions? options, bool write)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var files = Generate(context, module ?? ModuleNameOf(context.GetType()), options);
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
                // Rules added after a migration was exported do not make its file a changed one: the drop
                // at its start is compared as though neither side had it.
                var matches = string.Equals(
                    WithoutProductVersion(WithoutAccessDrop(Normalize(File.ReadAllText(path))), file.MigrationId),
                    WithoutProductVersion(WithoutAccessDrop(file.Sql), file.MigrationId),
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

        if (Access(context, module ?? ModuleNameOf(context.GetType()), directory, existing, files, options ?? new SupabaseMigrationOptions(), write) is { } access)
        {
            entries.Add(access);
        }

        return new SupabaseMigrationReport(directory, entries);
    }

    /// <summary>
    /// The module's file of row access rules: unchanged when the newest one says what the rules say now
    /// and no migration of the module came after it, and otherwise a new one, written after everything
    /// else in the directory, so the Supabase CLI applies it last. Nothing for a module that has no rules
    /// and never had any.
    /// </summary>
    private static SupabaseMigrationEntry? Access(
        DbContext context,
        string module,
        string directory,
        Dictionary<string, List<string>> existing,
        List<SupabaseMigrationFile> migrations,
        SupabaseMigrationOptions options,
        bool write)
    {
        var owner = context.GetType().Name;
        var rules = PostgresRowAccess.RulesOf(context, options.RowAccessRules);
        var functions = PostgresRowAccess.FunctionsOf(context, options.RowAccessFunctions);
        var files = existing.Values.SelectMany(paths => paths)
            .Where(path => AccessOwner(path) == owner)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToList();

        if (rules.Count == 0 && functions.Count == 0 && files.Count == 0)
        {
            return null;
        }

        EnsureDefined(rules.Select(rule => ($"The rule '{rule.Name}'", rule.Sql)).Concat(functions.Select(function => ($"The access function '{function.Name}'", function.Sql))), options);

        var sql = new StringBuilder()
            .Append("-- Written by DDDToolkit from the row access rules of ").Append(owner).Append('.').Append('\n')
            .Append("-- Written from those rules; change the rules, not this file. Every file like it says what the").Append('\n')
            .Append("-- rules are now: it drops the policies the one before it made, and makes them again.").Append('\n')
            .Append('\n')
            .Append(PostgresRowAccess.DropStatement(context))
            .Append(PostgresRowAccess.CreateStatements(context, rules, functions, SupabaseRowLevelSecurity.CallerFunctions))
            .ToString();

        var newestMigration = migrations.Count == 0 ? "" : migrations.Max(file => file.Version)!;
        if (files.LastOrDefault() is { } latest
            && string.CompareOrdinal(VersionOf(latest), newestMigration) > 0
            && string.Equals(Normalize(File.ReadAllText(latest)), sql, StringComparison.Ordinal))
        {
            return new(Path.GetFileNameWithoutExtension(latest).Split('.')[0], SupabaseMigrationStatus.Unchanged, latest);
        }

        if (!write)
        {
            return new($"{module} row access rules", SupabaseMigrationStatus.Missing, Path.Combine(directory, $"_access.{module}.ddd.sql"));
        }

        var version = NextVersion(options.TimeProvider.GetUtcNow().UtcDateTime, existing.Keys.Concat(migrations.Select(file => file.Version)));
        var path = Path.Combine(directory, $"{version}_access.{module}.ddd.sql");

        Directory.CreateDirectory(directory);
        File.WriteAllText(path, sql, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        existing[version] = [path];

        return new($"{version}_access", SupabaseMigrationStatus.Created, path);
    }

    /// <summary>
    /// Refuses SQL that asks an access function no module defines: a contract published without its
    /// definition, or a definition whose name changed. Postgres would refuse the policy when Supabase applies
    /// the file; this says so when the file is written, and names the rule.
    /// </summary>
    private static void EnsureDefined(IEnumerable<(string What, string Sql)> asking, SupabaseMigrationOptions options)
    {
        var defined = options.RowAccessFunctions.Select(function => function.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (what, sql) in asking)
        {
            if (PostgresRowAccess.FunctionsAskedBy(sql).FirstOrDefault(name => !defined.Contains(name)) is { } missing)
            {
                throw new InvalidOperationException(
                    $"{what} asks the access function {missing}, which no [AccessFunction] in the modules this host references defines. " +
                    $"Define it in the module whose aggregate it is about, with [AccessFunction<TAggregate>(\"{missing}\")].");
            }
        }
    }

    /// <summary>
    /// A version for a new file that sorts after every file in the directory, a second on from the newest
    /// when the clock is behind it, so the CLI never finds it older than what it already applied.
    /// </summary>
    private static string NextVersion(DateTime now, IEnumerable<string> taken)
    {
        const string Format = "yyyyMMddHHmmss";

        var candidate = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);
        foreach (var version in taken)
        {
            if (DateTime.TryParseExact(version, Format, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var used)
                && used >= candidate)
            {
                candidate = used.AddSeconds(1);
            }
        }

        return candidate.ToString(Format, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string VersionOf(string path) => MigrationFilePattern.Match(Path.GetFileName(path)) is { Success: true } match ? match.Groups[1].Value : "";

    /// <summary>The context whose rules a file holds, or null for a file that is not a file of rules.</summary>
    private static string? AccessOwner(string path)
    {
        using var reader = new StreamReader(path);
        return AccessHeader.Match(reader.ReadLine() ?? "") is { Success: true } match ? match.Groups["context"].Value : null;
    }

    private static string WithoutAccessDrop(string sql) => AccessDropBlock.Replace(sql, string.Empty);

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
            .Append("-- Written from that migration; change the migration, not this file.").Append('\n')
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
