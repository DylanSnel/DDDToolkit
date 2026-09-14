using DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;

namespace DDDToolkit.EntityFramework.Providers.Tests.Providers;

/// <summary>
/// The tests about the tests. Everything else in this project asks whether the toolkit works on a real
/// server; this asks whether a real server was there at all.
/// <para>
/// That question needs asking because the failure mode is silent. Every other test begins with
/// <c>SkipIfUnavailable</c>, so on a machine with no Docker the whole suite reports green having
/// touched nothing. A green build that proves nothing is worse than a red one, because people trust
/// it. <see cref="RequiredContainers"/> is the switch that closes the gap and this is where it is
/// exercised.
/// </para>
/// </summary>
public abstract class ProviderContainerTests(ProviderFixture fixture) : ProviderTestBase(fixture)
{
    /// <summary>The image this provider's fixture is supposed to be running.</summary>
    protected abstract string ExpectedImage { get; }

    [Fact]
    public void The_container_the_fixture_starts_is_the_pinned_one()
    {
        SkipIfUnavailable();

        // Not a tautology: it is what makes a change to ContainerImages show up as a test change, so
        // nobody bumps a database version in a commit that says something else.
        Fixture.Image.Should().Be(ExpectedImage);
        Fixture.Image.Should().NotContain("latest", "a floating tag makes somebody else's release able to turn this build red");
    }

    [Fact]
    public async Task The_server_that_answered_is_a_real_one_and_this_test_can_prove_it()
    {
        SkipIfUnavailable();

        // A skipped test and a passing test look identical in a CI summary. This one cannot pass
        // without a database having answered a query, so seeing it green means a server ran.
        var answer = await Database.ScalarAsync("SELECT 1", Cancellation);

        answer.Should().NotBeNull();
        Fixture.IsAvailable.Should().BeTrue();
        Fixture.SkipReason.Should().BeNull();
    }

    [Fact]
    public void When_CI_requires_containers_a_missing_one_is_a_failure_and_not_a_skip()
    {
        // No SkipIfUnavailable here on purpose: this test is about the decision itself, so it has to
        // run on a machine without Docker too. It is the only test in the project that does.
        RequiredContainers.IsSet("1").Should().BeTrue();
        RequiredContainers.IsSet("true").Should().BeTrue();
        RequiredContainers.IsSet("TRUE").Should().BeTrue();
        RequiredContainers.IsSet("yes").Should().BeTrue();

        RequiredContainers.IsSet(null).Should().BeFalse("an unset variable means a laptop, where skipping is honest");
        RequiredContainers.IsSet(string.Empty).Should().BeFalse();
        RequiredContainers.IsSet("0").Should().BeFalse();
        RequiredContainers.IsSet("false").Should().BeFalse();

        RequiredContainers.Explain(Fixture.ProviderName, "Docker was not reachable.")
            .Should().Contain(Fixture.ProviderName)
            .And.Contain(RequiredContainers.Variable)
            .And.Contain("Docker was not reachable.");
    }

    [Fact]
    public void A_missing_container_skips_when_it_may_and_fails_when_it_may_not()
    {
        // The branch every other test in this project depends on, exercised without a container and
        // without touching the environment, which the other provider's collection is reading from at the
        // same moment.
        var skipped = Capture(() => RequiredContainers.EnforceOrSkip(available: false, required: false, "PostgreSQL", "Docker was not reachable."));
        var failed = Capture(() => RequiredContainers.EnforceOrSkip(available: false, required: true, "PostgreSQL", "Docker was not reachable."));

        skipped.Should().NotBeNull();
        failed.Should().NotBeNull();

        // xunit signals the two outcomes with two different exception types, and telling them apart is
        // the entire point: one of them keeps a build green and the other does not.
        skipped!.GetType().Should().NotBe(failed!.GetType());
        failed.Message.Should().Contain(RequiredContainers.Variable);

        Capture(() => RequiredContainers.EnforceOrSkip(available: true, required: true, "PostgreSQL", skipReason: null))
            .Should().BeNull("a container that is there is not an event");
    }

    /// <summary>
    /// What <c>Record.Exception</c> cannot do here: hold on to the exception xunit uses to skip a test.
    /// That one is meant to escape, and letting it escape would skip this very test, which is the
    /// mistake this whole class exists to catch.
    /// </summary>
    private static Exception? Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
