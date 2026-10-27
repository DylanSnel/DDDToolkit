namespace DDDToolkit.Supporting.Tenancy.UseCases;

/// <summary>What the managers' reads share: how many ids one question takes, and the order seats are answered in.</summary>
internal static class TenancyReads
{
    /// <summary>How many ids one question by id takes.</summary>
    public const int MostIds = 200;

    /// <summary>
    /// The different ids among <paramref name="ids"/>. Refused when they are more than one question takes;
    /// asked after the caller, so nobody learns the limit before its own refusal.
    /// </summary>
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.too-many-ids</c>, with <c>Max</c>.</exception>
    public static HashSet<TId> Asked<TId>(IReadOnlyCollection<TId> ids)
        where TId : struct, IEquatable<TId>
    {
        var asked = new HashSet<TId>(ids);
        return asked.Count > MostIds
            ? throw TenancyRefusals.Refuse(TenancyRefusals.TooManyIds, ("Max", MostIds))
            : asked;
    }

    /// <summary>
    /// The order of ids: the id's own, which every id the toolkit generates has, so ids that are numbers come as numbers
    /// do, 2 before 10, and Guids in the order of their text; for an id without one, the order of its text.
    /// </summary>
    public static IComparer<TId> IdOrder<TId>()
        => typeof(IComparable<TId>).IsAssignableFrom(typeof(TId)) || typeof(System.IComparable).IsAssignableFrom(typeof(TId))
            ? Comparer<TId>.Default
            : Comparer<TId>.Create(static (one, other) => string.CompareOrdinal(one?.ToString(), other?.ToString()));
}
