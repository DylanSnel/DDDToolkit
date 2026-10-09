using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// The database is missing migrations that a registered context has. Thrown by
/// <see cref="DependencyInjection.EnsureSupabaseMigrationsAppliedAsync"/> at start-up, so the application
/// refuses to run against a schema older than its code rather than failing on the first query that
/// touches a missing column.
/// </summary>
public sealed partial class SupabaseMigrationsPendingException : InvalidOperationException
{
    /// <summary>Names every context with migrations missing, and each missing migration.</summary>
    /// <param name="pending">Per context, the migrations the database does not have.</param>
    public SupabaseMigrationsPendingException(IReadOnlyList<(string Context, IReadOnlyList<string> Migrations)> pending)
        : this(pending, [])
    {
    }

    /// <summary>
    /// Names every context with migrations missing, each missing migration, and what <paramref name="elsewhere"/> says
    /// of a context that reads its history from another table than its exported files record the migrations in.
    /// </summary>
    internal SupabaseMigrationsPendingException(IReadOnlyList<(string Context, IReadOnlyList<string> Migrations)> pending, IReadOnlyList<string> elsewhere)
        : base(Describe(pending ?? throw new ArgumentNullException(nameof(pending)), elsewhere))
        => Pending = pending;

    /// <summary>Per context, the migrations the database does not have.</summary>
    public IReadOnlyList<(string Context, IReadOnlyList<string> Migrations)> Pending { get; }

    /// <summary>
    /// What to say where <paramref name="running"/> reads its migration history from another table than the context
    /// <paramref name="source"/>'s design-time factory makes records the migrations in: every file the export writes
    /// records its migration where that factory's context does, so the application reads none of them as applied.
    /// <see langword="null"/> where the two agree, or where the factory's context cannot be made, which the export
    /// reports where it runs.
    /// </summary>
    /// <remarks>
    /// The two are compared by the statement each one's history repository writes to record a migration, which is
    /// Entity Framework's own answer, however the options of each said where the history is.
    /// </remarks>
    internal static string? HistoryElsewhere(SupabaseMigrationSource source, DbContext running)
    {
        string recorded;
        try
        {
            using var designTime = source.CreateDesignTimeContext();
            recorded = Recording(designTime);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }

        var read = Recording(running);
        return string.Equals(read, recorded, StringComparison.Ordinal)
            ? null
            : $"{source.ContextType.Name} reads its migration history from {TableOf(read)}, and its design-time factory, which the exported files are written with, " +
              $"records each migration in {TableOf(recorded)}: the files record their migrations where the application does not look. " +
              $"Where those files were applied to a database already, keep the history where they record it, and name that table in the application's options: {Naming(recorded)}. " +
              "Where none of them was applied anywhere yet, give the factory the application's history instead, UseDDDToolkitDesignTime() of DDDToolkit.EntityFramework where the application calls UseDDDToolkit, " +
              "or the application's own MigrationsHistoryTable(...), and for a context marked [SupabaseMigrations], whose factory the build writes, write a factory of your own beside it that does: the build then writes none and uses yours. " +
              "Then delete the files, export them again and reset the local database, since a file keeps the table it was exported with.";
    }

    private static string Describe(IReadOnlyList<(string Context, IReadOnlyList<string> Migrations)> pending, IReadOnlyList<string> elsewhere)
    {
        var message = new StringBuilder("The database is missing migrations:");

        foreach (var (context, migrations) in pending)
        {
            message.AppendLine().Append("  ").Append(context).Append(": ").Append(string.Join(", ", migrations));
        }

        foreach (var said in elsewhere)
        {
            message.AppendLine().Append(said);
        }

        return message.AppendLine()
            .Append("Supabase applies them from supabase/migrations: 'supabase db reset' locally, 'supabase db push' for a linked project. ")
            .Append("The application does not apply them itself, because the CLI would then apply them a second time.")
            .ToString();
    }

    /// <summary>The statement <paramref name="context"/>'s history repository records a migration with.</summary>
    private static string Recording(DbContext context)
        => context.GetService<IHistoryRepository>().GetInsertScript(new HistoryRow("00000000000000_WhereTheHistoryIs", "0"));

    /// <summary>
    /// The table <paramref name="recording"/> inserts into, as the SQL names it, in the schema Postgres finds it in
    /// where it names none, or the statement itself where it is no insert.
    /// </summary>
    private static string TableOf(string recording)
        => InsertInto().Match(recording) is { Success: true } match
            ? (match.Groups["schema"].Success ? match.Groups["schema"].Value : "public") + "." + match.Groups["name"].Value
            : recording.Trim();

    /// <summary>
    /// The call that names the table <paramref name="recording"/> inserts into, as the options of a provider write it:
    /// <c>MigrationsHistoryTable(HistoryRepository.DefaultTableName)</c> for Entity Framework's own table in the default
    /// schema, the name and the schema otherwise.
    /// </summary>
    private static string Naming(string recording)
    {
        if (InsertInto().Match(recording) is not { Success: true } match)
        {
            return "MigrationsHistoryTable(...)";
        }

        var name = Undelimited(match.Groups["name"].Value);
        var named = name == HistoryRepository.DefaultTableName ? "HistoryRepository.DefaultTableName" : Quoted(name);
        return match.Groups["schema"].Success
            ? $"MigrationsHistoryTable({named}, {Quoted(Undelimited(match.Groups["schema"].Value))})"
            : $"MigrationsHistoryTable({named})";

        static string Quoted(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>An identifier as the SQL writes it, without the double quotes Postgres needs around some.</summary>
    private static string Undelimited(string identifier)
        => identifier.Length > 1 && identifier[0] == '"' && identifier[^1] == '"'
            ? identifier[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal)
            : identifier;

    /// <summary>The table an insert into the history writes to: its schema, if it names one, and its name, each delimited or not.</summary>
    [GeneratedRegex("""INSERT INTO (?:(?<schema>"(?:[^"]|"")+"|[^\s."(]+)\.)?(?<name>"(?:[^"]|"")+"|[^\s."(]+) \(""", RegexOptions.CultureInvariant)]
    private static partial Regex InsertInto();
}
