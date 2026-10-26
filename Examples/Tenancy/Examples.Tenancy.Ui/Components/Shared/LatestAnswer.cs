namespace Examples.Tenancy.Ui.Components.Shared;

/// <summary>
/// Which action of a page answered last. A page that cascades one shows that answer and no older one: an answer
/// is about the page as it was when its call was sent, so the next answer, of whichever action, takes its place.
/// Without it a refusal such as "the project is closed" stays under its button after the project is reopened.
/// </summary>
public sealed class LatestAnswer
{
    private object? _source;

    /// <summary>Records that <paramref name="source"/>, a button or the page itself, answered last.</summary>
    public void IsFrom(object source) => _source = source;

    /// <summary>Whether <paramref name="source"/> answered last.</summary>
    public bool IsOf(object source) => ReferenceEquals(_source, source);
}
