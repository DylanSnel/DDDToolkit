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
    public void The_toolkits_own_refusals_come_in_English_and_Dutch()
    {
        var refused = ToolkitRefusals.Refuse(ToolkitRefusals.Refused);

        using (new Culture("en-US"))
        {
            FailureLocalizer.Default.Localize(refused).Should().Be("The database refused this change.").And.Be(refused.Message, "the neutral text is the refusal's own");
        }

        using (new Culture("nl-NL"))
        {
            FailureLocalizer.Default.Localize(refused).Should().Be("De database heeft deze wijziging geweigerd.");
        }

        var notAllowed = ToolkitRefusals.Refuse(ToolkitRefusals.RoleNotAllowed, ("Role", "intern"));

        using (new Culture("en-US"))
        {
            FailureLocalizer.Default.Localize(notAllowed).Should().Be("The role this sign-in carries gives no access here: intern.").And.Be(notAllowed.Message);
        }

        using (new Culture("nl-NL"))
        {
            FailureLocalizer.Default.Localize(notAllowed).Should().Be("De rol van deze aanmelding geeft hier geen toegang: intern.", "the translation is filled from the refusal's arguments");
            FailureLocalizer.Default.Localize(ToolkitRefusals.Refuse(ToolkitRefusals.NotSignedIn)).Should().Be("Dit kan alleen een aangemelde gebruiker.");
            FailureLocalizer.Default.Localize(ToolkitRefusals.Refuse(ToolkitRefusals.SystemOnly)).Should().Be("Dit kan alleen de applicatie zelf.");
        }

        // Every code the toolkit refuses with has both texts, and the neutral one is the one it throws.
        foreach (var code in ToolkitRefusals.Codes)
        {
            using (new Culture("en-US"))
            {
                FailureLocalizer.Default.Localize(ToolkitRefusals.Refuse(code)).Should().Be(ToolkitRefusals.TemplateOf(code), code);
            }

            using (new Culture("nl-NL"))
            {
                FailureLocalizer.Default.Localize(ToolkitRefusals.Refuse(code)).Should().NotBe(ToolkitRefusals.TemplateOf(code), code + " has a Dutch text of its own");
            }
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

    // ---------------------------------------------------------------- refusals

    [Fact]
    public void A_refusal_is_localized_by_code_with_its_arguments()
    {
        var localizer = Localizer(("subscription.plan-closed", "Het abonnement {Plan} is gesloten ({Code}, {Kind})."));
        var refusal = new RefusalException(
            "subscription.plan-closed",
            RefusalKind.Conflict,
            "The gold plan is closed.",
            new Dictionary<string, object?> { ["Plan"] = "gold" });

        localizer.Localize(refusal).Should().Be("Het abonnement gold is gesloten (subscription.plan-closed, Conflict).");
    }

    [Fact]
    public void A_refusal_without_a_template_keeps_its_message()
    {
        var refusal = new RefusalException("subscription.plan-closed", RefusalKind.Conflict, "The gold plan is closed.");

        Localizer().Localize(refusal).Should().Be("The gold plan is closed.");
        FailureLocalizer.Default.Localize(refusal).Should().Be("The gold plan is closed.");
    }

    [Fact]
    public void An_existing_IFailureLocalizer_implementation_still_compiles()
    {
        // Written before refusals existed: it implements the two failure methods and nothing else.
        IFailureLocalizer localizer = new WrittenBeforeRefusals();
        var refusal = new RefusalException("subscription.plan-closed", RefusalKind.Conflict, "The gold plan is closed.");

        localizer.Localize(refusal).Should().Be("The gold plan is closed.", "the interface's default answers with the domain's own message");
        localizer.Localize(new ValidationError("m", code: "C")).Should().Be("OLD C");
    }

    /// <summary>A localizer of the shape every implementation had before <see cref="RefusalException"/> was added.</summary>
    private sealed class WrittenBeforeRefusals : IFailureLocalizer
    {
        public string Localize(ValidationError error) => "OLD " + error.Code;

        public string Localize(InvariantViolation violation) => "OLD " + violation.Code;
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

    // ---------------------------------------------------------------- a resx read under other codes

    [Fact]
    public void A_resx_is_read_under_the_codes_an_application_gives_its_failures()
    {
        // A package names its texts once; an application that chooses its own codes says which code reads which.
        using var provider = Services()
            .AddLocalization()
            .AddDDDToolkitLocalization(options => options.AddResource<TestFailures>(new Dictionary<string, string>
            {
                ["cash.drawer-full"] = "Till.OverLimit",
                ["cash.safe-full"] = "Till.OverLimit",
            }))
            .BuildServiceProvider();
        var localizer = provider.GetRequiredService<IFailureLocalizer>();

        var drawer = new RefusalException("cash.drawer-full", RefusalKind.Conflict, "The drawer is full.", new Dictionary<string, object?> { ["Limit"] = 100m, ["Cash"] = 150m });
        var safe = new RefusalException("cash.safe-full", RefusalKind.Conflict, "The safe is full.", new Dictionary<string, object?> { ["Limit"] = 5m, ["Cash"] = 6m });

        using (new Culture("nl-NL"))
        {
            localizer.Localize(drawer).Should().Be("In deze kassa mag hooguit 100,00 zitten, en er zit 150,00 in.");
            localizer.Localize(safe).Should().Be("In deze kassa mag hooguit 5,00 zitten, en er zit 6,00 in.", "several codes may read one entry");
        }

        using (new Culture("en-GB"))
        {
            localizer.Localize(drawer).Should().Be("A till holds at most 100.00; this one holds 150.00.");
        }
    }

    [Fact]
    public void A_resx_read_under_other_codes_answers_those_codes_and_no_other()
    {
        using var provider = Services()
            .AddLocalization()
            .AddDDDToolkitLocalization(options => options.AddResource<TestFailures>(new Dictionary<string, string>
            {
                ["cash.drawer-full"] = "Till.OverLimit",
                ["cash.unknown"] = "Nobody.Wrote.This",
            }))
            .BuildServiceProvider();
        var localizer = provider.GetRequiredService<IFailureLocalizer>();

        // The entry's own name is not one of the codes, so a failure that carries it keeps its own sentence.
        localizer.Localize(new RefusalException("Till.OverLimit", RefusalKind.Conflict, "Own sentence.")).Should().Be("Own sentence.");
        localizer.Localize(new RefusalException("cash.other", RefusalKind.Conflict, "Own sentence.")).Should().Be("Own sentence.");

        // A code that reads an entry the resx does not have is not found either, rather than shown as its name.
        localizer.Localize(new RefusalException("cash.unknown", RefusalKind.Conflict, "Own sentence.")).Should().Be("Own sentence.");
    }

    [Fact]
    public void A_resx_read_under_other_codes_is_checked_language_by_language_like_any_other()
    {
        using var provider = Services()
            .AddLocalization()
            .AddDDDToolkitLocalization(options => options.AddResource<TestFailures>(new Dictionary<string, string>
            {
                ["cash.drawer-full"] = "Till.OverLimit",
                ["cash.unknown"] = "Nobody.Wrote.This",
            }))
            .BuildServiceProvider();
        var localizer = provider.GetRequiredService<IFailureLocalizer>();

        FailureTranslations.Check(localizer, "en", "nl").Codes("cash.drawer-full").Findings().Should().BeEmpty();

        // German has no resx, and the check says whose neutral text a German reader gets.
        var german = FailureTranslations.Check(localizer, "en", "de").Codes("cash.drawer-full").Findings().Should().ContainSingle().Which;
        german.Problem.Should().Be(FailureTranslationProblem.FallsBack);
        german.Detail.Should().Contain("TestFailures.resx");

        FailureTranslations.Check(localizer, "en", "nl").Codes("cash.unknown", "Till.OverLimit")
            .Findings().Should().HaveCount(4).And.OnlyContain(finding => finding.Problem == FailureTranslationProblem.Missing);
    }

    [Fact]
    public void The_same_resx_is_added_once_for_each_set_of_codes()
    {
        // Two resources of one application, each with its own codes for the same rule.
        using var provider = Services()
            .AddLocalization()
            .AddDDDToolkitLocalization(options => options.AddResource<TestFailures>(new Dictionary<string, string> { ["drawers.full"] = "Till.OverLimit" }))
            .AddDDDToolkitLocalization(options => options.AddResource<TestFailures>(new Dictionary<string, string> { ["safes.full"] = "Till.OverLimit" }))
            .BuildServiceProvider();
        var localizer = provider.GetRequiredService<IFailureLocalizer>();
        var arguments = new Dictionary<string, object?> { ["Limit"] = 1m, ["Cash"] = 2m };

        using (new Culture("nl-NL"))
        {
            localizer.Localize(new RefusalException("drawers.full", RefusalKind.Conflict, "m", arguments)).Should().StartWith("In deze kassa");
            localizer.Localize(new RefusalException("safes.full", RefusalKind.Conflict, "m", arguments)).Should().StartWith("In deze kassa");
        }
    }

    [Fact]
    public void The_codes_a_resx_is_read_under_are_the_ones_it_was_added_with()
    {
        var codes = new Dictionary<string, string> { ["cash.drawer-full"] = "Till.OverLimit" };
        using var provider = Services()
            .AddLocalization()
            .AddDDDToolkitLocalization(options => options.AddResource<TestFailures>(codes))
            .BuildServiceProvider();

        // Changed afterwards: the source was added with one code, and answers that one.
        codes["cash.safe-full"] = "Till.OverLimit";
        var localizer = provider.GetRequiredService<IFailureLocalizer>();

        localizer.Localize(new RefusalException("cash.safe-full", RefusalKind.Conflict, "Own sentence.")).Should().Be("Own sentence.");

        var options = new FailureLocalizationOptions();
        FluentActions.Invoking(() => options.AddResource<TestFailures>(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => options.AddResource(null!, codes)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => options.AddResource<TestFailures>(new Dictionary<string, string> { ["cash.drawer-full"] = "" }))
            .Should().Throw<ArgumentException>().WithMessage("*both have a name*");
        FluentActions.Invoking(() => options.AddResource<TestFailures>(new Dictionary<string, string> { [""] = "Till.OverLimit" }))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_resx_read_under_other_codes_without_AddLocalization_says_what_is_missing()
    {
        using var provider = Services()
            .AddDDDToolkitLocalization(options => options.AddResource<TestFailures>(new Dictionary<string, string> { ["cash.drawer-full"] = "Till.OverLimit" }))
            .BuildServiceProvider();

        FluentActions.Invoking(() => provider.GetRequiredService<IFailureLocalizer>())
            .Should().Throw<InvalidOperationException>().WithMessage("*AddLocalization()*");
    }

    // ---------------------------------------------------------------- texts a package offers with its registration

    [Fact]
    public void Texts_a_package_offers_are_read_without_a_line_of_the_applications()
    {
        // The package's registration offers its resx under the codes the application chose. The application
        // only turns localization on: it adds no source for the package, and no resx factory of its own.
        using var provider = new ServiceCollection()
            .AddFailureTexts<TestFailures>(new Dictionary<string, string> { ["cash.drawer-full"] = "Till.OverLimit" })
            .AddDDDToolkitLocalization()
            .BuildServiceProvider();
        var localizer = provider.GetRequiredService<IFailureLocalizer>();
        var drawer = new RefusalException("cash.drawer-full", RefusalKind.Conflict, "The drawer is full.", new Dictionary<string, object?> { ["Limit"] = 100m, ["Cash"] = 150m });

        using (new Culture("nl-NL"))
        {
            localizer.Localize(drawer).Should().Be("In deze kassa mag hooguit 100,00 zitten, en er zit 150,00 in.");
        }

        using (new Culture("en-GB"))
        {
            localizer.Localize(drawer).Should().Be("A till holds at most 100.00; this one holds 150.00.");
        }

        using (new Culture("de-DE"))
        {
            localizer.Localize(drawer).Should().Be("A till holds at most 100,00; this one holds 150,00.", "a language the package does not ship reads its neutral text");
        }

        // Only the codes it was offered for: the entry's own name is not one of them.
        localizer.Localize(new RefusalException("Till.OverLimit", RefusalKind.Conflict, "Own sentence.")).Should().Be("Own sentence.");
    }

    [Fact]
    public void Texts_offered_without_codes_are_read_under_the_names_of_their_entries()
    {
        using var provider = new ServiceCollection()
            .AddDDDToolkitLocalization()
            .AddFailureTexts<TestFailures>()
            .BuildServiceProvider();
        var localizer = provider.GetRequiredService<IFailureLocalizer>();

        using (new Culture("nl-NL"))
        {
            localizer.Localize(new RefusalException("Till.OverLimit", RefusalKind.Conflict, "m", new Dictionary<string, object?> { ["Limit"] = 1m, ["Cash"] = 2m }))
                .Should().StartWith("In deze kassa", "an offer made after localization was turned on is read as well");
        }
    }

    [Fact]
    public void A_text_of_the_applications_own_for_a_packages_code_is_the_one_a_reader_gets_wherever_it_was_added()
    {
        var offered = new Dictionary<string, string> { ["cash.drawer-full"] = "Till.OverLimit" };
        var own = Source(("cash.drawer-full", "A full drawer, in the application's own words."));
        var drawer = new RefusalException("cash.drawer-full", RefusalKind.Conflict, "The drawer is full.");

        // The application's source added after the package registered, and before it: the application answers first either way.
        using var after = new ServiceCollection()
            .AddFailureTexts<TestFailures>(offered)
            .AddDDDToolkitLocalization(options => options.AddLocalizer(own))
            .BuildServiceProvider();
        using var before = new ServiceCollection()
            .AddDDDToolkitLocalization(options => options.AddLocalizer(own))
            .AddFailureTexts<TestFailures>(offered)
            .BuildServiceProvider();

        after.GetRequiredService<IFailureLocalizer>().Localize(drawer).Should().Be("A full drawer, in the application's own words.");
        before.GetRequiredService<IFailureLocalizer>().Localize(drawer).Should().Be("A full drawer, in the application's own words.");
    }

    [Fact]
    public void A_package_answers_before_the_toolkits_own_messages_and_offers_are_asked_in_the_order_they_were_made()
    {
        // The second offer reads a text of the test resx under the code of one of the toolkit's own messages,
        // ValidationError.UnspecifiedCode, which the toolkit answers itself when nothing else does.
        using var provider = new ServiceCollection()
            .AddFailureTexts<TestFailures>(new Dictionary<string, string> { ["shared"] = "Till.OverLimit" })
            .AddFailureTexts<TestFailures>(new Dictionary<string, string> { ["shared"] = "MaximumLengthValidator", [ValidationError.UnspecifiedCode] = "MaximumLengthValidator" })
            .AddDDDToolkitLocalization()
            .BuildServiceProvider();
        var localizer = provider.GetRequiredService<IFailureLocalizer>();
        using var toolkitAlone = new ServiceCollection().AddDDDToolkitLocalization().BuildServiceProvider();
        var unspecified = new ValidationError("m", code: ValidationError.UnspecifiedCode, arguments: new Dictionary<string, object?> { ["PropertyName"] = "City", ["MaxLength"] = 20, ["ValueObject"] = "City" });

        using (new Culture("en-GB"))
        {
            localizer.Localize(new RefusalException("shared", RefusalKind.Conflict, "m", new Dictionary<string, object?> { ["Limit"] = 1m, ["Cash"] = 2m }))
                .Should().StartWith("A till holds at most", "the first offer that knows a code answers");

            toolkitAlone.GetRequiredService<IFailureLocalizer>().Localize(unspecified).Should().Be("City is not valid.", "without the offer, the toolkit's own message answers");
            localizer.Localize(unspecified).Should().Be("City is at most 20 characters.", "an offer is asked before the toolkit's own messages");
        }
    }

    [Fact]
    public void An_application_that_does_not_localize_its_failures_is_given_no_localizer_by_an_offer()
    {
        // An offer changes nothing by itself: a failure keeps the domain's own sentence, as it did before the
        // package was registered, and nothing asks for a resx factory the application never added.
        using var provider = new ServiceCollection()
            .AddFailureTexts<TestFailures>(new Dictionary<string, string> { ["cash.drawer-full"] = "Till.OverLimit" })
            .BuildServiceProvider();

        provider.GetService<IFailureLocalizer>().Should().BeNull();
    }

    [Fact]
    public void Offered_texts_are_checked_language_by_language_like_any_other_source()
    {
        using var provider = new ServiceCollection()
            .AddFailureTexts<TestFailures>(new Dictionary<string, string> { ["cash.drawer-full"] = "Till.OverLimit", ["cash.unknown"] = "Nobody.Wrote.This" })
            .AddDDDToolkitLocalization()
            .BuildServiceProvider();
        var localizer = provider.GetRequiredService<IFailureLocalizer>();

        FailureTranslations.Check(localizer, "en", "nl").Codes("cash.drawer-full").Findings().Should().BeEmpty();

        // German has no resx in the package, and the check says whose neutral text a German reader gets.
        var german = FailureTranslations.Check(localizer, "en", "de").Codes("cash.drawer-full").Findings().Should().ContainSingle().Which;
        german.Problem.Should().Be(FailureTranslationProblem.FallsBack);
        german.Detail.Should().Contain("TestFailures.resx");

        FailureTranslations.Check(localizer, "en", "nl").Codes("cash.unknown").Findings()
            .Should().HaveCount(2).And.OnlyContain(finding => finding.Problem == FailureTranslationProblem.Missing);
    }

    [Fact]
    public void An_offer_answers_the_codes_it_was_made_with()
    {
        var codes = new Dictionary<string, string> { ["cash.drawer-full"] = "Till.OverLimit" };
        var offer = new FailureTexts(typeof(TestFailures), codes);

        // Changed afterwards: the offer was made with one code, and answers that one.
        codes["cash.safe-full"] = "Till.OverLimit";

        offer.ResourceSource.Should().Be(typeof(TestFailures));
        offer.Entries.Should().BeEquivalentTo(new Dictionary<string, string> { ["cash.drawer-full"] = "Till.OverLimit" });
        new FailureTexts(typeof(TestFailures)).Entries.Should().BeNull("texts read under the names of their entries");

        FluentActions.Invoking(() => new FailureTexts(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new FailureTexts(typeof(TestFailures), new Dictionary<string, string> { ["cash.drawer-full"] = "" }))
            .Should().Throw<ArgumentException>().WithMessage("*both have a name*");
        FluentActions.Invoking(() => new FailureTexts(typeof(TestFailures), new Dictionary<string, string> { [""] = "Till.OverLimit" }))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => FailureTextsServiceCollectionExtensions.AddFailureTexts<TestFailures>(null!)).Should().Throw<ArgumentNullException>();
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
