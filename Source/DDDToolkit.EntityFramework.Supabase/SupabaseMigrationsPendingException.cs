using System.Text;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// The database is missing migrations that a registered context has. Thrown by
/// <see cref="DependencyInjection.EnsureSupabaseMigrationsAppliedAsync"/> at start-up, so the application
/// refuses to run against a schema older than its code rather than failing on the first query that
/// touches a missing column.
/// </summary>
public sealed class SupabaseMigrationsPendingException : InvalidOperationException
{
    /// <summary>Names every context with migrations missing, and each missing migration.</summary>
    /// <param name="pending">Per context, the migrations the database does not have.</param>
    public SupabaseMigrationsPendingException(IReadOnlyList<(string Context, IReadOnlyList<string> Migrations)> pending)
        : base(Describe(pending ?? throw new ArgumentNullException(nameof(pending))))
        => Pending = pending;

    /// <summary>Per context, the migrations the database does not have.</summary>
    public IReadOnlyList<(string Context, IReadOnlyList<string> Migrations)> Pending { get; }

    private static string Describe(IReadOnlyList<(string Context, IReadOnlyList<string> Migrations)> pending)
    {
        var message = new StringBuilder("The database is missing migrations:");

        foreach (var (context, migrations) in pending)
        {
            message.AppendLine().Append("  ").Append(context).Append(": ").Append(string.Join(", ", migrations));
        }

        return message.AppendLine()
            .Append("Supabase applies them from supabase/migrations: 'supabase db reset' locally, 'supabase db push' for a linked project. ")
            .Append("The application does not apply them itself, because the CLI would then apply them a second time.")
            .ToString();
    }
}
