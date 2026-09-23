namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// How Entity Framework migrations are turned into Supabase migration files.
/// </summary>
public sealed class SupabaseMigrationOptions
{
    /// <summary>The schema Supabase exposes through its Data API unless a project says otherwise.</summary>
    public const string PublicSchema = "public";

    private ISet<string> _rowLevelSecuritySchemas = new HashSet<string>(StringComparer.Ordinal) { PublicSchema };

    /// <summary>
    /// The schemas whose new tables get <c>ENABLE ROW LEVEL SECURITY</c> appended to the migration that
    /// creates them. <c>public</c> by default.
    /// <para>
    /// Supabase serves every table in an exposed schema over its Data API, and grants the <c>anon</c> and
    /// <c>authenticated</c> roles access to it. A table Entity Framework creates there without row level
    /// security is therefore readable and writable by anyone holding the project's publishable key, which
    /// is why Supabase's security advisor reports it as an error. With row level security on and no
    /// policy, those roles see nothing, while the application keeps working: it connects as the table's
    /// owner, and an owner is not subject to row level security unless the table forces it.
    /// </para>
    /// <para>
    /// A table created without a schema lands in the connection's <c>search_path</c>, which on Supabase is
    /// <c>public</c>, so such tables are treated as <c>public</c>. Clear the set to turn this off, for
    /// instance when every table lives in a schema the Data API does not expose.
    /// </para>
    /// </summary>
    public ISet<string> RowLevelSecuritySchemas
    {
        get => _rowLevelSecuritySchemas;
        set => _rowLevelSecuritySchemas = value ?? throw new ArgumentNullException(nameof(value));
    }
}
