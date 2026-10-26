using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// The questions that refuse, over what a storage answers: what a caller is refused with, and in which order,
/// is decided once in the package, the same for every storage and under the codes of the resource asked about.
/// </summary>
public sealed class MemberQuestionsTests
{
    private static readonly DocumentId Minutes = DocumentId.CreateSequential();

    private static readonly DocumentId Hidden = DocumentId.CreateSequential();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_key_that_is_held_answers_the_resource_its_version_how_it_is_held_and_until_when()
    {
        var nextWeek = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
        var access = new StubAccess<DocumentId>(DocumentMembership.Rules) { Until = nextWeek }
            .Sees(Minutes, version: 4, (DocumentKeys.View, MemberVia.Members), (DocumentKeys.Edit, MemberVia.Members));

        var hold = await access.RequireAsync(Minutes, DocumentKeys.Edit, Cancellation);

        hold.Should().Be(new MemberHold<DocumentId>(Minutes, MemberVia.Members, 4, nextWeek));
        access.Asked.Should().Equal("caller", "hold " + DocumentKeys.Edit);

        // A hold with no end, such as an owner's, says so by naming none.
        access.Until = null;
        (await access.RequireAsync(Minutes, DocumentKeys.Edit, Cancellation)).Until.Should().BeNull();
    }

    [Fact]
    public async Task A_resource_the_caller_does_not_see_is_not_found_exactly_as_one_that_does_not_exist()
    {
        var access = new StubAccess<DocumentId>(DocumentMembership.Rules).Sees(Minutes, version: 4, (DocumentKeys.View, MemberVia.Members));

        var unseen = await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => access.RequireAsync(Hidden, DocumentKeys.Edit, Cancellation));
        var missing = await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => access.RequireAsync(DocumentId.CreateSequential(), DocumentKeys.View, Cancellation));

        // One answer for both, and nothing in it that tells them apart: nobody learns a resource is there.
        (unseen.Code, unseen.Kind, unseen.Message).Should().Be((missing.Code, missing.Kind, missing.Message));
        unseen.Code.Should().Be("documents.not-found");
        unseen.Kind.Should().Be(RefusalKind.NotFound);
        unseen.Arguments.Should().BeEmpty("neither the id nor the key is said back");
        missing.Arguments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_resource_the_caller_sees_without_holding_the_key_is_not_permitted_and_names_the_key()
    {
        var access = new StubAccess<DocumentId>(DocumentMembership.Rules).Sees(Minutes, version: 4, (DocumentKeys.View, MemberVia.Members));

        var refusal = await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotPermitted, () => access.RequireAsync(Minutes, DocumentKeys.Share, Cancellation));

        refusal.Code.Should().Be("documents.not-permitted");
        refusal.Kind.Should().Be(RefusalKind.NotPermitted);
        refusal.Arguments.Should().Contain("Key", DocumentKeys.Share).And.HaveCount(1);
        refusal.Message.Should().Be("Doing this needs the key documents.share.");
    }

    [Fact]
    public async Task A_caller_that_is_nobody_is_refused_as_nobody_before_anything_is_read()
    {
        var nobody = new RefusalException("host.not-signed-in", RefusalKind.NotPermitted, "Sign in first.");
        var access = new StubAccess<DocumentId>(DocumentMembership.Rules) { Nobody = nobody }.Sees(Minutes, version: 4, (DocumentKeys.View, MemberVia.Members));

        var refused = await FluentActions.Awaiting(() => access.RequireAsync(Minutes, DocumentKeys.View, Cancellation)).Should().ThrowAsync<RefusalException>();

        refused.Which.Should().BeSameAs(nobody, "the caller's own refusal comes first, as the caller was given it");
        access.Asked.Should().Equal(["caller"], "no statement is made for nobody");

        // The question that answers instead of refusing for want of a key starts the same way: nobody is not
        // told a resource was not found, as if it had been looked for.
        access.Asked.Clear();
        (await FluentActions.Awaiting(() => access.ViaAsync(Minutes, DocumentKeys.View, Cancellation)).Should().ThrowAsync<RefusalException>())
            .Which.Should().BeSameAs(nobody);
        (await FluentActions.Awaiting(() => access.ViaAsync(Hidden, DocumentKeys.View, Cancellation)).Should().ThrowAsync<RefusalException>())
            .Which.Should().BeSameAs(nobody);
        access.Asked.Should().Equal("caller", "caller");
    }

    [Fact]
    public async Task How_a_key_is_held_is_answered_for_a_resource_the_caller_sees_and_refused_only_for_one_it_does_not()
    {
        var access = new StubAccess<DocumentId>(DocumentMembership.Rules).Sees(Minutes, version: 4, (DocumentKeys.View, MemberVia.Members), (DocumentKeys.Edit, MemberVia.System));

        (await access.ViaAsync(Minutes, DocumentKeys.View, Cancellation)).Should().Be(MemberVia.Members);
        (await access.ViaAsync(Minutes, DocumentKeys.Edit, Cancellation)).Should().Be(MemberVia.System);
        (await access.ViaAsync(Minutes, DocumentKeys.Share, Cancellation)).Should().BeNull("not holding the key is an answer, not a refusal");

        var refusal = await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => access.ViaAsync(Hidden, DocumentKeys.View, Cancellation));
        refusal.Arguments.Should().BeEmpty();
        access.Asked.TakeLast(2).Should().Equal(["caller", "hold " + DocumentKeys.View], "the caller first, then one statement");
    }

    [Fact]
    public async Task Each_kind_of_resource_is_asked_through_its_own_questions_and_refused_under_its_own_codes()
    {
        var documents = new StubAccess<DocumentId>(DocumentMembership.Rules).Sees(Minutes, version: 1, (DocumentKeys.View, MemberVia.Members));
        var folders = new StubAccess<FolderId>(FolderMembership.Rules).Sees(new FolderId(7), version: 9, (FolderKeys.Read, MemberVia.Members));

        (await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotPermitted, () => documents.RequireAsync(Minutes, DocumentKeys.Edit, Cancellation)))
            .Code.Should().Be("documents.not-permitted");
        (await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.NotPermitted, () => folders.RequireAsync(new FolderId(7), FolderKeys.File, Cancellation)))
            .Code.Should().Be("folders.not-permitted");
        (await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.NotFound, () => folders.RequireAsync(new FolderId(8), FolderKeys.Read, Cancellation)))
            .Code.Should().Be("folders.not-found");

        (await folders.RequireAsync(new FolderId(7), FolderKeys.Read, Cancellation)).Version.Should().Be(9);
        documents.Asked.Should().NotContain(asked => asked.Contains("folders", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_question_is_asked_with_a_key()
    {
        var access = new StubAccess<DocumentId>(DocumentMembership.Rules);

        await FluentActions.Awaiting(() => access.RequireAsync(Minutes, " ", Cancellation)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => access.ViaAsync(Minutes, "", Cancellation)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => ((IMemberQuestions<DocumentId>)null!).RequireAsync(Minutes, DocumentKeys.View, Cancellation)).Should().ThrowAsync<ArgumentNullException>();
        access.Asked.Should().BeEmpty("a question that is not one is refused before anything is asked");
    }

    [Fact]
    public void A_reach_says_how_a_resource_it_was_read_with_is_reached_members_first()
    {
        var moment = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var reach = new StubAccess<DocumentId>(DocumentMembership.Rules) { Now = moment }.Reach(DocumentKeys.Edit);

        (reach.Key, reach.Now, reach.Everything).Should().Be((DocumentKeys.Edit, moment, false));
        reach.Via(asMember: true).Should().Be(MemberVia.Members);
        reach.Via(asMember: true, fromAbove: true).Should().Be(MemberVia.Members, "the members come first when both reach the resource");
        reach.Via(asMember: false, fromAbove: true).Should().Be(MemberVia.Above);
        reach.Via(asMember: false).Should().BeNull("the resource is not within the reach");

        // The application's own work reaches everything, by neither way.
        var system = new StubAccess<DocumentId>(DocumentMembership.Rules) { System = true }.Reach(DocumentKeys.Edit);
        system.Everything.Should().BeTrue();
        system.Via(asMember: false).Should().Be(MemberVia.System);
        system.Via(asMember: true, fromAbove: true).Should().Be(MemberVia.System);
    }

    [Fact]
    public void The_reach_of_what_is_seen_has_no_key_under_rules_that_name_none_for_seeing()
    {
        // A folder is seen by its staff, and its rules give being on it no key: there is a reach of what is seen, for no key.
        var moment = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        FolderMembership.Rules.SeeKey.Should().BeNull();

        var see = new StubAccess<FolderId>(FolderMembership.Rules) { Now = moment }.KeyReach([FolderKeys.File]).See;

        (see.Key, see.Now, see.Everything).Should().Be((null, moment, false));
        see.Via(asMember: true).Should().Be(MemberVia.Members);
        see.Via(asMember: false).Should().BeNull();
        new StubAccess<FolderId>(FolderMembership.Rules) { System = true }.KeyReach([FolderKeys.File]).See.Via(asMember: false).Should().Be(MemberVia.System);

        // Every other reach is for a key, and a blank one is none.
        FluentActions.Invoking(() => new StubAccess<FolderId>(FolderMembership.Rules).Reach(" ")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task The_keys_held_on_several_resources_are_answered_for_those_the_caller_sees()
    {
        var other = DocumentId.CreateSequential();
        var access = new StubAccess<DocumentId>(DocumentMembership.Rules)
            .Sees(Minutes, version: 4, (DocumentKeys.View, MemberVia.Members), (DocumentKeys.Edit, MemberVia.Members))
            .Sees(other, version: 1, (DocumentKeys.View, MemberVia.Members));

        var reach = access.KeyReach([DocumentKeys.Edit, DocumentKeys.Share, DocumentKeys.Edit]);
        reach.Keys.Should().Equal(DocumentKeys.Edit, DocumentKeys.Share);
        reach.See.Key.Should().Be(DocumentKeys.View, "which resources are answered about is decided by seeing them");

        var held = await access.KeysOnAsync([Minutes, other, Hidden], [DocumentKeys.Edit, DocumentKeys.Share], Cancellation);
        held.Should().ContainSingle().Which.Should().Match<KeyValuePair<DocumentId, IReadOnlySet<string>>>(pair => pair.Key == Minutes && pair.Value.SetEquals(new[] { DocumentKeys.Edit }));
        MemberQuestions.MostResources.Should().Be(200);
    }
}
