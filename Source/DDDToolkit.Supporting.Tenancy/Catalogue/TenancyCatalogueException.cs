namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// Thrown by <see cref="TenancyCatalogue.Build(ApplicationCatalogue, IEnumerable{Permission})"/> when the
/// catalogue does not hold together. Every problem found is reported at once, so one start-up shows all of them.
/// </summary>
public sealed class TenancyCatalogueException : InvalidOperationException
{
    /// <summary>Reports <paramref name="problems"/>.</summary>
    /// <param name="problems">Everything that is wrong, one sentence each.</param>
    public TenancyCatalogueException(IReadOnlyList<string> problems)
        : this(problems, advice: null)
    {
    }

    /// <summary>Reports <paramref name="problems"/>, and after them what most often causes problems like these.</summary>
    /// <param name="problems">Everything that is wrong, one sentence each.</param>
    /// <param name="advice">What most often causes them, said once after them; null when there is nothing to add.</param>
    internal TenancyCatalogueException(IReadOnlyList<string> problems, string? advice)
        : base(Describe(problems, advice))
    {
        Problems = problems;
    }

    /// <summary>Everything that is wrong, one sentence each.</summary>
    public IReadOnlyList<string> Problems { get; }

    private static string Describe(IReadOnlyList<string> problems, string? advice)
    {
        ArgumentNullException.ThrowIfNull(problems);

        var header = problems.Count == 1
            ? "The Tenancy catalogue has 1 problem:"
            : "The Tenancy catalogue has " + problems.Count + " problems:";

        return header + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(problem => "  " + problem))
               + (advice is null ? string.Empty : Environment.NewLine + advice);
    }
}
