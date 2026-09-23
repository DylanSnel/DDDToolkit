namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// One Entity Framework migration as the Supabase CLI wants it: a file in <c>supabase/migrations</c>
/// whose name starts with the version the CLI records in <c>supabase_migrations.schema_migrations</c>.
/// </summary>
/// <param name="MigrationId">The Entity Framework migration id, for instance <c>20260922120000_AddOrders</c>.</param>
/// <param name="Module">The module the migration belongs to, as it appears in the file name.</param>
/// <param name="Sql">The file's contents, with <c>\n</c> line endings on every platform.</param>
public sealed record SupabaseMigrationFile(string MigrationId, string Module, string Sql)
{
    /// <summary>
    /// The version Supabase records: the timestamp that starts the migration id. Entity Framework and
    /// the Supabase CLI both use <c>yyyyMMddHHmmss</c>, so the two histories sort the same way.
    /// </summary>
    public string Version => MigrationId[..MigrationId.IndexOf('_', StringComparison.Ordinal)];

    /// <summary>
    /// The file name: the migration id, the module and <c>.ddd.sql</c>, as in
    /// <c>20260922120000_AddOrders.ordering.ddd.sql</c>. The Supabase CLI reads everything between the
    /// first underscore and <c>.sql</c> as the name, so the suffix is harmless to it, and it tells a file
    /// this toolkit wrote from one written by hand, and which module it belongs to, at a glance.
    /// </summary>
    public string FileName => $"{MigrationId}.{Module}.ddd.sql";
}

/// <summary>Where one migration stands against the files in a Supabase migrations directory.</summary>
public enum SupabaseMigrationStatus
{
    /// <summary>The file is there and says exactly what the migration generates.</summary>
    Unchanged,

    /// <summary>There is no file for this migration yet.</summary>
    Missing,

    /// <summary>There was no file for this migration, and the export just wrote it.</summary>
    Created,

    /// <summary>
    /// The file is there but its SQL is not what the migration generates now. Either the migration was
    /// edited after it was exported, or the file was. The export leaves it alone, because Supabase does
    /// not apply a version twice: a database that already ran the old SQL would never see the new one.
    /// </summary>
    Changed,

    /// <summary>
    /// Another file already uses this migration's version, so the Supabase CLI would treat the two as one
    /// migration. The export does not write next to it.
    /// </summary>
    VersionTaken,

    /// <summary>
    /// A file this toolkit exported for a migration that no longer exists, typically after
    /// <c>dotnet ef migrations remove</c>. Supabase would still apply it. The export never deletes
    /// anything, so remove it yourself if it was never applied anywhere.
    /// </summary>
    Orphaned,
}

/// <summary>What happened to, or would happen to, one migration's file.</summary>
/// <param name="MigrationId">The Entity Framework migration id.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Path">The file concerned: the one that exists, or the one that was or would be written.</param>
public sealed record SupabaseMigrationEntry(string MigrationId, SupabaseMigrationStatus Status, string Path);

/// <summary>Every migration's standing against one Supabase migrations directory.</summary>
public sealed class SupabaseMigrationReport
{
    internal SupabaseMigrationReport(string directory, IReadOnlyList<SupabaseMigrationEntry> entries)
    {
        Directory = directory;
        Entries = entries;
    }

    /// <summary>The directory that was compared or written to.</summary>
    public string Directory { get; }

    /// <summary>One entry per migration, in migration order, followed by any orphaned files.</summary>
    public IReadOnlyList<SupabaseMigrationEntry> Entries { get; }

    /// <summary>
    /// Whether the directory needs no attention: every migration has its file, as generated, and there is
    /// no file left behind by a migration that is gone. A file the export has just written counts.
    /// </summary>
    public bool IsInSync => Entries.All(e => e.Status is SupabaseMigrationStatus.Unchanged or SupabaseMigrationStatus.Created);

    /// <summary>The files the export wrote.</summary>
    public IEnumerable<SupabaseMigrationEntry> Created => Entries.Where(e => e.Status == SupabaseMigrationStatus.Created);

    /// <summary>The entries that need someone to look at them.</summary>
    public IEnumerable<SupabaseMigrationEntry> Problems => Entries.Where(e => e.Status is not (SupabaseMigrationStatus.Unchanged or SupabaseMigrationStatus.Created));
}
