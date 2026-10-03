namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Offers a class that writes row level security of its own for the tables a package or a module maps: SQL
/// functions, policies and statements written from each context's Entity Framework model, which the Supabase
/// export and <c>PostgresRowAccess.Scripts</c> write next to the policies of the <c>[RowAccess]</c> rules:
/// <code>
/// [assembly: RowAccessContribution(typeof(AuditRowAccess))]
/// </code>
/// <para>
/// The class is public, has a public parameterless constructor and implements <c>IRowAccessContribution</c>,
/// from <c>DDDToolkit.EntityFramework.Postgres</c>. An offer alone writes nothing: SQL that the role running
/// the migrations creates belongs in an application's migrations only when the application chose it, with
/// <see cref="UseRowAccessContributionAttribute"/>. The build warns about an offer the application does not
/// use (DDD00054).
/// </para>
/// </summary>
/// <param name="contribution">The class that writes the SQL.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class RowAccessContributionAttribute(Type contribution) : Attribute
{
    /// <summary>The class that writes the SQL.</summary>
    public Type Contribution { get; } = contribution;
}
