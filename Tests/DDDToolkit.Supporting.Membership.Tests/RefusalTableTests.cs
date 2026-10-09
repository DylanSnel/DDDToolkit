using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;
using DDDToolkit.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// Every rule the package refuses by has one name, one kind and one English text, written in one table; the
/// package's resx holds the same English text and a Dutch translation of each; a resource refuses under codes
/// of its own, and its readers still get those texts in their language.
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

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The names declared as constants on <see cref="MembershipRefusals"/>.</summary>
    private static IReadOnlyList<string> DeclaredNames()
        => typeof(MembershipRefusals).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

    private static Dictionary<string, string> Resx(CultureInfo culture)
    {
        var resources = new ResourceManager(typeof(MembershipFailures).FullName!, typeof(MembershipFailures).Assembly);
        var set = resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!;
        return set.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    /// <summary>The localizer of an application that added the package's texts for these resources, as an application does.</summary>
    private static IFailureLocalizer Localizer(params MembershipCodes[] resources)
    {
        var services = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance).AddLocalization();
        foreach (var codes in resources)
        {
            services.AddDDDToolkitLocalization(texts => texts.AddResource<MembershipFailures>(codes.TextKeys));
        }

        return services.BuildServiceProvider().GetRequiredService<IFailureLocalizer>();
    }

    // ---------------------------------------------------------------- the table

    [Fact]
    public void Every_rule_has_a_name_a_kind_and_an_english_text()
    {
        var declared = DeclaredNames();

        MembershipRefusals.Codes.Should().BeEquivalentTo(declared, "every constant is in the table and the table has nothing else");
        declared.Should().OnlyHaveUniqueItems().And.OnlyContain(code => code.StartsWith("membership.", StringComparison.Ordinal));
        declared.Should().HaveCount(17, "the eleven rules of access and of a member list, the one about a period, and the five about a role kept for a resource");

        var english = Resx(CultureInfo.InvariantCulture);
        english.Keys.Should().BeEquivalentTo(declared, "MembershipFailures.resx holds exactly the table's names");

        foreach (var code in declared)
        {
            Enum.IsDefined(MembershipRefusals.KindOf(code)).Should().BeTrue();
            MembershipRefusals.TemplateOf(code).Should().NotBeNullOrWhiteSpace().And.EndWith(".");
            english[code].Should().Be(MembershipRefusals.TemplateOf(code), "the resx's English is the table's, for " + code);
        }

        FluentActions.Invoking(() => MembershipRefusals.KindOf("membership.nothing-like-it")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => MembershipRefusals.TemplateOf("documents.not-found")).Should().Throw<ArgumentException>("a resource's code is not the rule's name");
    }

    [Fact]
    public void Each_rule_is_the_kind_of_refusal_an_edge_answers_it_with()
    {
        var kinds = MembershipRefusals.Codes.ToDictionary(code => code, MembershipRefusals.KindOf);

        kinds.Should().BeEquivalentTo(new Dictionary<string, RefusalKind>
        {
            [MembershipRefusals.NotFound] = RefusalKind.NotFound,
            [MembershipRefusals.NotPermitted] = RefusalKind.NotPermitted,
            [MembershipRefusals.MemberNotActive] = RefusalKind.Conflict,
            [MembershipRefusals.RoleNotForMembers] = RefusalKind.Invalid,
            [MembershipRefusals.NoOwnerRole] = RefusalKind.Conflict,
            [MembershipRefusals.AlreadyMember] = RefusalKind.Conflict,
            [MembershipRefusals.MemberNotFound] = RefusalKind.NotFound,
            [MembershipRefusals.RoleHeld] = RefusalKind.Conflict,
            [MembershipRefusals.RoleNotHeld] = RefusalKind.NotFound,
            [MembershipRefusals.OwnerProtected] = RefusalKind.Conflict,
            [MembershipRefusals.AlreadyOwner] = RefusalKind.Conflict,
            [MembershipRefusals.InvalidPeriod] = RefusalKind.Invalid,
            [MembershipRefusals.RoleIsArchived] = RefusalKind.Conflict,
            [MembershipRefusals.RoleNameInvalid] = RefusalKind.Invalid,
            [MembershipRefusals.KeyNotForMembers] = RefusalKind.Invalid,
            [MembershipRefusals.OwnerRoleStays] = RefusalKind.Conflict,
            [MembershipRefusals.RoleKeysNotNormalized] = RefusalKind.Conflict,
        });
    }

    // ---------------------------------------------------------------- a resource's own codes

    [Fact]
    public void A_resource_refuses_each_rule_under_its_own_prefix()
    {
        var codes = MembershipCodes.Under("documents");

        codes.Prefix.Should().Be("documents");
        codes.All.Should().Equal(MembershipRefusals.Codes.Select(code => code.Replace("membership.", "documents.", StringComparison.Ordinal)));
        codes[MembershipRefusals.AlreadyMember].Should().Be("documents.already-member");

        foreach (var rule in MembershipRefusals.Codes)
        {
            var refusal = codes.Refuse(rule);
            refusal.Code.Should().Be(codes[rule]);
            refusal.Kind.Should().Be(MembershipRefusals.KindOf(rule), "the kind is the rule's, whatever a resource calls it");
        }

        // The package's own names are what a resource named "membership" would refuse with: nothing is special about them.
        MembershipCodes.Under("membership").All.Should().Equal(MembershipRefusals.Codes);
        MembershipCodes.Under("sales.documents")[MembershipRefusals.NotFound].Should().Be("sales.documents.not-found");

        FluentActions.Invoking(() => codes["documents.already-member"]).Should().Throw<ArgumentException>("a rule is named by the package's constant");
        FluentActions.Invoking(() => codes.Refuse("documents.already-member")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => codes[null!]).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Documents")]
    [InlineData("my documents")]
    [InlineData("documents.")]
    [InlineData(".documents")]
    [InlineData("7documents")]
    [InlineData("documents_x")]
    public void A_prefix_that_cannot_start_a_code_is_refused(string prefix)
    {
        FluentActions.Invoking(() => MembershipCodes.Under(prefix)).Should().Throw<ArgumentException>().WithMessage("*cannot start a refusal code*");
    }

    [Fact]
    public void A_code_map_keeps_a_resources_own_words_for_a_rule()
    {
        var plain = MembershipCodes.Under("projects");
        var own = plain
            .With(MembershipRefusals.AlreadyMember, "already-on-crew")
            .With(MembershipRefusals.MemberNotActive, "seat-not-active");

        own[MembershipRefusals.AlreadyMember].Should().Be("projects.already-on-crew");
        own[MembershipRefusals.MemberNotActive].Should().Be("projects.seat-not-active");
        own[MembershipRefusals.OwnerProtected].Should().Be("projects.owner-protected", "a rule nobody renamed keeps the package's name");
        own.All.Should().HaveCount(17).And.OnlyHaveUniqueItems();
        own.Refuse(MembershipRefusals.AlreadyMember).Code.Should().Be("projects.already-on-crew");

        // A map never changes: renaming answers another one.
        plain[MembershipRefusals.AlreadyMember].Should().Be("projects.already-member");

        // Renamed to what it was already called, or back: fine.
        own.With(MembershipRefusals.AlreadyMember, "already-on-crew")[MembershipRefusals.AlreadyMember].Should().Be("projects.already-on-crew");
        own.With(MembershipRefusals.AlreadyMember, "already-member").All.Should().Contain("projects.already-member");
    }

    [Fact]
    public void A_code_map_refuses_what_a_client_could_not_tell_apart_or_look_up()
    {
        var codes = MembershipCodes.Under("projects");

        // Two rules under one code.
        FluentActions.Invoking(() => codes.With(MembershipRefusals.RoleHeld, "already-member"))
            .Should().Throw<ArgumentException>().WithMessage("*'projects.already-member' is this resource's code for membership.already-member already*");

        FluentActions.Invoking(() => codes.With("projects.already-member", "x")).Should().Throw<ArgumentException>("the rule is named by the package's constant");
        foreach (var suffix in new[] { "", "Already", "already member", "tenancy.invalid-period", "9lives" })
        {
            FluentActions.Invoking(() => codes.With(MembershipRefusals.AlreadyMember, suffix)).Should().Throw<ArgumentException>("'" + suffix + "' cannot end a code");
        }
    }

    [Fact]
    public void A_code_map_keeps_a_resources_own_word_for_a_member_in_what_a_refusal_carries()
    {
        var plain = MembershipCodes.Under("projects");
        var own = plain.WithMemberArgument("Seat");
        var seat = UserId.CreateSequential();

        (plain.MemberArgument, own.MemberArgument).Should().Be((MembershipCodes.DefaultMemberArgument, "Seat"));
        MembershipCodes.DefaultMemberArgument.Should().Be("Member");

        // Whoever refuses names the member in the package's word, and the resource's map carries it in its own.
        var refusal = own.Refuse(MembershipRefusals.RoleHeld, (MembershipCodes.DefaultMemberArgument, seat), ("Role", new NamedRole("contributor")));
        refusal.Arguments.Keys.Should().BeEquivalentTo("Seat", "Role");
        refusal.Arguments["Seat"].Should().Be(seat);
        refusal.Message.Should().Be(plain.Refuse(MembershipRefusals.RoleHeld).Message, "the text is the rule's, whatever the member is called");

        // Nothing else is renamed, and a refusal that names no member carries none.
        own.Refuse(MembershipRefusals.NotPermitted, ("Key", "projects.edit")).Arguments.Keys.Should().Equal("Key");
        own.Refuse(MembershipRefusals.OwnerProtected).Arguments.Should().BeEmpty();
        own.Refuse(MembershipRefusals.InvalidPeriod).Arguments.Keys.Should().Equal(RefusalException.FieldArgument);
        own.All.Should().Equal(plain.All, "the codes are the same: only the argument is called otherwise");

        // A map never changes, and the word stays through a rule renamed afterwards.
        plain.Refuse(MembershipRefusals.AlreadyMember, (MembershipCodes.DefaultMemberArgument, seat)).Arguments.Keys.Should().Equal("Member");
        var both = own.With(MembershipRefusals.AlreadyMember, "already-on-crew");
        both.MemberArgument.Should().Be("Seat");
        both.Refuse(MembershipRefusals.AlreadyMember, (MembershipCodes.DefaultMemberArgument, seat)).Arguments.Keys.Should().Equal("Seat");

        // Called by the package's word in another case, it is still the one argument.
        MembershipCodes.Under("projects").WithMemberArgument("member").Refuse(MembershipRefusals.AlreadyMember, (MembershipCodes.DefaultMemberArgument, seat))
            .Arguments.Keys.Should().Equal("member");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("9lives")]
    [InlineData("my seat")]
    [InlineData("seat-id")]
    [InlineData("{Seat}")]
    public void A_word_that_cannot_name_an_argument_is_refused(string name)
    {
        FluentActions.Invoking(() => MembershipCodes.Under("projects").WithMemberArgument(name))
            .Should().Throw<ArgumentException>().WithParameterName("name").WithMessage("*cannot name an argument of a refusal*");
    }

    [Theory]
    [InlineData("Role")]
    [InlineData("key")]
    [InlineData("Keys")]
    [InlineData("max")]
    [InlineData("Field")]
    public void The_member_is_not_carried_under_the_name_of_another_argument(string name)
    {
        // Two values under one name: a client would read the role, the key or the input where it expects the member.
        FluentActions.Invoking(() => MembershipCodes.Under("projects").WithMemberArgument(name))
            .Should().Throw<ArgumentException>().WithParameterName("name").WithMessage("*is the name of another argument a refusal carries*");
        FluentActions.Invoking(() => MembershipCodes.Under("projects").WithMemberArgument(null!)).Should().Throw<ArgumentNullException>();
    }

    // ---------------------------------------------------------------- the texts

    [Fact]
    public void Every_rule_has_a_dutch_text()
    {
        var localizer = Localizer(MembershipCodes.Under("membership"));

        FailureTranslations.Check(localizer, "en", "nl").Codes(MembershipRefusals.Codes).Verify();
        Resx(CultureInfo.GetCultureInfo("nl")).Keys.Should().BeEquivalentTo(MembershipRefusals.Codes, "the Dutch resx has every name and no other");
    }

    [Fact]
    public void An_entry_of_one_resource_file_is_an_entry_of_the_other_with_the_same_placeholders()
    {
        var english = Resx(CultureInfo.InvariantCulture);
        var dutch = Resx(CultureInfo.GetCultureInfo("nl"));

        english.Keys.Except(dutch.Keys).Should().BeEmpty(
            "every entry of MembershipFailures.resx needs a Dutch text in MembershipFailures.nl.resx, under the same name");
        dutch.Keys.Except(english.Keys).Should().BeEmpty(
            "every entry of MembershipFailures.nl.resx needs an English text in MembershipFailures.resx, under the same name");

        foreach (var (name, text) in dutch)
        {
            text.Should().NotBeNullOrWhiteSpace("the Dutch text of " + name + " says something");
            english[name].Should().NotBeNullOrWhiteSpace("the English text of " + name + " says something");
            text.Should().NotBe(english[name], "the Dutch text of " + name + " is Dutch");
            text.Should().EndWith(".");
            Placeholders(text).Should().BeEquivalentTo(
                Placeholders(english[name]),
                "the Dutch text of " + name + " is filled from the same arguments as the English one: a placeholder nobody fills is shown as written");
        }
    }

    [Fact]
    public void No_dutch_text_uses_a_word_for_the_reader()
    {
        var addressing = Resx(CultureInfo.GetCultureInfo("nl"))
            .Select(entry => (entry.Key, Text: entry.Value, Words: WordsOf(entry.Value).Intersect(WordsForTheReader).ToArray()))
            .Where(entry => entry.Words.Length > 0)
            .Select(entry => entry.Key + " says \"" + string.Join("\", \"", entry.Words) + "\": " + entry.Text)
            .ToArray();

        addressing.Should().BeEmpty(
            "a Dutch text of the package has no word for the reader, neither the familiar nor the formal one. Say what "
            + "is the case or what is needed instead: \"Hiervoor is het recht {Key} nodig.\", not \"Je mist het recht {Key}.\"");

        // The check itself: it finds the words as words, in any case, and not inside other words or placeholders.
        WordsOf("Je mist het recht {U}; vraag uw eigenaar, of U zelf.").Intersect(WordsForTheReader).Should().Equal("je", "uw", "u");
        WordsOf("Deze uitleg juicht, en een menu is geen jeu; id's ook niet.").Intersect(WordsForTheReader).Should().BeEmpty();
    }

    [Fact]
    public void No_text_says_what_the_resource_or_its_members_are_called()
    {
        // The package's words are member, role and owner. What a resource is, and what it calls its members, is
        // the application's to say in a resx of its own.
        string[] someonesWords = ["document", "folder", "project", "crew", "seat", "tenant", "user", "ploeg", "plaats", "gebruiker"];

        foreach (var texts in new[] { Resx(CultureInfo.InvariantCulture), Resx(CultureInfo.GetCultureInfo("nl")) })
        {
            texts.Should().OnlyContain(entry => !WordsOf(entry.Value).Intersect(someonesWords).Any());
        }
    }

    [Fact]
    public void Every_invalid_refusal_names_the_input_it_is_about()
    {
        var document = new Document(DocumentId.CreateSequential(), "Minutes", UserId.CreateSequential(), DocumentMembership.Owner, Now);
        var codes = DocumentRefusals.Membership;

        // Each one refused the way a caller meets it.
        var thrown = new List<(string Rule, string Field, IReadOnlyDictionary<string, object?> Arguments)>
        {
            (MembershipRefusals.InvalidPeriod, "until",
                Refused.With(codes, MembershipRefusals.InvalidPeriod, () => document.ShareWith(UserId.CreateSequential(), MemberPeriod.Between(Now, Now), Now, by: null)).Arguments),
            (MembershipRefusals.RoleNotForMembers, "role",
                Refused.With(codes, MembershipRefusals.RoleNotForMembers, () => new MemberAdmission<DocumentId, UserId, NamedRole>(DocumentMembership.Rules, new NamedRoles<DocumentId>(DocumentMembership.Rules))
                    .RequireRoleAsync(new NamedRole("auditor"), CancellationToken.None).AsTask().GetAwaiter().GetResult()).Arguments),

            // A role kept for a resource, refused under that resource's codes: its name, and the keys it would give.
            (MembershipRefusals.RoleNameInvalid, "name",
                Refused.With(PlotMembership.Codes, MembershipRefusals.RoleNameInvalid, () => _ = new PlotRole(PlotRoleId.CreateSequential(), GardenId.CreateSequential(), new KeptRoleDraft("  ", null, []))).Arguments),
            (MembershipRefusals.KeyNotForMembers, "keys",
                Refused.With(PlotMembership.Codes, MembershipRefusals.KeyNotForMembers, () => _ = new PlotRole(PlotRoleId.CreateSequential(), GardenId.CreateSequential(), new KeptRoleDraft("Seller", null, [PlotKeys.Sell]))).Arguments),
        };

        var invalid = MembershipRefusals.Codes.Where(code => MembershipRefusals.KindOf(code) == RefusalKind.Invalid).ToArray();
        thrown.Select(refused => refused.Rule).Should().BeEquivalentTo(
            invalid,
            "a rule of kind Invalid names the input it is about, in the argument Field, and is refused here to show it");

        foreach (var (rule, field, arguments) in thrown)
        {
            arguments.TryGetValue(RefusalException.FieldArgument, out var named).Should().BeTrue(rule + " names the input it is about");
            named.Should().Be(field, "that is the word the member list calls the input of " + rule + " by");
        }

        // A field given where a refusal is made is kept: an application that refuses with a rule for an input of its own says so.
        codes.Refuse(MembershipRefusals.InvalidPeriod, (RefusalException.FieldArgument, "endsOn")).Arguments[RefusalException.FieldArgument].Should().Be("endsOn");

        // And no other kind of refusal names one: a conflict is about the state, not about an input.
        foreach (var rule in MembershipRefusals.Codes.Except(invalid))
        {
            codes.Refuse(rule).Arguments.Should().NotContainKey(RefusalException.FieldArgument, rule + " is about no one input");
        }
    }

    [Fact]
    public void A_refusal_carries_its_arguments_and_an_english_text_filled_from_them()
    {
        var refusal = DocumentRefusals.Membership.Refuse(MembershipRefusals.NotPermitted, ("Key", DocumentKeys.Share));

        refusal.Message.Should().Be("Doing this needs the key documents.share.");
        refusal.Arguments.Should().Contain("Key", DocumentKeys.Share);
        refusal.Arguments.Should().ContainKey("key", "names are matched without regard to case, like the arguments of every failure");

        // An argument nobody gave is left as written, rather than shown as nothing.
        DocumentRefusals.Membership.Refuse(MembershipRefusals.NotPermitted).Message.Should().Be("Doing this needs the key {Key}.");
    }

    // ---------------------------------------------------------------- read in the onlooker's language

    [Fact]
    public void A_refusal_reads_in_the_readers_language_under_the_resources_own_code()
    {
        var localizer = Localizer(DocumentRefusals.Membership, FolderRefusals.Membership);
        var shared = DocumentRefusals.Membership.Refuse(MembershipRefusals.NotPermitted, ("Key", DocumentKeys.Share));
        var staffed = FolderRefusals.Membership.Refuse(MembershipRefusals.AlreadyMember, ("Member", new StaffCode("C-014")));

        localizer.Localize(shared, CultureInfo.GetCultureInfo("nl-NL")).Should().Be("Hiervoor is het recht documents.share nodig.");
        localizer.Localize(shared, CultureInfo.GetCultureInfo("en-GB")).Should().Be(shared.Message).And.Be("Doing this needs the key documents.share.");

        // A code in the resource's own words reads the text of the rule it stands for.
        staffed.Code.Should().Be("folders.already-on-folder");
        localizer.Localize(staffed, CultureInfo.GetCultureInfo("nl")).Should().Be("Dit is al een lid, of wordt het nog. Geef het lid een rol.");

        // Every code of both resources, in both languages, with nothing falling back.
        FailureTranslations.Check(localizer, "en", "nl")
            .Codes(DocumentRefusals.Membership.All)
            .Codes(FolderRefusals.Membership.All)
            .Verify();
    }

    [Fact]
    public void A_resource_whose_texts_were_not_added_keeps_the_english_text_of_its_refusals()
    {
        // Only the documents' texts were added: a folder's refusal is nobody's to translate, and says so in English.
        var localizer = Localizer(DocumentRefusals.Membership);
        var staffed = FolderRefusals.Membership.Refuse(MembershipRefusals.OwnerProtected);

        localizer.Localize(staffed, CultureInfo.GetCultureInfo("nl")).Should().Be(staffed.Message);
        localizer.Localize(DocumentRefusals.Membership.Refuse(MembershipRefusals.OwnerProtected), CultureInfo.GetCultureInfo("nl"))
            .Should().Be("De eigenaar blijft lid, in de rol van eigenaar, tot iemand anders eigenaar is.");
    }

    [Fact]
    public void The_text_keys_of_a_resource_are_its_own_codes_and_the_one_rule_a_member_reports_itself()
    {
        var keys = FolderRefusals.Membership.TextKeys;

        keys.Should().HaveCount(20, "seventeen codes of its own, and the three rules a member and a role report under the package's names");
        keys["folders.already-on-folder"].Should().Be(MembershipRefusals.AlreadyMember);
        keys["folders.owner-protected"].Should().Be(MembershipRefusals.OwnerProtected);
        keys[MembershipRefusals.RoleHeld].Should().Be(MembershipRefusals.RoleHeld, "a member does not know whose it is, and reports the rule by the package's name");
        keys[MembershipRefusals.RoleNameInvalid].Should().Be(MembershipRefusals.RoleNameInvalid, "neither does a role kept for a resource, for the two rules it checks itself");
        keys[MembershipRefusals.RoleKeysNotNormalized].Should().Be(MembershipRefusals.RoleKeysNotNormalized);
        keys["folders.role-name-invalid"].Should().Be(MembershipRefusals.RoleNameInvalid, "refused where a role is named, the same rule carries the resource's code");
        keys.Values.Distinct().Should().BeEquivalentTo(MembershipRefusals.Codes, "every text is read by a code");

        // A resource named as the package names its rules needs no entry twice.
        MembershipCodes.Under("membership").TextKeys.Should().HaveCount(17);
    }

    [Fact]
    public void The_rule_a_member_checks_itself_reads_in_the_readers_language_too()
    {
        var localizer = Localizer(DocumentRefusals.Membership);
        var document = new Document(DocumentId.CreateSequential(), "Minutes", UserId.CreateSequential(), DocumentMembership.Owner, Now);
        var member = document.ShareWith(UserId.CreateSequential(), DocumentMembership.Contributor, MemberPeriod.Open(Now), Now, by: null);
        var roles = Break.ListOf<MemberRole<UserId, NamedRole>>(member, "_roles");
        roles.Add(new Document(DocumentId.CreateSequential(), "Other", UserId.CreateSequential(), DocumentMembership.Contributor, Now).Shares.Single().Roles.Single());

        var violation = document.GetInvariantViolations().Should().ContainSingle().Which;

        violation.Code.Should().Be(MembershipRefusals.RoleHeld);
        using (CultureScope.Use("nl-NL"))
        {
            localizer.Localize(violation).Should().Be("Dat lid heeft deze rol al.");
        }

        using (CultureScope.Use("en-GB"))
        {
            localizer.Localize(violation).Should().Be(violation.Message).And.Be("That member already has this role.");
        }
    }

    [Fact]
    public void No_two_rules_of_the_member_class_share_a_code()
    {
        foreach (var parent in new[] { typeof(DocumentShare).BaseType!, typeof(FolderMember).BaseType! })
        {
            var codes = parent.GetGenericTypeDefinition().GetNestedTypes()
                .Where(nested => nested.GetInterfaces().Any(@interface => @interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IInvariant<>)))
                .Select(nested => nested.MakeGenericType(parent.GetGenericArguments()))
                .Select(rule => (string)rule.GetProperty(nameof(IInvariant<object>.Code))!.GetValue(Activator.CreateInstance(rule))!)
                .ToArray();

            codes.Should().Equal([MembershipRefusals.RoleHeld], "a member states the one rule it can tell by itself");
            codes.Should().BeSubsetOf(MembershipRefusals.Codes, "a rule reports the name its refusal has");
        }
    }

    [Fact]
    public void The_refusals_of_a_role_and_the_rules_it_checks_itself_read_in_the_readers_language()
    {
        var localizer = Localizer(PlotMembership.Codes);
        var meadow = GardenId.CreateSequential();

        // Refused where a role is given keys, under the plots' own code.
        var refused = Refused.With(PlotMembership.Codes, MembershipRefusals.KeyNotForMembers, () => _ = new PlotRole(PlotRoleId.CreateSequential(), meadow, new KeptRoleDraft("Seller", null, [PlotKeys.Sell])));
        refused.Code.Should().Be("plots.key-not-for-members");
        localizer.Localize(refused, CultureInfo.GetCultureInfo("nl-NL")).Should().Be("Deze rechten kan een rol hier niet geven: plots.sell.");
        localizer.Localize(refused, CultureInfo.GetCultureInfo("en-GB")).Should().Be(refused.Message).And.Be("A role cannot give these keys here: plots.sell.");

        var archived = PlotMembership.Codes.Refuse(MembershipRefusals.RoleIsArchived);
        localizer.Localize(archived, CultureInfo.GetCultureInfo("nl")).Should().Be("Die rol is gearchiveerd.");
        localizer.Localize(PlotMembership.Codes.Refuse(MembershipRefusals.OwnerRoleStays), CultureInfo.GetCultureInfo("nl")).Should().Be("De rol die elke eigenaar heeft, kan niet worden gearchiveerd.");

        // What a role checks by itself it reports under the package's name for the rule, and that reads in the reader's language too.
        var role = new PlotRole(PlotRoleId.CreateSequential(), meadow, new KeptRoleDraft("Fencer", null, [PlotKeys.See]));
        Break.Set(role, nameof(PlotRole.Name), string.Empty);
        var violation = role.GetInvariantViolations().Should().ContainSingle().Which;
        violation.Code.Should().Be(MembershipRefusals.RoleNameInvalid);
        using (CultureScope.Use("nl-NL"))
        {
            localizer.Localize(violation).Should().Be("Vul 1 tot 120 tekens in.");
        }

        using (CultureScope.Use("en-GB"))
        {
            localizer.Localize(violation).Should().Be(violation.Message).And.Be("Enter 1 to 120 characters.");
        }
    }

    [Fact]
    public void No_two_rules_of_the_role_class_share_a_code()
    {
        foreach (var parent in new[] { typeof(PlotRole).BaseType!, typeof(ShedRole).BaseType! })
        {
            var codes = parent.GetGenericTypeDefinition().GetNestedTypes()
                .Where(nested => nested.GetInterfaces().Any(@interface => @interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IInvariant<>)))
                .Select(nested => nested.MakeGenericType(parent.GetGenericArguments()))
                .Select(rule => (string)rule.GetProperty(nameof(IInvariant<object>.Code))!.GetValue(Activator.CreateInstance(rule))!)
                .ToArray();

            codes.Should().BeEquivalentTo([MembershipRefusals.RoleNameInvalid, MembershipRefusals.RoleKeysNotNormalized], "a role states the two rules it can tell by itself");
            codes.Should().BeSubsetOf(MembershipRefusals.Codes, "a rule reports the name its refusal has");
        }
    }

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
