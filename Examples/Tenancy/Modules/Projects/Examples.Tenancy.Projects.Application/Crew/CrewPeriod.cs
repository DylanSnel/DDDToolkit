using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Tenancy;

namespace Examples.Tenancy.Projects.Application.Crew;

/// <summary>
/// The period a crew command gives a place or a role for: from now, until a moment or for good. Every crew
/// command takes one the same way, so it is said once.
/// </summary>
internal static class CrewPeriod
{
    /// <summary>
    /// From <paramref name="now"/> until <paramref name="until"/>, or for good. An end that is not after now is
    /// refused as a grant's end is, with Tenancy's code, which a client has always been answered for it here.
    /// </summary>
    /// <param name="now">When the command runs.</param>
    /// <param name="until">The end the command was sent with, or <see langword="null"/> for none.</param>
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.invalid-period</c>.</exception>
    public static MemberPeriod Between(DateTimeOffset now, DateTimeOffset? until)
    {
        var period = GrantPeriod.Between(now, until);
        return MemberPeriod.Between(period.Starts, period.Ends);
    }
}
