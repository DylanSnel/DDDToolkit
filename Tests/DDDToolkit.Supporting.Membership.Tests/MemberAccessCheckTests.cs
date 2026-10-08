using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// The requirements a request about a resource with members declares, and the check that holds its caller to
/// them before a handler runs: over the toolkit's own requirement types, with nothing of a dispatcher in it. A
/// module with two kinds of resource registers two checks, and each decides the cases of its own resource only.
/// </summary>
public sealed class MemberAccessCheckTests
{
    private static readonly DocumentId Minutes = DocumentId.CreateSequential();

    private static readonly FolderId Cabinet = new(7);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- the requirements

    [Fact]
    public void The_cases_are_made_by_MemberAccess_alone_the_one_spelling_a_request_writes()
    {
        typeof(MemberAccess<DocumentId>.On).GetConstructors().Should().BeEmpty("MemberAccess.On makes it");
        typeof(MemberAccess<DocumentId>.SeenWith).GetConstructors().Should().BeEmpty("MemberAccess.SeenWith makes it");
    }

    [Fact]
    public void Requirements_that_say_the_same_are_equal_and_others_are_not()
    {
        MemberAccess.On(DocumentKeys.Share, Minutes, 3).Should().Be(MemberAccess.On(DocumentKeys.Share, Minutes, 3));
        MemberAccess.On(DocumentKeys.Share, Minutes).Should().Be(MemberAccess.On(DocumentKeys.Share, Minutes, expectedVersion: null));
        MemberAccess.On(DocumentKeys.Share, Minutes, 3).Should().NotBe(MemberAccess.On(DocumentKeys.Share, Minutes, 4));
        MemberAccess.On(DocumentKeys.Share, Minutes).Should().NotBe(MemberAccess.On(DocumentKeys.Edit, Minutes));
        MemberAccess.On(DocumentKeys.Share, Minutes).Should().NotBe(MemberAccess.On(DocumentKeys.Share, DocumentId.CreateSequential()));
        MemberAccess.SeenWith<DocumentId>(DocumentKeys.View).Should().Be(MemberAccess.SeenWith<DocumentId>(DocumentKeys.View));

        // The same key about another kind of resource is another requirement.
        ((AccessRequirement)MemberAccess.SeenWith<DocumentId>(DocumentKeys.View)).Should().NotBe(MemberAccess.SeenWith<FolderId>(DocumentKeys.View));

        var on = MemberAccess.On(DocumentKeys.Share, Minutes, 3);
        (on.Key, on.Resource, on.ExpectedVersion).Should().Be((DocumentKeys.Share, Minutes, 3));
    }

    [Fact]
    public void A_requirement_names_a_key()
    {
        FluentActions.Invoking(() => MemberAccess.On(" ", Minutes)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => MemberAccess.SeenWith<DocumentId>("")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => MemberAccess.On(null!, Minutes)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_request_declares_what_it_requires_next_to_its_own_fields()
    {
        IRequireAccess share = new ShareDocument(Minutes, UserId.CreateSequential(), ExpectedVersion: 5);
        IRequireAccess list = new ListDocuments();
        IRequireAccess file = new FileInFolder(Cabinet);

        share.RequiredAccess.Should().Be(MemberAccess.On(DocumentKeys.Share, Minutes, 5));
        list.RequiredAccess.Should().Be(MemberAccess.SeenWith<DocumentId>(DocumentKeys.View));
        file.RequiredAccess.Should().Be(MemberAccess.On(FolderKeys.File, Cabinet));
    }

    // ---------------------------------------------------------------- one check

    [Fact]
    public void A_check_decides_the_requirements_of_its_own_resource_and_no_other()
    {
        var documents = DocumentCheck(new StubAccess<DocumentId>(DocumentMembership.Rules), new());
        var folders = new MemberAccessCheck<Folder, FolderId>(new StubAccess<FolderId>(FolderMembership.Rules), new(), new AmbientCallerAccessor());

        documents.Decides(MemberAccess.On(DocumentKeys.Share, Minutes)).Should().BeTrue();
        documents.Decides(MemberAccess.SeenWith<DocumentId>(DocumentKeys.View)).Should().BeTrue();
        documents.Decides(MemberAccess.On(FolderKeys.File, Cabinet)).Should().BeFalse();
        documents.Decides(MemberAccess.SeenWith<FolderId>(FolderKeys.Read)).Should().BeFalse();
        documents.Decides(AccessRequirement.AllowAnonymous()).Should().BeFalse("nobody has to decide that anyone may send a request");
        documents.Decides(AccessRequirement.SignedIn()).Should().BeFalse("who is calling is the core's to decide");
        documents.Decides(new SomethingElse()).Should().BeFalse();

        folders.Decides(MemberAccess.On(FolderKeys.File, Cabinet)).Should().BeTrue();
        folders.Decides(MemberAccess.On(DocumentKeys.Share, Minutes)).Should().BeFalse();
    }

    [Fact]
    public async Task A_request_on_a_resource_passes_when_the_key_is_held_and_its_handler_takes_what_was_checked()
    {
        var access = new StubAccess<DocumentId>(DocumentMembership.Rules).Sees(Minutes, version: 4, (DocumentKeys.Share, MemberVia.Members));
        var kept = new Checked<MemberHold<DocumentId>>();
        var check = DocumentCheck(access, kept);
        var request = new ShareDocument(Minutes, UserId.CreateSequential());

        await check.RequireAsync(((IRequireAccess)request).RequiredAccess, request, Cancellation);

        // Kept under the request itself: its handler loads the very resource that was checked, at that version.
        kept.TakeFor(request).Should().Be(new MemberHold<DocumentId>(Minutes, MemberVia.Members, 4, Until: null));
        FluentActions.Invoking(() => kept.TakeFor(request)).Should().Throw<InvalidOperationException>("what was kept is handed out once");
    }

    [Fact]
    public async Task What_is_kept_says_until_when_the_caller_holds_the_key_and_the_check_refuses_nothing_by_it()
    {
        // A hold that ends in an hour, and one that ended on paper a year from now: the check passes both alike.
        // What a host makes of the end is the host's own rule, in its handler.
        var soon = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        var access = new StubAccess<DocumentId>(DocumentMembership.Rules) { Until = soon }.Sees(Minutes, version: 4, (DocumentKeys.Share, MemberVia.Members));
        var kept = new Checked<MemberHold<DocumentId>>();
        var check = DocumentCheck(access, kept);
        var request = new ShareDocument(Minutes, UserId.CreateSequential(), Until: soon.AddYears(1));

        await check.RequireAsync(((IRequireAccess)request).RequiredAccess, request, Cancellation);

        kept.TakeFor(request).Until.Should().Be(soon, "the answer of the storage is handed on as it is");
    }

    [Fact]
    public async Task A_refused_request_keeps_nothing_for_its_handler()
    {
        var access = new StubAccess<DocumentId>(DocumentMembership.Rules).Sees(Minutes, version: 4, (DocumentKeys.View, MemberVia.Members));
        var kept = new Checked<MemberHold<DocumentId>>();
        var check = DocumentCheck(access, kept);
        var seen = new ShareDocument(Minutes, UserId.CreateSequential());
        var unseen = new ShareDocument(DocumentId.CreateSequential(), UserId.CreateSequential());

        (await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotPermitted, () => check.RequireAsync(((IRequireAccess)seen).RequiredAccess, seen, Cancellation).AsTask()))
            .Arguments.Should().Contain("Key", DocumentKeys.Share);
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => check.RequireAsync(((IRequireAccess)unseen).RequiredAccess, unseen, Cancellation).AsTask());

        // A handler reached anyway has nothing to load.
        FluentActions.Invoking(() => kept.TakeFor(seen)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => kept.TakeFor(unseen)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task The_version_a_caller_read_is_compared_only_once_the_caller_has_access()
    {
        var access = new StubAccess<DocumentId>(DocumentMembership.Rules).Sees(Minutes, version: 4, (DocumentKeys.Share, MemberVia.Members));
        var kept = new Checked<MemberHold<DocumentId>>();
        var check = DocumentCheck(access, kept);

        // With access and the version the resource is at: through.
        var current = new ShareDocument(Minutes, UserId.CreateSequential(), ExpectedVersion: 4);
        await check.RequireAsync(((IRequireAccess)current).RequiredAccess, current, Cancellation);
        kept.TakeFor(current).Version.Should().Be(4);

        // With access and another version: somebody changed the resource since. A lost race, and nothing kept.
        var stale = new ShareDocument(Minutes, UserId.CreateSequential(), ExpectedVersion: 3);
        var conflict = (await FluentActions.Awaiting(() => check.RequireAsync(((IRequireAccess)stale).RequiredAccess, stale, Cancellation).AsTask())
            .Should().ThrowAsync<ConcurrencyConflictException>()).Which;
        (conflict.AggregateType, conflict.AggregateId).Should().Be((typeof(Document), Minutes), "a lost race names the resource's aggregate and its id");
        FluentActions.Invoking(() => kept.TakeFor(stale)).Should().Throw<InvalidOperationException>();

        // Without the key, whatever version is sent: answered as if none had been sent.
        var noKey = new StubAccess<DocumentId>(DocumentMembership.Rules).Sees(Minutes, version: 4, (DocumentKeys.View, MemberVia.Members));
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotPermitted, () => DocumentCheck(noKey, kept).RequireAsync(((IRequireAccess)stale).RequiredAccess, stale, Cancellation).AsTask());

        // Without seeing the resource, whatever version is sent: not found, and no version is learned.
        var blind = new StubAccess<DocumentId>(DocumentMembership.Rules);
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => DocumentCheck(blind, kept).RequireAsync(((IRequireAccess)stale).RequiredAccess, stale, Cancellation).AsTask());
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => DocumentCheck(blind, kept).RequireAsync(((IRequireAccess)current).RequiredAccess, current, Cancellation).AsTask());
    }

    [Fact]
    public async Task A_query_that_shows_what_a_key_is_held_on_requires_a_caller_and_refuses_nobody_for_the_key()
    {
        // The caller sees nothing and holds nothing: its list is empty, and that is an answer.
        var access = new StubAccess<DocumentId>(DocumentMembership.Rules);
        var kept = new Checked<MemberHold<DocumentId>>();
        var query = new ListDocuments();

        await DocumentCheck(access, kept).RequireAsync(((IRequireAccess)query).RequiredAccess, query, Cancellation);

        access.Asked.Should().Equal(["caller"], "the key is the filter of the query's own statement");
        FluentActions.Invoking(() => kept.TakeFor(query)).Should().Throw<InvalidOperationException>("no resource was checked for a list");

        // A caller the questions cannot be asked about at all is refused, with that caller's own refusal.
        var nobody = new RefusalException("host.not-signed-in", RefusalKind.NotPermitted, "Sign in first.");
        var closed = new StubAccess<DocumentId>(DocumentMembership.Rules) { Nobody = nobody };
        (await FluentActions.Awaiting(() => DocumentCheck(closed, kept).RequireAsync(((IRequireAccess)query).RequiredAccess, query, Cancellation).AsTask())
            .Should().ThrowAsync<RefusalException>()).Which.Should().BeSameAs(nobody);
    }

    [Fact]
    public async Task A_query_that_shows_what_a_key_is_held_on_refuses_a_caller_who_did_not_sign_in_before_its_statement()
    {
        // A caller who did not sign in is nobody's member under any rules: its list would be empty, and a database
        // that checks every row may refuse its role the resource's schema outright. So the handler does not run.
        var access = new StubAccess<DocumentId>(DocumentMembership.Rules).Sees(Minutes, version: 4, (DocumentKeys.View, MemberVia.Members));
        var query = new ListDocuments();

        RefusalException refused;
        using (Callers.Begin(Caller.Anonymous))
        {
            refused = await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotPermitted, () => DocumentCheck(access, new()).RequireAsync(((IRequireAccess)query).RequiredAccess, query, Cancellation).AsTask());
        }

        refused.Arguments.Should().Contain("Key", DocumentKeys.View);
        access.Asked.Should().Equal(["caller"], "the caller's own refusal comes first, and nothing is read");

        // A signed-in caller who reaches nothing is let through: its empty list is an answer.
        using (Callers.Begin(Caller.User(Guid.NewGuid())))
        {
            await DocumentCheck(new StubAccess<DocumentId>(DocumentMembership.Rules), new()).RequireAsync(((IRequireAccess)query).RequiredAccess, query, Cancellation);
        }

        // And the caller's own refusal, where the questions have one, is what a caller who is nobody hears first.
        var nobody = new RefusalException("host.not-signed-in", RefusalKind.NotPermitted, "Sign in first.");
        using (Callers.Begin(Caller.Anonymous))
        {
            (await FluentActions.Awaiting(() => DocumentCheck(new StubAccess<DocumentId>(DocumentMembership.Rules) { Nobody = nobody }, new()).RequireAsync(((IRequireAccess)query).RequiredAccess, query, Cancellation).AsTask())
                .Should().ThrowAsync<RefusalException>()).Which.Should().BeSameAs(nobody);
        }
    }

    [Fact]
    public async Task A_requirement_that_is_not_one_of_its_cases_stops_the_request()
    {
        var check = DocumentCheck(new StubAccess<DocumentId>(DocumentMembership.Rules), new());
        var request = new FileInFolder(Cabinet);

        // Asked all the same, round the set that asks Decides first: it fails closed.
        (await FluentActions.Awaiting(() => check.RequireAsync(((IRequireAccess)request).RequiredAccess, request, Cancellation).AsTask())
            .Should().ThrowAsync<InvalidOperationException>()).WithMessage("*FileInFolder*the access check of Document does not decide*lets nobody through*");
        await FluentActions.Awaiting(() => check.RequireAsync(null!, request, Cancellation).AsTask()).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => check.RequireAsync(((IRequireAccess)request).RequiredAccess, null!, Cancellation).AsTask()).Should().ThrowAsync<ArgumentNullException>();
    }

    // ---------------------------------------------------------------- two resources in one module

    [Fact]
    public async Task Two_kinds_of_resource_in_one_module_are_each_held_to_their_own_check()
    {
        var documents = new StubAccess<DocumentId>(DocumentMembership.Rules).Sees(Minutes, version: 4, (DocumentKeys.Share, MemberVia.Members));
        var folders = new StubAccess<FolderId>(FolderMembership.Rules).Sees(Cabinet, version: 9, (FolderKeys.Read, MemberVia.Members));

        await using var provider = new ServiceCollection()
            .AddSingleton<IMemberQuestions<DocumentId>>(documents)
            .AddSingleton<IMemberQuestions<FolderId>>(folders)
            .AddSingleton<ICallerAccessor, AmbientCallerAccessor>()
            .AddAccessCheck<IFilingRequest, MemberAccessCheck<Document, DocumentId>>()
            .AddAccessCheck<IFilingRequest, MemberAccessCheck<Folder, FolderId>>()
            .BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var checks = scope.ServiceProvider.GetRequiredService<AccessChecks<IFilingRequest>>();

        // A request about a document is checked on documents, and what was read is kept as a document's.
        var share = new ShareDocument(Minutes, UserId.CreateSequential());
        await checks.RequireAsync(share, Cancellation);
        scope.ServiceProvider.GetRequiredService<Checked<MemberHold<DocumentId>>>().TakeFor(share).Should().Be(new MemberHold<DocumentId>(Minutes, MemberVia.Members, 4, Until: null));
        folders.Asked.Should().BeEmpty("a request about a document asks nothing about folders");

        // A request about a folder is checked on folders, and refused under the folder's codes.
        var file = new FileInFolder(Cabinet);
        (await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.NotPermitted, () => checks.RequireAsync(file, Cancellation).AsTask()))
            .Arguments.Should().Contain("Key", FolderKeys.File);
        documents.Asked.Should().Equal("caller", "hold " + DocumentKeys.Share);
        FluentActions.Invoking(() => scope.ServiceProvider.GetRequiredService<Checked<MemberHold<FolderId>>>().TakeFor(file)).Should().Throw<InvalidOperationException>();

        // Both lists pass, each through its own questions.
        await checks.RequireAsync(new ListDocuments(), Cancellation);
        await checks.RequireAsync(new ListFolders(), Cancellation);
        documents.Asked.Should().Equal("caller", "hold " + DocumentKeys.Share, "caller");
        folders.Asked.Should().Equal("caller", "hold " + FolderKeys.File, "caller");

        // Every requirement the module's requests declare has a check in its set.
        IRequireAccess[] requests = [share, file, new ListDocuments(), new ListFolders()];
        requests.Should().OnlyContain(request => checks.Decides(request.RequiredAccess));
    }

    [Fact]
    public async Task A_requirement_about_a_kind_of_resource_nobody_registered_a_check_for_lets_nobody_through()
    {
        await using var provider = new ServiceCollection()
            .AddSingleton<IMemberQuestions<DocumentId>>(new StubAccess<DocumentId>(DocumentMembership.Rules))
            .AddSingleton<ICallerAccessor, AmbientCallerAccessor>()
            .AddAccessCheck<IFilingRequest, MemberAccessCheck<Document, DocumentId>>()
            .BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var checks = scope.ServiceProvider.GetRequiredService<AccessChecks<IFilingRequest>>();

        checks.Decides(MemberAccess.On(FolderKeys.File, Cabinet)).Should().BeFalse();
        (await FluentActions.Awaiting(() => checks.RequireAsync(new FileInFolder(Cabinet), Cancellation).AsTask())
            .Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*FileInFolder declares 'MemberAccess<FolderId>.On'*register the check that decides it with services.Add{Resource}MemberAccess<IFilingRequest>(), written for the resource whose id the case names.");
    }

    private static MemberAccessCheck<Document, DocumentId> DocumentCheck(IMemberQuestions<DocumentId> access, Checked<MemberHold<DocumentId>> kept) => new(access, kept, new AmbientCallerAccessor());

    /// <summary>A requirement of somebody else's.</summary>
    private sealed record SomethingElse : AccessRequirement;
}
