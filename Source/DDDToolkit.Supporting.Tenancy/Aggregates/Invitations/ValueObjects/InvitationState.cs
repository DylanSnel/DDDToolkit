namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Where an invitation is in its life. It starts open and ends once: accepted, by the person who then has the
/// seat, or cancelled. An open invitation whose time ran out stays open in this column; whether it can still be
/// accepted is decided when somebody tries, from its end and the clock.
/// </summary>
public enum InvitationState
{
    /// <summary>Issued, and not accepted or cancelled since.</summary>
    Open,

    /// <summary>Accepted: it made a seat, and makes no other.</summary>
    Accepted,

    /// <summary>Cancelled before anybody accepted it.</summary>
    Cancelled,
}
