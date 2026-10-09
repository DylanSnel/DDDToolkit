namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// A row access contribution that hands the export what a package's contribution writes: the class the Supabase
/// build writes, into <c>DDDToolkit.RowAccessContributionsOfPackages.g.cs</c> of the project that runs the export,
/// for each package that declares itself a contributor with <c>[assembly: RowAccessContribution]</c>. It holds the
/// package's class, made from what the application marks, and answers what that answers, except for a context
/// the application leaves it out of.
/// </summary>
/// <remarks>
/// The comment above what it writes names the package's class and the package's assembly, without a version: not
/// the class the build wrote, which would put the exporting project's version in every access file, so that each
/// release of the application wrote them anew with nothing in them changed. A new version of the package that
/// writes other SQL is a new access file all the same, because the SQL differs; one that writes the same SQL is not.
/// </remarks>
public interface IPackageRowAccessContribution : IRowAccessContribution
{
    /// <summary>The package's contribution, as the build made it: what writes the SQL.</summary>
    IRowAccessContribution Contribution { get; }
}
