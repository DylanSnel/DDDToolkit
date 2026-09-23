using DDDToolkit.Invariants;
using DDDToolkit.Localization;
using DDDToolkit.Validation;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.Tests.Localization;

/// <summary>
/// <see cref="FailureTranslations"/>: a missing translation fails a test rather than reaching a user,
/// including the one nobody notices, a neutral override hiding the toolkit's own translation.
/// </summary>
public class FailureTranslationsTests
{
    [Fact]
    public void A_code_translated_in_every_language_passes()
    {
        var check = FailureTranslations.Check(Localizer<TestFailures>(), "en", "nl").Codes(Till.MustStayUnderLimit.ViolationCode);

        check.Findings().Should().BeEmpty();
        check.Invoking(c => c.Verify()).Should().NotThrow();
    }

    [Fact]
    public void A_regional_culture_is_covered_by_its_language()
    {
        FailureTranslations.Check(Localizer<TestFailures>(), "en-GB", "nl-NL", "nl-BE")
            .Codes(Till.MustStayUnderLimit.ViolationCode)
            .Findings().Should().BeEmpty();
    }

    [Fact]
    public void A_language_with_no_resx_falls_back_to_the_neutral_text()
    {
        var finding = FailureTranslations.Check(Localizer<TestFailures>(), "en", "nl", "de")
            .Codes(Till.MustStayUnderLimit.ViolationCode)
            .Findings().Should().ContainSingle().Which;

        finding.Culture.Name.Should().Be("de");
        finding.Key.Should().Be(Till.MustStayUnderLimit.ViolationCode);
        finding.Problem.Should().Be(FailureTranslationProblem.FallsBack);
        finding.Detail.Should().Contain("TestFailures.resx");
    }

    [Fact]
    public void A_code_nobody_knows_is_missing_in_every_language()
    {
        FailureTranslations.Check(Localizer<TestFailures>(), "en", "nl").Codes("Nobody.Knows")
            .Findings().Should().HaveCount(2).And.OnlyContain(f => f.Problem == FailureTranslationProblem.Missing);
    }

    [Fact]
    public void The_toolkits_own_codes_are_covered_in_English_and_Dutch_out_of_the_box()
    {
        FailureTranslations.Check(Localizer<TestFailures>(), "en", "nl").ToolkitCodes()
            .Findings().Should().BeEmpty();
    }

    [Fact]
    public void Any_other_language_needs_the_toolkits_codes_in_your_own_resx()
    {
        var findings = FailureTranslations.Check(Localizer<TestFailures>(), "en", "de").ToolkitCodes().Findings();

        findings.Select(f => f.Key).Should().BeEquivalentTo(ValidationError.UnspecifiedCode, "ValueObjectValidator");
        findings.Should().OnlyContain(f => f.Culture.Name == "de" && f.Problem == FailureTranslationProblem.FallsBack);
    }

    [Fact]
    public void A_neutral_override_that_hides_the_toolkits_Dutch_is_reported()
    {
        var finding = FailureTranslations.Check(Localizer<OverridingFailures>(), "en", "nl").ToolkitCodes()
            .Findings().Should().ContainSingle().Which;

        finding.Culture.Name.Should().Be("nl");
        finding.Key.Should().Be("ValueObjectValidator");
        finding.Problem.Should().Be(FailureTranslationProblem.Hidden);
        finding.Detail.Should().Contain("OverridingFailures.resx").And.Contain("nl resx");
    }

    [Fact]
    public void Invariants_are_found_in_an_assembly_and_either_of_their_keys_counts()
    {
        var findings = FailureTranslations.Check(Localizer<TestFailures>(), "en", "nl")
            .Invariants(typeof(Till).Assembly)
            .Findings();

        // Till.OverLimit is translated under its bare code; the other rules in this assembly are not.
        findings.Should().NotContain(f => f.Key.Contains(Till.MustStayUnderLimit.ViolationCode));
        findings.Should().Contain(f => f.Key == "Drawer.NotNegative or NotNegative" && f.Problem == FailureTranslationProblem.Missing);
    }

    [Fact]
    public void Verify_throws_with_the_whole_report()
    {
        var exception = FluentActions.Invoking(() =>
                FailureTranslations.Check(Localizer<TestFailures>(), "en", "nl", "de").Codes("Nobody.Knows", Till.MustStayUnderLimit.ViolationCode).Verify())
            .Should().Throw<MissingFailureTranslationsException>().Which;

        exception.Findings.Should().HaveCount(4);
        exception.Message.Should().StartWith("4 failure translations are missing or hidden:")
            .And.Contain("de     Till.OverLimit");
    }

    [Fact]
    public void Only_the_toolkits_own_localizer_can_be_checked()
    {
        FluentActions.Invoking(() => FailureTranslations.Check(new Unknown(), "en"))
            .Should().Throw<ArgumentException>().WithMessage("*FailureLocalizer*");
    }

    private static IFailureLocalizer Localizer<TResource>()
        => new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddLocalization()
            .AddDDDToolkitLocalization(options => options.AddResource<TResource>())
            .BuildServiceProvider()
            .GetRequiredService<IFailureLocalizer>();

    private sealed class Unknown : IFailureLocalizer
    {
        public string Localize(ValidationError error) => error.Message;

        public string Localize(InvariantViolation violation) => violation.Message;
    }
}
