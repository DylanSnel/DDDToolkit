namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>The kinds of <see cref="MemberSource"/>.</summary>
public enum MemberSourceKind
{
    /// <summary>The caller's user id: <see cref="MemberSource.CallerId"/>.</summary>
    CallerId,

    /// <summary>A claim of the caller's token: <see cref="MemberSource.Claim"/>.</summary>
    Claim,

    /// <summary>An id the application resolves for the caller: <see cref="MemberSource.Resolved"/>.</summary>
    Resolved,
}
