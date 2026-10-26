using Examples.Tenancy.Ui.Api.Wire;

namespace Examples.Tenancy.Ui.Api.TryIt;

/// <summary>A preset, run, and its answer.</summary>
/// <param name="Attempt">The preset.</param>
/// <param name="Answer">The answer: the preset's own call, or the sign-in that failed before it.</param>
public sealed record AttemptRun(AttemptPreset Attempt, ApiOutcome Answer)
{
    /// <summary>Whether the API answered as the preset expects.</summary>
    public bool AsExpected => Attempt.IsAnsweredBy(Answer);
}
