using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.Tests.Support;

/// <summary>Asserts that an action is refused under a resource's own code for one of the package's rules, and hands the refusal back for more.</summary>
public static class Refused
{
    /// <summary>
    /// Runs <paramref name="act"/> and asserts it throws a <see cref="RefusalException"/> with the code
    /// <paramref name="codes"/> give <paramref name="rule"/>, and the kind the package gives that rule.
    /// </summary>
    public static RefusalException With(MembershipCodes codes, string rule, Action act)
    {
        var refusal = FluentActions.Invoking(act).Should().Throw<RefusalException>().Which;
        return Checked(refusal, codes, rule);
    }

    /// <summary>The same, for something asked asynchronously.</summary>
    public static async Task<RefusalException> WithCodeAsync(MembershipCodes codes, string rule, Func<Task> act)
    {
        var refusal = (await FluentActions.Awaiting(act).Should().ThrowAsync<RefusalException>()).Which;
        return Checked(refusal, codes, rule);
    }

    private static RefusalException Checked(RefusalException refusal, MembershipCodes codes, string rule)
    {
        refusal.Code.Should().Be(codes[rule], refusal.Message);
        refusal.Kind.Should().Be(MembershipRefusals.KindOf(rule), "the kind of a rule comes from the one table, whatever a resource calls it");
        return refusal;
    }
}
