namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>The kinds of <see cref="MemberSource"/>.</summary>
public enum MemberSourceKind
{
    /// <summary>Who a member is follows from what it is known by: <see cref="MemberSource.Inferred"/>.</summary>
    Inferred,

    /// <summary>The caller's user id: <see cref="MemberSource.CallerId"/>.</summary>
    CallerId,

    /// <summary>A claim of the caller's token: <see cref="MemberSource.Claim"/>.</summary>
    Claim,

    /// <summary>An id the application resolves for the caller: <see cref="MemberSource.Resolved"/>.</summary>
    Resolved,
}
