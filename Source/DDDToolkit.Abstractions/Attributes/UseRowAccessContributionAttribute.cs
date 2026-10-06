namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Writes a row access contribution of the application's own into its exported migrations: a class of the host's,
/// or of one of its modules, that no package declares. Put it in the project that runs the Supabase export:
/// <code>
/// [assembly: UseRowAccessContribution(typeof(UnitChangesWithItsKeys))]
/// </code>
/// <para>
/// The build hands the export the contributions listed this way, each made with <c>new X()</c>, next to those the
/// packages it references write by themselves (<see cref="RowAccessContributionAttribute"/>). A module that offers
/// its own with that attribute is written only where it is listed here, and the build warns until it is
/// (DDD00069). A package's contribution, a class derived from it or its closing over a type of the application's
/// is not listed again: the package writes it already, and listed it would be written twice (DDD00073). To write a
/// package's SQL with a class of your own, leave the package's out with
/// <see cref="LeaveOutRowAccessContributionAttribute"/> and list yours.
/// </para>
/// </summary>
/// <param name="contribution">The class that writes the SQL: public, with a public parameterless constructor, implementing <c>IRowAccessContribution</c>.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class UseRowAccessContributionAttribute(Type contribution) : Attribute
{
    /// <summary>The class that writes the SQL.</summary>
    public Type Contribution { get; } = contribution;
}
