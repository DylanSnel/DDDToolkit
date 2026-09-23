using System.Globalization;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;
using DDDToolkit.Localization;
using DDDToolkit.Tests.Domain;
using DDDToolkit.Tests.Validation;
using DDDToolkit.Validation;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.Tests.Localization;

/// <summary>
/// Phrasing a failure in the reader's language, at the edge, from its code and its arguments.
/// <para>
/// DECISION (pinned here): the domain never looks at a culture. It reports a code, its own sentence and
/// the values; <see cref="IFailureLocalizer"/> picks the language from the current UI culture when a
/// failure becomes a response. A failure nobody translated keeps the domain's own sentence.
/// </para>
/// </summary>
public class FailureLocalizerTests
{
    // ---------------------------------------------------------------- lookup

    [Fact]
    public void A_failure_is_looked_up_by_its_code_and_filled_from_its_arguments()
    {
        var localizer = Localizer(("TooLong", "{PropertyName} mag hooguit {MaxLength} tekens zijn."));
        var error = new ValidationError("A street is at most 40 characters.", "Street", "TooLong").With("MaxLength", 40);

        localizer.Localize(error).Should().Be("Street mag hooguit 40 tekens zijn.");
    }

    [Fact]
    public void A_code_nobody_translated_keeps_the_domains_own_sentence()
    {
        var error = new ValidationError("A street is at most 40 characters.", "Street", "TooLong");

        Localizer().Localize(error).Should().Be("A street is at most 40 characters.");
        Localizer().Localize(new ValidationError("No code at all.")).Should().Be("No code at all.");
    }

    [Fact]
    public void A_violation_is_looked_up_for_its_entity_type_before_its_bare_code()
    {
        var localizer = Localizer(
            ("NotNegative", "Mag niet negatief zijn."),
            ("Drawer.NotNegative", "Een la kan geen {Coins} munten bevatten."));
        var id = new DrawerId(1);

        localizer.Localize(new InvariantViolation("NotNegative", "x", typeof(Drawer), id).With("Coins", -3))
            .Should().Be("Een la kan geen -3 munten bevatten.");
        localizer.Localize(new InvariantViolation("NotNegative", "x", typeof(Till), null))
            .Should().Be("Mag niet negatief zijn.");
    }

    [Fact]
    public void Sources_are_asked_in_order_and_the_first_that_knows_wins()
    {
        var localizer = new FailureLocalizer([Source(("C", "first")), Source(("C", "second"), ("D", "only here"))]);

        localizer.Localize(new ValidationError("m", code: "C")).Should().Be("first");
        localizer.Localize(new ValidationError("m", code: "D")).Should().Be("only here");
    }

    // ---------------------------------------------------------------- templates

    [Fact]
    public void A_template_can_name_the_failures_own_members()
    {
        var localizer = Localizer(
            ("V", "{PropertyName}={AttemptedValue} ({Code})"),
            ("I", "{EntityType} {EntityId}: {Code}"));
        var id = new DrawerId(7);

        localizer.Localize(new ValidationError("m", "Street", "V", "x")).Should().Be("Street=x (V)");
        localizer.Localize(new InvariantViolation("I", "m", typeof(Drawer), id)).Should().Be($"Drawer {id}: I");
    }

    [Fact]
    public void An_argument_wins_over_a_member_of_the_same_name()
    {
        var localizer = Localizer(("V", "{PropertyName}"));

        localizer.Localize(new ValidationError("m", "ShipTo", "V").With("PropertyName", "Ship to"))
            .Should().Be("Ship to");
    }

    [Fact]
    public void Braces_escape_and_an_unknown_placeholder_is_left_as_written()
    {
        var localizer = Localizer(("V", "{{literal}} {Missing} {Known}"));

        localizer.Localize(new ValidationError("m", code: "V").With("Known", "yes"))
            .Should().Be("{literal} {Missing} yes");
    }

    [Fact]
    public void Values_are_formatted_in_the_current_culture_with_the_templates_format()
    {
        var localizer = Localizer(("V", "{Amount:N2}"));
        var error = new ValidationError("m", code: "V").With("Amount", 1234.5m);

        using (new Culture("nl-NL"))
        {
            localizer.Localize(error).Should().Be("1.234,50");
        }

        using (new Culture("en-US"))
        {
            localizer.Localize(error).Should().Be("1,234.50");
        }
    }

    // ---------------------------------------------------------------- the toolkit's own messages

    [Fact]
    public void The_toolkits_own_messages_come_in_English_and_Dutch()
    {
        var error = new Mystery().ValidationErrors.Single();

        using (new Culture("en-US"))
        {
            FailureLocalizer.Default.Localize(error).Should().Be("Mystery is not valid.");
        }

        using (new Culture("nl-NL"))
        {
            FailureLocalizer.Default.Localize(error).Should().Be("Mystery is niet geldig.");
        }
    }

    [Fact]
    public void The_toolkits_own_messages_can_be_overridden()
    {
        var localizer = Localizer((ValidationError.UnspecifiedCode, "Klopt niet: {ValueObject}"));

        using (new Culture("nl-NL"))
        {
            localizer.Localize(new Mystery().ValidationErrors.Single()).Should().Be("Klopt niet: Mystery");
        }
    }

    // ---------------------------------------------------------------- whole lists

    [Fact]
    public void A_list_is_localized_without_losing_what_it_is_branched_on()
    {
        var localizer = Localizer(("TooLong", "Te lang."));
        var errors = new[] { new ValidationError("Too long.", "Street", "TooLong", "xxx").With("MaxLength", 2) };

        var localized = errors.Localized(localizer).Single();

        localized.Message.Should().Be("Te lang.");
        localized.Code.Should().Be("TooLong");
        localized.PropertyName.Should().Be("Street");
        localized.AttemptedValue.Should().Be("xxx");
        localized.Arguments["MaxLength"].Should().Be(2);
    }

    [Fact]
    public void An_error_dictionary_can_be_built_in_the_readers_language()
    {
        var localizer = Localizer(("TooLong", "Te lang."));

        var dictionary = new[] { new ValidationError("Too long.", "Street", "TooLong") }.ToErrorDictionary(localizer);

        dictionary["Street"].Should().Equal("Te lang.");
    }

    // ---------------------------------------------------------------- resx through dependency injection

    [Fact]
    public void A_resx_registered_through_dependency_injection_follows_the_ui_culture()
    {
        using var provider = Services()
            .AddLocalization()
            .AddDDDToolkitLocalization(options => options.AddResource<TestFailures>())
            .BuildServiceProvider();
        var localizer = provider.GetRequiredService<IFailureLocalizer>();

        var till = new Till(TillId.CreateUnique(), limit: 100m);
        till.Deposit(150m);
        var exception = FluentActions.Invoking(till.EnsureInvariants).Should().Throw<InvariantViolationException>().Which;

        using (new Culture("nl-NL"))
        {
            exception.InvariantViolations.Localized(localizer).Single().Message
                .Should().Be("In deze kassa mag hooguit 100,00 zitten, en er zit 150,00 in.");
        }

        using (new Culture("en-GB"))
        {
            exception.InvariantViolations.Localized(localizer).Single().Message
                .Should().Be("A till holds at most 100.00; this one holds 150.00.");
        }
    }

    [Fact]
    public void FluentValidations_codes_and_placeholders_can_be_translated_too()
    {
        using var provider = Services()
            .AddLocalization()
            .AddDDDToolkitLocalization(options => options.AddResource<TestFailures>())
            .BuildServiceProvider();
        var localizer = provider.GetRequiredService<IFailureLocalizer>();

        var errors = new Address("Main Street", new string('x', 50)).ValidationErrors;

        using (new Culture("nl-NL"))
        {
            errors.ToErrorDictionary(localizer)["City"].Should().Equal("City is hooguit 20 tekens lang.");
        }
    }

    [Fact]
    public void Each_module_can_register_its_own_translations_and_every_one_is_asked()
    {
        using var provider = Services()
            .AddDDDToolkitLocalization(options => options.AddLocalizer(Source(("Ordering.Code", "uit ordering"), ("Shared", "ordering eerst"))))
            .AddDDDToolkitLocalization(options => options.AddLocalizer(Source(("Shipping.Code", "uit shipping"), ("Shared", "shipping later"))))
            .BuildServiceProvider();
        var localizer = provider.GetRequiredService<IFailureLocalizer>();

        localizer.Localize(new ValidationError("m", code: "Ordering.Code")).Should().Be("uit ordering");
        localizer.Localize(new ValidationError("m", code: "Shipping.Code")).Should().Be("uit shipping");
        localizer.Localize(new ValidationError("m", code: "Shared")).Should().Be("ordering eerst", "the calls are asked in the order they were made");
    }

    [Fact]
    public void A_resx_without_AddLocalization_says_what_is_missing()
    {
        using var provider = Services()
            .AddDDDToolkitLocalization(options => options.AddResource<TestFailures>())
            .BuildServiceProvider();

        FluentActions.Invoking(() => provider.GetRequiredService<IFailureLocalizer>())
            .Should().Throw<InvalidOperationException>().WithMessage("*AddLocalization()*");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>What a host always has and a bare collection does not: the resx factory wants a logger factory.</summary>
    private static IServiceCollection Services()
        => new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

    private static FailureLocalizer Localizer(params (string Key, string Value)[] entries) => new([Source(entries)]);

    private static IStringLocalizer Source(params (string Key, string Value)[] entries) => new DictionaryLocalizer(entries);

    /// <summary>A localizer over a fixed table, the shape a translation store of your own would take.</summary>
    private sealed class DictionaryLocalizer((string Key, string Value)[] entries) : IStringLocalizer
    {
        private readonly Dictionary<string, string> _entries = entries.ToDictionary(e => e.Key, e => e.Value);

        public LocalizedString this[string name]
            => _entries.TryGetValue(name, out var value)
                ? new LocalizedString(name, value)
                : new LocalizedString(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => _entries.Select(e => new LocalizedString(e.Key, e.Value));
    }

    /// <summary>Sets both cultures for the duration of a block, and puts them back after.</summary>
    private sealed class Culture : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        public Culture(string name)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}
