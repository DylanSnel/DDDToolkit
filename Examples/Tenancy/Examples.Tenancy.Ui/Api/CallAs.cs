namespace Examples.Tenancy.Ui.Api;

/// <summary>Whose token a call carries.</summary>
/// <remarks>
/// Three sources, kept apart because a 401 means different things for each: for the session's own token it means
/// the sign-in is over; for a preset's person or for no token at all it is simply the answer being shown.
/// </remarks>
public sealed class CallAs
{
    private CallAs(string? person, string? accessToken, bool isSession)
    {
        PersonKey = person;
        AccessToken = accessToken;
        IsSession = isSession;
    }

    /// <summary>The signed-in person, with the session's token.</summary>
    public static CallAs Session { get; } = new(null, null, isSession: true);

    /// <summary>Nobody: no <c>Authorization</c> header.</summary>
    public static CallAs Anonymous { get; } = new(null, null, isSession: false);

    /// <summary>Another person, with a token of theirs, as a try-it preset runs. The session is left alone.</summary>
    /// <param name="person">Whom the token was issued to, for the request line.</param>
    /// <param name="accessToken">Their token.</param>
    public static CallAs Person(string person, string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(person);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        return new(person, accessToken, isSession: false);
    }

    /// <summary>Whether the call carries the session's token.</summary>
    public bool IsSession { get; }

    /// <summary>The person a <see cref="Person"/> caller names, by key.</summary>
    public string? PersonKey { get; }

    /// <summary>The token a <see cref="Person"/> caller carries.</summary>
    public string? AccessToken { get; }
}
