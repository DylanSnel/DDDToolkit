using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;
using DDDToolkit.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Every refusal code has one kind and one English text, written in one table; the package's resx holds the
/// same English text and a Dutch translation of each; and the codes the nested invariants report are the
/// same codes, never two alike in one parent.
/// <para>
/// The two resource files are also held to each other, entry by entry, whatever the table says: an entry of
/// one is an entry of the other, with the same placeholders, and no Dutch text has a word for the reader. A
/// refusal of kind Invalid names the input it is about.
/// </para>
/// </summary>
public partial class RefusalTableTests
{
    /// <summary>
    /// The Dutch words for the reader, familiar and formal. A Dutch text uses none of them: which of the two
    /// tones fits is the application's to choose, in a resx of its own.
    /// </summary>
    private static readonly string[] WordsForTheReader =
        ["je", "jij", "jou", "jouw", "jezelf", "jouzelf", "jullie", "u", "uw", "uwe", "uzelf", "gij", "ge"];

    /// <summary>
    /// The codes of kind Invalid that are about no one input of a command. The tenant is named beside the
    /// command, in a header or in the address, so a form has no input to put that text under.
    /// </summary>
    private static readonly string[] AboutNoOneInput = [TenancyRefusals.TenantRequired];

    /// <summary>The codes declared as constants on <see cref="TenancyRefusals"/>.</summary>
    private static IReadOnlyList<string> DeclaredCodes()
        => typeof(TenancyRefusals).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

    private static Dictionary<string, string> Resx(CultureInfo culture)
    {
        var resources = new ResourceManager(typeof(TenancyFailures).FullName!, typeof(TenancyFailures).Assembly);
        var set = resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!;
        return set.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    private static FailureLocalizer Localizer()
    {
        var factory = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new FailureLocalizer([factory.Create(typeof(TenancyFailures))]);
    }

    [Fact]
    public void Every_code_has_a_kind_and_an_english_message()
    {
        var declared = DeclaredCodes();

        TenancyRefusals.Codes.Should().BeEquivalentTo(declared, "every constant is in the table and the table has nothing else");
        declared.Should().OnlyHaveUniqueItems().And.OnlyContain(code => code.StartsWith("tenancy.", StringComparison.Ordinal));

        var english = Resx(CultureInfo.InvariantCulture);
        english.Keys.Should().BeEquivalentTo(declared, "TenancyFailures.resx holds exactly the table's codes");

        foreach (var code in declared)
        {
            Enum.IsDefined(TenancyRefusals.KindOf(code)).Should().BeTrue();
            TenancyRefusals.TemplateOf(code).Should().NotBeNullOrWhiteSpace().And.EndWith(".");
            english[code].Should().Be(TenancyRefusals.TemplateOf(code), "the resx's English is the table's, for " + code);

            var refusal = TenancyRefusals.Of(code);
            refusal.Code.Should().Be(code);
            refusal.Kind.Should().Be(TenancyRefusals.KindOf(code));
        }

        FluentActions.Invoking(() => TenancyRefusals.Of("tenancy.nothing-like-it")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Every_code_has_a_dutch_translation()
    {
        var localizer = Localizer();

        FailureTranslations.Check(localizer, "en", "nl").Codes(TenancyRefusals.Codes).Verify();
        Resx(CultureInfo.GetCultureInfo("nl")).Keys.Should().BeEquivalentTo(TenancyRefusals.Codes, "the Dutch resx has every code and no other");

        var organization = New.Organization();
        var refusal = Refused.With(TenancyRefusals.NameInvalid, () => organization.RenameUnit<SeatId>(organization.Root.Id, " "));
        var tooLong = Refused.With(TenancyRefusals.NameInvalid, () => New.Seat().Rename(new string('n', 201)));
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nl-NL");
            localizer.Localize(refusal).Should().Be("Vul 1 tot 200 tekens in.", "a Dutch reader reads no English word, whichever name it is about");
            localizer.Localize(tooLong).Should().Be("Vul 1 tot 200 tekens in.");

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
            localizer.Localize(refusal).Should().Be(refusal.Message).And.Be("Enter 1 to 200 characters.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void An_entry_of_one_resource_file_is_an_entry_of_the_other_with_the_same_placeholders()
    {
        var english = Resx(CultureInfo.InvariantCulture);
        var dutch = Resx(CultureInfo.GetCultureInfo("nl"));

        english.Keys.Except(dutch.Keys).Should().BeEmpty(
            "every entry of TenancyFailures.resx needs a Dutch text in TenancyFailures.nl.resx, under the same name");
        dutch.Keys.Except(english.Keys).Should().BeEmpty(
            "every entry of TenancyFailures.nl.resx needs an English text in TenancyFailures.resx, under the same name");

        foreach (var (name, text) in dutch)
        {
            text.Should().NotBeNullOrWhiteSpace("the Dutch text of " + name + " says something");
            english[name].Should().NotBeNullOrWhiteSpace("the English text of " + name + " says something");
            text.Should().NotBe(english[name], "the Dutch text of " + name + " is Dutch");
            Placeholders(text).Should().BeEquivalentTo(
                Placeholders(english[name]),
                "the Dutch text of " + name + " is filled from the same arguments as the English one: a placeholder nobody fills is shown as written");
        }
    }

    [Fact]
    public void No_dutch_text_uses_a_pronoun()
    {
        var addressing = Resx(CultureInfo.GetCultureInfo("nl"))
            .Select(entry => (entry.Key, Text: entry.Value, Words: WordsOf(entry.Value).Intersect(WordsForTheReader).ToArray()))
            .Where(entry => entry.Words.Length > 0)
            .Select(entry => entry.Key + " says \"" + string.Join("\", \"", entry.Words) + "\": " + entry.Text)
            .ToArray();

        addressing.Should().BeEmpty(
            "a Dutch text of the package has no word for the reader, neither the familiar nor the formal one. Say what "
            + "is the case or what is needed instead: \"Deze plaats in de tenant is geschorst.\", not \"Je plaats in deze "
            + "tenant is geschorst.\"; \"Een toekenning gaat nu in.\", not \"Een toekenning die jij doet gaat nu in.\"");

        // The check itself: it finds the words as words, in any case, and not inside other words or placeholders.
        WordsOf("Je mist het recht {U}; vraag uw hoofdgebruiker, of U zelf.").Intersect(WordsForTheReader).Should().Equal("je", "uw", "u");
        WordsOf("Deze uitleg juicht, en een menu is geen jeu; id's ook niet.").Intersect(WordsForTheReader).Should().BeEmpty();
    }

    [Fact]
    public void The_tenant_required_text_names_no_header()
    {
        var localizer = Localizer();
        var refusal = TenancyRefusals.Of(TenancyRefusals.TenantRequired);

        // How a request names its tenant is the host's: a header, a part of the address, a host name.
        refusal.Message.Should().Be("This request names no tenant.");
        localizer.Localize(refusal, CultureInfo.GetCultureInfo("en")).Should().Be("This request names no tenant.");
        localizer.Localize(refusal, CultureInfo.GetCultureInfo("nl")).Should().Be("Dit verzoek noemt geen tenant.");

        foreach (var text in new[] { refusal.Message, localizer.Localize(refusal, CultureInfo.GetCultureInfo("nl")) })
        {
            text.Should().NotContainEquivalentOf("header").And.NotContainEquivalentOf("slug");
        }

        refusal.Arguments.Should().NotContainKey(RefusalException.FieldArgument, "the tenant is no input of the command");
    }

    [Fact]
    public void A_refusal_is_phrased_in_the_language_named_whatever_the_current_culture()
    {
        var localizer = Localizer();
        var refusal = TenancyRefusals.Of(TenancyRefusals.NotPermitted, ("Key", TenancyKeys.SeatsManage), ("Unit", null));

        using (CultureScope.Use("en-GB"))
        {
            // A mail or a notice, written by a job: the reader's language is said, not found.
            localizer.Localize(refusal, CultureInfo.GetCultureInfo("nl-NL")).Should().Be("Hiervoor is het recht tenancy.seats.manage nodig.");

            using (CultureScope.Use("nl"))
            {
                localizer.Localize(refusal).Should().Be("Hiervoor is het recht tenancy.seats.manage nodig.");
            }

            localizer.Localize(refusal).Should().Be("You lack the permission tenancy.seats.manage.");
        }
    }

    [Fact]
    public async Task Every_invalid_refusal_about_one_input_carries_its_field()
    {
        var harness = Harness.OfHarbor();
        var root = harness.Harbor.Root;
        var ada = harness.Administrator;
        var watcher = harness.RoleFromPack(HostCatalogue.WatcherPack);
        var tooLong = new string('x', 1001);
        var tooMany = Enumerable.Range(0, HostTenancy.TenancyDirectory.MostIds + 1).Select(_ => SeatId.CreateSequential()).ToArray();

        async Task<IReadOnlyDictionary<string, object?>> Refusing(string code, Func<Harness, Task> act)
            => (await Refused.WithCodeAsync(code, () => harness.BySystemWork(act))).Arguments;

        // The tenants' directory answers an operator, who is the toolkit's caller and nobody to Tenancy.
        harness.Options.OperatorTokenRoles.Add("operator");
        async Task<IReadOnlyDictionary<string, object?>> RefusingAnOperator(string code, Func<Harness, Task> act)
        {
            using (Callers.Begin(Caller.User(Guid.NewGuid(), "operator")))
            {
                return (await Refused.WithCodeAsync(code, () => harness.Run(HostCaller.Nobody(TenancyRefusals.NotSeated), act))).Arguments;
            }
        }

        // Each code through a use case, as a caller meets it. A name is refused under one code for six inputs.
        var thrown = new List<(string Code, string Field, IReadOnlyDictionary<string, object?> Arguments)>
        {
            (TenancyRefusals.NameInvalid, "name", await Refusing(TenancyRefusals.NameInvalid, h => h.Tenants.RenameOrganizationAsync(" ", default))),
            (TenancyRefusals.NameInvalid, "name", await Refusing(TenancyRefusals.NameInvalid, h => h.Organization.RenameUnitAsync(harness.Harbor.North, " ", default))),
            (TenancyRefusals.NameInvalid, "displayName", await Refusing(TenancyRefusals.NameInvalid, h => h.Seats.RenameAsync(ada, " ", default))),
            (TenancyRefusals.NameInvalid, "name", await Refusing(TenancyRefusals.NameInvalid, h => h.Roles.CreateAsync(" ", "Files widgets", [], default))),
            (TenancyRefusals.NameInvalid, "description", await Refusing(TenancyRefusals.NameInvalid, h => h.Roles.CreateAsync("Clerk", tooLong, [], default))),
            (TenancyRefusals.NameInvalid, "reason", await Refusing(TenancyRefusals.NameInvalid, h => h.Seats.GrantAsync(ada, root, watcher, until: null, tooLong, default))),
            (TenancyRefusals.NameInvalid, "reason", await Refusing(TenancyRefusals.NameInvalid, h => h.Tenants.SuspendAsync(tooLong, default))),
            (TenancyRefusals.InvalidPeriod, "until", await Refusing(TenancyRefusals.InvalidPeriod, h => h.Seats.GrantAsync(ada, root, watcher, until: h.Clock.Now, reason: null, default))),
            (TenancyRefusals.ReasonRequired, "reason", await Refusing(TenancyRefusals.ReasonRequired, h => h.Tenants.CloseAsync(" ", default))),
            (TenancyRefusals.UnknownPermission, "keys", await Refusing(TenancyRefusals.UnknownPermission, h => h.Roles.CreateAsync("Clerk", "Files widgets", ["widget.polish"], default))),
            (TenancyRefusals.IdentityRequired, "identity", await Refusing(TenancyRefusals.IdentityRequired, h => h.Seats.AddSeatAsync(Guid.Empty, "Bert", default))),
            (TenancyRefusals.TooManyIds, "ids", await Refusing(TenancyRefusals.TooManyIds, h => h.Directory.SeatsByIdAsync(tooMany, default))),
            (TenancyRefusals.PageSizeInvalid, "size", await RefusingAnOperator(TenancyRefusals.PageSizeInvalid, h => h.TenantDirectory.ListAsync(after: null, size: 0, default))),
            (TenancyRefusals.CursorInvalid, "after", await RefusingAnOperator(TenancyRefusals.CursorInvalid, h => h.TenantDirectory.ListAsync(after: "not a marker", size: 50, default))),
            (TenancyRefusals.AddressInvalid, "address", await Refusing(TenancyRefusals.AddressInvalid, h => h.Invitations.IssueAsync("nobody", root, watcher, null, null, null, default))),
            (TenancyRefusals.InvitationLifetime, "lifetime", await Refusing(TenancyRefusals.InvitationLifetime, h => h.Invitations.IssueAsync("wren@example.test", root, watcher, null, null, TimeSpan.FromMinutes(1), default))),
            (TenancyRefusals.InvitationGrantEndsFirst, "grantUntil", await Refusing(TenancyRefusals.InvitationGrantEndsFirst, h => h.Invitations.IssueAsync("wren@example.test", root, watcher, h.Clock.Now.AddDays(1), null, null, default))),
            (TenancyRefusals.NameInvalid, "displayName", await Refusing(TenancyRefusals.NameInvalid, h => h.Invitations.IssueAsync("wren@example.test", root, watcher, null, tooLong, null, default))),
        };

        // A slug is a value object, so a wrong one is a validation failure with the same code, and it names its input too.
        var slug = (await FluentActions.Awaiting(() => harness.Run(HostCaller.System, h => h.Tenants.ProvisionAsync(
                new HostTenancy.TenantToProvision("-wharf", "Wharf", TenantShape.Flat, "Wharf", Guid.NewGuid(), "Bert"), default)))
            .Should().ThrowAsync<InvalidValueObjectException>()).Which.Errors.Should().ContainSingle().Which;
        slug.Code.Should().Be(TenancyRefusals.InvalidSlug);
        thrown.Add((TenancyRefusals.InvalidSlug, "slug", slug.Arguments));

        // A role's keys are set from the catalogue's own expansion, so only the rule under it reports them.
        var role = New.Role(harness.Catalogue, "Clerk", HostCatalogue.WidgetRead);
        Break(role, nameof(HostRole.Keys), (IReadOnlyList<string>)[HostCatalogue.WidgetRead, HostCatalogue.WidgetChange]);
        thrown.Add((TenancyRefusals.KeysNotNormalized, "keys",
            role.GetInvariantViolations().Should().ContainSingle(violation => violation.Code == TenancyRefusals.KeysNotNormalized).Which.Arguments));

        var invalid = TenancyRefusals.Codes.Where(code => TenancyRefusals.KindOf(code) == RefusalKind.Invalid).ToArray();
        thrown.Select(refused => refused.Code).Distinct().Concat(AboutNoOneInput).Should().BeEquivalentTo(
            invalid,
            "a code of kind Invalid names the input it is about, in the argument Field, and is refused here to show it; "
            + "one that is about no input of a command is listed in AboutNoOneInput with the reason");

        foreach (var (code, field, arguments) in thrown)
        {
            arguments.TryGetValue(RefusalException.FieldArgument, out var named).Should().BeTrue(code + " names the input it is about");
            named.Should().Be(field, "that is the word the use cases call the input of " + code + " by");
        }

        thrown.Where(refused => refused.Code == TenancyRefusals.NameInvalid).Select(refused => refused.Arguments["What"]).Distinct()
            .Should().BeEquivalentTo(
                ["tenant-name", "unit-name", "display-name", "role-name", "role-description", "reason"],
                "every name the code refuses has been seen with its field");

        // The rule under a refusal reports the same arguments, so one translation and one form serve both.
        var seat = New.Seat();
        Break(seat, nameof(HostSeat.DisplayName), string.Empty);
        seat.GetInvariantViolations().Should().ContainSingle().Which.Arguments[RefusalException.FieldArgument].Should().Be("displayName");

        foreach (var code in AboutNoOneInput)
        {
            TenancyRefusals.Of(code).Arguments.Should().NotContainKey(RefusalException.FieldArgument, code + " is about no input of a command");
        }
    }

    [Fact]
    public void A_field_given_where_a_refusal_is_made_is_kept()
    {
        // An application that throws one of the package's codes for an input of its own says so.
        TenancyRefusals.Of(TenancyRefusals.InvalidPeriod).Arguments[RefusalException.FieldArgument].Should().Be("until");
        TenancyRefusals.Of(TenancyRefusals.InvalidPeriod, (RefusalException.FieldArgument, "endsOn"))
            .Arguments[RefusalException.FieldArgument].Should().Be("endsOn");
        TenancyRefusals.Of(TenancyRefusals.RoleArchived).Arguments.Should().NotContainKey(RefusalException.FieldArgument, "a conflict is about the state, not about an input");
    }

    [Fact]
    public void No_two_invariants_of_a_parent_share_a_code()
    {
        var parents = new[] { typeof(HostTenant), typeof(HostOrganization), typeof(HostUnit), typeof(HostSeat), typeof(HostRole), typeof(HostInvitation) }
            .Select(host => host.BaseType!);

        foreach (var parent in parents)
        {
            var codes = parent.GetGenericTypeDefinition().GetNestedTypes()
                .Where(nested => nested.GetInterfaces().Any(@interface => @interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IInvariant<>)))
                .Select(nested => nested.MakeGenericType(parent.GetGenericArguments()))
                .Select(rule => (string)rule.GetProperty(nameof(IInvariant<object>.Code))!.GetValue(Activator.CreateInstance(rule))!)
                .ToArray();

            codes.Should().NotBeEmpty(parent.Name + " states rules of its own");
            codes.Should().OnlyHaveUniqueItems(parent.Name + "'s rules are told apart by their codes");
            codes.Should().BeSubsetOf(TenancyRefusals.Codes, "an invariant reports the code its refusal does");
        }
    }

    /// <summary>Puts a parent's private property in a state its methods never would, to see the rule catch it.</summary>
    private static void Break(object aggregate, string property, object? value)
        => aggregate.GetType().BaseType!.GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!
            .GetSetMethod(nonPublic: true)!.Invoke(aggregate, [value]);

    /// <summary>The names a template fills, without their formats: <c>{Max}</c> and <c>{Max:N0}</c> are both <c>Max</c>.</summary>
    private static string[] Placeholders(string template)
        => [.. Placeholder().Matches(template).Select(match => match.Groups["name"].Value).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The words of a text in lower case, without its placeholders: letters only, so <c>id's</c> is <c>id</c> and <c>s</c>.</summary>
    private static string[] WordsOf(string text)
        => [.. Word().Matches(Placeholder().Replace(text, " ")).Select(match => match.Value.ToLowerInvariant())];

    [GeneratedRegex(@"\{(?<name>[A-Za-z][A-Za-z0-9]*)(:[^}]*)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\p{L}+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();
}
