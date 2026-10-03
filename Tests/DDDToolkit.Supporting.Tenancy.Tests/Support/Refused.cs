using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Tenancy.Tests.Support;

/// <summary>Asserts that an action is refused with a given code, and hands the refusal back for more.</summary>
public static class Refused
{
    /// <summary>Runs <paramref name="act"/> and asserts it throws a <see cref="RefusalException"/> with <paramref name="code"/>.</summary>
    public static RefusalException With(string code, Action act)
    {
        var refusal = FluentActions.Invoking(act).Should().Throw<RefusalException>().Which;
        return Checked(refusal, code);
    }

    /// <summary>Runs <paramref name="act"/> and asserts it fails with a <see cref="RefusalException"/> with <paramref name="code"/>.</summary>
    public static async Task<RefusalException> WithCodeAsync(string code, Func<Task> act, string because = "")
    {
        var refusal = (await FluentActions.Awaiting(act).Should().ThrowAsync<RefusalException>(because)).Which;
        return Checked(refusal, code);
    }

    private static RefusalException Checked(RefusalException refusal, string code)
    {
        refusal.Code.Should().Be(code, refusal.Message);
        refusal.Kind.Should().Be(TenancyRefusals.KindOf(code), "the kind of a code comes from the one table");
        return refusal;
    }
}
