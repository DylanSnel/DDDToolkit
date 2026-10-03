using DDDToolkit.Exceptions;
using DDDToolkit.Localization;
using Microsoft.Extensions.Localization;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// The texts of what a resource refuses with, in English and Dutch, reach an application that phrases its
/// failures in the reader's language without a line of its own: the resource's registration offers them, under
/// that resource's codes. An application that does not localize its failures is given nothing.
/// </summary>
public sealed class RefusalTextsTests
{
    /// <summary>The services of a host that registered its two resources, and what <paramref name="more"/> adds.</summary>
    private static ServiceProvider Host(Action<IServiceCollection>? more = null)
    {
        var services = new ServiceCollection();
        FilingHost.Add(services);
        more?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registering_a_resource_offers_its_texts_in_both_languages_under_that_resources_codes()
    {
        // All the host says about texts is that it localizes its failures.
        using var provider = Host(services => services.AddDDDToolkitLocalization());
        var localizer = provider.GetRequiredService<IFailureLocalizer>();
        var user = UserId.CreateUnique();

        var shared = DocumentRefusals.Membership.Of(MembershipRefusals.AlreadyMember, (MembershipCodes.DefaultMemberArgument, user));
        shared.Code.Should().Be("documents.already-member");

        using (CultureScope.Use("nl-NL"))
        {
            localizer.Localize(shared).Should().Be("Dit is al een lid, of wordt het nog. Geef het lid een rol.");
        }

        using (CultureScope.Use("en-GB"))
        {
            localizer.Localize(shared).Should().Be("That one is a member already, or is due to become one. Give the member a role instead.");
        }

        // Every code of both resources, and the names of the rules a member and a role check by themselves.
        FailureTranslations.Check(localizer, "en", "nl")
            .Codes([.. DocumentRefusals.Membership.TextKeys.Keys, .. FolderRefusals.Membership.TextKeys.Keys])
            .Verify();
    }

    [Fact]
    public void A_resource_that_calls_a_rule_otherwise_reads_the_rules_text_under_its_own_code()
    {
        using var provider = Host(services => services.AddDDDToolkitLocalization());
        var localizer = provider.GetRequiredService<IFailureLocalizer>();

        // A folder has a code of its own for the rule, and a document's code for it is not a folder's.
        var onFolder = FolderRefusals.Membership.Of(MembershipRefusals.AlreadyMember, (MembershipCodes.DefaultMemberArgument, new StaffCode("S-1")));
        onFolder.Code.Should().NotBe("folders.already-member", "the host's folders call the rule otherwise");

        using (CultureScope.Use("nl-NL"))
        {
            localizer.Localize(onFolder).Should().Be("Dit is al een lid, of wordt het nog. Geef het lid een rol.");
            localizer.Localize(new RefusalException("folders.already-member", RefusalKind.Conflict, "Own sentence."))
                .Should().Be("Own sentence.", "a code no resource has is nobody's text");
        }
    }

    [Fact]
    public void A_text_of_the_hosts_own_for_a_resources_code_is_the_one_a_reader_gets()
    {
        // Added after the resources were registered, and still asked first: the package answers only what the host does not.
        using var provider = Host(services => services.AddDDDToolkitLocalization(texts => texts.AddLocalizer(new OwnWords())));
        var localizer = provider.GetRequiredService<IFailureLocalizer>();

        localizer.Localize(DocumentRefusals.Membership.Of(MembershipRefusals.AlreadyMember, (MembershipCodes.DefaultMemberArgument, UserId.CreateUnique())))
            .Should().Be("Already shared with them.");
        using (CultureScope.Use("en-GB"))
        {
            localizer.Localize(DocumentRefusals.Membership.Of(MembershipRefusals.OwnerProtected)).Should().NotBe("Already shared with them.").And.NotBeEmpty();
        }
    }

    [Fact]
    public void A_host_that_does_not_localize_its_failures_is_given_no_localizer()
    {
        using var provider = Host();

        provider.GetService<IFailureLocalizer>().Should().BeNull("registering a resource offers its texts, and turns nothing on");
    }

    [Fact]
    public void A_resource_offers_its_texts_once_however_often_it_is_registered()
    {
        var services = new ServiceCollection();
        FilingHost.Add(services);
        FilingHost.Add(services);

        var offers = services.Where(descriptor => descriptor.ServiceType == typeof(FailureTexts)).Select(descriptor => (FailureTexts)descriptor.ImplementationInstance!).ToList();
        offers.Should().HaveCount(2, "one for the documents and one for the folders");
        offers.Should().OnlyContain(offer => offer.ResourceSource == typeof(MembershipFailures));
        offers.Select(offer => offer.Entries!.Keys.Order(StringComparer.Ordinal))
            .Should().BeEquivalentTo(
                [DocumentRefusals.Membership.TextKeys.Keys.Order(StringComparer.Ordinal), FolderRefusals.Membership.TextKeys.Keys.Order(StringComparer.Ordinal)],
                "each under the codes of its own resource");
    }

    /// <summary>The host's own words for one code of its documents.</summary>
    private sealed class OwnWords : IStringLocalizer
    {
        private const string Code = "documents.already-member";

        public LocalizedString this[string name]
            => name == Code ? new LocalizedString(name, "Already shared with them.") : new LocalizedString(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [new LocalizedString(Code, "Already shared with them.")];
    }
}
