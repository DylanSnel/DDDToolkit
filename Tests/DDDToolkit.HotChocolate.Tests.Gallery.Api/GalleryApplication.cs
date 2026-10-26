using System.Collections.Concurrent;
using DDDToolkit.Exceptions;

namespace Gallery.Application;

// What stands in for a module's application layer: the records its queries answer, which know nothing of
// GraphQL, and one service in place of the queries themselves, which also remembers what it was asked.

/// <summary>An exhibit as the gallery answers it. Nothing of it may be null but the note: it says so about itself, not about a schema.</summary>
public sealed record ExhibitOverview(int Id, string Title, int Year, string? Note, IReadOnlyList<string> Techniques, int CuratorId);

/// <summary>A loan of an exhibit. It has no key: nothing outside the gallery names one.</summary>
public sealed record LoanOverview(string Lender, int Days);

/// <summary>The permission keys the gallery's fields ask for.</summary>
public static class GalleryKeys
{
    /// <summary>Reading what an exhibit is insured for.</summary>
    public const string ViewValuations = "gallery.valuations.view";
}

/// <summary>The refusals the gallery gives.</summary>
public static class GalleryRefusals
{
    public const string NotPermitted = "gallery.not-permitted";

    /// <summary>The refusal for a key the caller does not hold on an exhibit, as a refused request of the gallery would give it.</summary>
    public static RefusalException NotPermittedFor(string key, int exhibit)
        => new(
            NotPermitted,
            RefusalKind.NotPermitted,
            "The caller may not do this here.",
            new Dictionary<string, object?> { ["Key"] = key, ["Exhibit"] = exhibit });
}

/// <summary>
/// The gallery's queries, in one service: what it has, which keys the caller holds on which exhibit, and a count
/// of what was asked, so a test sees how many questions a request cost.
/// </summary>
public sealed class GalleryDesk
{
    private readonly ConcurrentDictionary<int, HashSet<string>> _held = new();
    private int _valuationsRead;

    /// <summary>The three exhibits the gallery has.</summary>
    public IReadOnlyList<ExhibitOverview> Exhibits { get; } =
    [
        new(1, "Night Ferry", 1921, "On loan until spring.", ["oil", "canvas"], 41),
        new(2, "Salt Marsh", 1954, null, ["watercolor"], 41),
        new(3, "Tin Orchard", 1987, null, [], 42),
    ];

    /// <summary>Every question about held keys, with the exhibits it was asked for: one entry is one question.</summary>
    public ConcurrentQueue<int[]> KeyQuestions { get; } = new();

    /// <summary>How often a valuation was read: a refused field's resolver must not get here.</summary>
    public int ValuationsRead => Volatile.Read(ref _valuationsRead);

    /// <summary>Lets the caller hold a key on some exhibits.</summary>
    public GalleryDesk Hold(string key, params int[] exhibits)
    {
        foreach (var exhibit in exhibits)
        {
            _held.GetOrAdd(exhibit, static _ => []).Add(key);
        }

        return this;
    }

    public ExhibitOverview? Find(int id) => Exhibits.FirstOrDefault(exhibit => exhibit.Id == id);

    public IReadOnlyList<LoanOverview> LoansOf(int exhibit)
        => exhibit == 1 ? [new("Harbor Museum", 90), new("Mill Collection", 30)] : [];

    public int ValuationOf(int exhibit)
    {
        Interlocked.Increment(ref _valuationsRead);
        return exhibit * 1000;
    }

    /// <summary>The keys the caller holds on each of the exhibits, asked for all of them at once: a page of exhibits is one question.</summary>
    public IReadOnlyDictionary<int, IReadOnlySet<string>> HeldKeysOn(IReadOnlyList<int> exhibits)
    {
        KeyQuestions.Enqueue([.. exhibits]);

        return exhibits
            .Where(_held.ContainsKey)
            .ToDictionary(exhibit => exhibit, exhibit => (IReadOnlySet<string>)_held[exhibit]);
    }
}
