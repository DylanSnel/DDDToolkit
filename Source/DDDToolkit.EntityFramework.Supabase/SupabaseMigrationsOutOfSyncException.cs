using System.Text;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// The Supabase migrations directory does not match the Entity Framework migrations: a migration has no
/// file, a file no longer says what its migration generates, or a file outlived its migration.
/// <para>
/// It is what <c>SupabaseMigrations.EnsureInSync</c> throws, so a test can fail a build that added a
/// migration and forgot to export it, rather than letting <c>supabase db push</c> discover it.
/// </para>
/// </summary>
public sealed class SupabaseMigrationsOutOfSyncException : InvalidOperationException
{
    /// <summary>Lists what is wrong and what to do about each, for every context compared.</summary>
    /// <param name="reports">The comparisons, one per context; the ones in sync add nothing to the message.</param>
    public SupabaseMigrationsOutOfSyncException(IReadOnlyList<SupabaseMigrationReport> reports)
        : base(Describe(reports ?? throw new ArgumentNullException(nameof(reports))))
        => Reports = reports;

    /// <summary>The comparisons, one per context, with every entry, not just the problems.</summary>
    public IReadOnlyList<SupabaseMigrationReport> Reports { get; }

    /// <summary>Every entry that needs someone to look at it, across all contexts.</summary>
    public IEnumerable<SupabaseMigrationEntry> Problems => Reports.SelectMany(report => report.Problems);

    private static string Describe(IReadOnlyList<SupabaseMigrationReport> reports)
    {
        var message = new StringBuilder()
            .Append("The Supabase migrations in '").Append(reports.Count > 0 ? reports[0].Directory : "")
            .Append("' do not match the Entity Framework migrations:");

        foreach (var entry in reports.SelectMany(report => report.Problems))
        {
            message.AppendLine().Append("  ").Append(entry.MigrationId).Append(": ").Append(entry.Status switch
            {
                SupabaseMigrationStatus.Missing when entry.MigrationId.EndsWith(" row access rules", StringComparison.Ordinal) =>
                    "have no file that says what they are now: a rule changed, or a migration of the module came after the last file. " +
                    "A build with SupabaseMigrationsExport=Write, or SupabaseMigrations.Export, writes a new one.",
                SupabaseMigrationStatus.Missing =>
                    $"has no file. A build with SupabaseMigrationsExport=Write, or SupabaseMigrations.Export, writes {Path.GetFileName(entry.Path)}.",
                SupabaseMigrationStatus.Changed =>
                    $"{Path.GetFileName(entry.Path)} is not what the migration generates. If it was never applied anywhere, " +
                    "delete it and export again. If it was, put the change in a new migration instead: Supabase will not run a version twice.",
                SupabaseMigrationStatus.VersionTaken =>
                    $"its version is already used by {Path.GetFileName(entry.Path)}. Give one of them a new timestamp.",
                SupabaseMigrationStatus.Orphaned =>
                    $"{Path.GetFileName(entry.Path)} was exported for a migration that no longer exists. " +
                    "Delete it if it was never applied; otherwise restore the migration.",
                _ => entry.Status.ToString(),
            });
        }

        return message.ToString();
    }
}
