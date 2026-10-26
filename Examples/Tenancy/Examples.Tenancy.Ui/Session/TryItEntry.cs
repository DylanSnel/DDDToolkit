using Examples.Tenancy.Ui.Api;
using Examples.Tenancy.Ui.Api.Wire;

namespace Examples.Tenancy.Ui.Session;

/// <summary>One call made on the try-it page, with the answer, and what it should have been for a preset.</summary>
/// <param name="At">When it was sent.</param>
/// <param name="What">
/// For the free form the name of the action's text, which the page says in its language; for a preset its title,
/// as the API gave it.
/// </param>
/// <param name="Answer">The answer.</param>
/// <param name="Preset">The preset it ran, whose expectation it is judged by; <see langword="null"/> for the free form.</param>
public sealed record TryItEntry(DateTimeOffset At, string What, ApiOutcome Answer, AttemptPreset? Preset = null)
{
    /// <summary>Whether a preset was answered as expected; <see langword="null"/> for the free form, which expects nothing.</summary>
    public bool? AsExpected => Preset?.IsAnsweredBy(Answer);
}
