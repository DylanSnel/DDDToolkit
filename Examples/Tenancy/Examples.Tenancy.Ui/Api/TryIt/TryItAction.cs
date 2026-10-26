namespace Examples.Tenancy.Ui.Api.TryIt;

/// <summary>One action of the try-it form: a route of the API, the fields it takes, and the body it sends.</summary>
/// <param name="Text">
/// What the form calls it: the name of a text in the UI's resource files, such as <c>try.action.open-project</c>,
/// so the form says it in the session's language. No two actions share one, so it is also what tells them apart.
/// </param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Route">
/// The route, with <c>{projectId}</c>, <c>{seatId}</c>, <c>{unitId}</c>, <c>{roleId}</c>, <c>{projectRoleId}</c> and
/// <c>{key}</c> for the form's values.
/// </param>
/// <param name="Fields">The fields it takes, in the order the form shows them.</param>
/// <param name="Body">Builds the JSON body from the form; <see langword="null"/> for an action that sends none.</param>
/// <param name="OrganizationRole">
/// Whether its role is one given at a unit, so the roles to pick from say which manage access: those only someone
/// holding their keys that do may give or take away.
/// </param>
/// <param name="OnlyAsks">
/// Whether it changes nothing although it is not a <c>GET</c>: a question whose ids travel in a body. Whoever may
/// read is answered, as with a <c>GET</c>.
/// </param>
public sealed record TryItAction(
    string Text,
    HttpMethod Method,
    string Route,
    IReadOnlyList<TryItField> Fields,
    Func<TryItInput, object?>? Body = null,
    bool OrganizationRole = false,
    bool OnlyAsks = false)
{
    /// <summary>Whether it only reads: a <c>GET</c>, or a question sent as a <c>POST</c>.</summary>
    public bool Reads => Method == HttpMethod.Get || OnlyAsks;

    /// <summary>The path to send, every value escaped as it was typed, so a malformed id reaches the API and is refused there.</summary>
    public string PathFor(TryItInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Route
            .Replace("{projectId}", SampleApi.Segment(input.Project), StringComparison.Ordinal)
            .Replace("{seatId}", SampleApi.Segment(input.Seat), StringComparison.Ordinal)
            .Replace("{unitId}", SampleApi.Segment(input.Unit), StringComparison.Ordinal)
            .Replace("{roleId}", SampleApi.Segment(input.Role), StringComparison.Ordinal)
            .Replace("{projectRoleId}", SampleApi.Segment(input.ProjectRole), StringComparison.Ordinal)
            .Replace("{key}", SampleApi.Segment(input.Key), StringComparison.Ordinal);
    }

    /// <summary>The body to send, or <see langword="null"/> for none.</summary>
    public object? BodyFor(TryItInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Body?.Invoke(input);
    }
}
