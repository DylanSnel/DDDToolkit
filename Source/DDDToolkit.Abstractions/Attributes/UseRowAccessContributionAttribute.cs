namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Writes a row access contribution a package or a module offers into this application's exported
/// migrations. Put it in the project that runs the Supabase export, the host:
/// <code>
/// [assembly: UseRowAccessContribution(typeof(AuditRowAccess))]
/// </code>
/// <para>
/// The build hands exactly the contributions listed this way to the export, which asks each of them, for
/// every context, what it writes there. See <see cref="RowAccessContributionAttribute"/>.
/// </para>
/// </summary>
/// <param name="contribution">The class that writes the SQL: public, with a public parameterless constructor, implementing <c>IRowAccessContribution</c>.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class UseRowAccessContributionAttribute(Type contribution) : Attribute
{
    /// <summary>The class that writes the SQL.</summary>
    public Type Contribution { get; } = contribution;
}
