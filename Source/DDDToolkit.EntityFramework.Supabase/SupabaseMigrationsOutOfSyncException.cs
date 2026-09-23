using System.Text;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// The Supabase migrations directory does not match the Entity Framework migrations: a migration has no
/// file, a file no longer says what its migration generates, or a file outlived its migration.
/// <para>
/// It is what <see cref="SupabaseMigrations.EnsureInSync"/> throws, so a test can fail a build that
/// added a migration and forgot to export it, rather than letting <c>supabase db push</c> discover it.
/// </para>
/// </summary>
public sealed class SupabaseMigrationsOutOfSyncException : InvalidOperationException
{
    /// <summary>Lists what is wrong and what to do about each.</summary>
    /// <param name="report">The comparison that found the problems.</param>
    public SupabaseMigrationsOutOfSyncException(SupabaseMigrationReport report)
        : base(Describe(report ?? throw new ArgumentNullException(nameof(report))))
        => Report = report;

    /// <summary>The comparison, with every entry, not just the problems.</summary>
    public SupabaseMigrationReport Report { get; }

    private static string Describe(SupabaseMigrationReport report)
    {
        var message = new StringBuilder()
            .Append("The Supabase migrations in '").Append(report.Directory)
            .Append("' do not match the Entity Framework migrations:");

        foreach (var entry in report.Problems)
        {
            message.AppendLine().Append("  ").Append(entry.MigrationId).Append(": ").Append(entry.Status switch
            {
                SupabaseMigrationStatus.Missing =>
                    $"has no file. Run SupabaseMigrations.Export to write {Path.GetFileName(entry.Path)}.",
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
