namespace DDDToolkit.EntityFramework.Interceptors;

/// <summary>
/// The mark an access guard of the database refuses with, for the two packages that have to agree on it:
/// <c>DDDToolkit.EntityFramework</c> reads it from a failed save, and <c>DDDToolkit.EntityFramework.Postgres</c>
/// writes it into every access guard it writes. The second does not reference the first, so this one file is compiled
/// into both, as an internal type: there is one spelling of the mark, not two to keep alike.
/// </summary>
internal static class GuardMark
{
    /// <summary>
    /// The hint a guard raises SQLSTATE <c>42501</c> with, which <c>DatabaseRefusal.GuardHint</c> makes public:
    /// the toolkit's name, and the code the refusal is answered with.
    /// </summary>
    public const string Hint = "ddd:access.refused";
}
