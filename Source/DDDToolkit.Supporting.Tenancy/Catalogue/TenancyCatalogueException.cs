namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// Thrown by <see cref="TenancyCatalogue.Build(ApplicationCatalogue, IEnumerable{Permission})"/> when the
/// catalogue does not hold together. Every problem found is reported at once, so one start-up shows all of them.
/// </summary>
/// <param name="problems">Everything that is wrong, one sentence each.</param>
public sealed class TenancyCatalogueException(IReadOnlyList<string> problems)
    : InvalidOperationException(Describe(problems))
{
    /// <summary>Everything that is wrong, one sentence each.</summary>
    public IReadOnlyList<string> Problems { get; } = problems;

    private static string Describe(IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        var header = problems.Count == 1
            ? "The Tenancy catalogue has 1 problem:"
            : "The Tenancy catalogue has " + problems.Count + " problems:";

        return header + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(problem => "  " + problem));
    }
}
