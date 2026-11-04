using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// A request about several resources of one kind: <c>[OnResource]</c> on a collection of ids, which the generator writes as
/// <c>MemberAccess.OnEach</c>. The check reads the resources in one statement for every 200, refuses the request when one of
/// them is refused, in the order one resource is refused in, and keeps a hold for each.
/// </summary>
public sealed class MemberAccessOnEachTests
{
    private static readonly DocumentId Minutes = DocumentId.CreateSequential();

    private static readonly DocumentId Budget = DocumentId.CreateSequential();

    private static readonly DocumentId Outline = DocumentId.CreateSequential();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void The_requirement_names_each_resource_once_and_two_that_say_the_same_are_equal()
    {
        var each = MemberAccess.OnEach(DocumentPermissions.Share, [Minutes, Budget, Minutes]);

        each.Resources.Should().Equal([Minutes, Budget], "a resource named twice is asked about once, at the place it is first named");
        each.Should().Be(MemberAccess.OnEach(DocumentPermissions.Share, new List<DocumentId> { Minutes, Budget }));
        each.GetHashCode().Should().Be(MemberAccess.OnEach(DocumentPermissions.Share, [Minutes, Budget]).GetHashCode());
        each.Should().NotBe(MemberAccess.OnEach(DocumentPermissions.Share, [Budget, Minutes]));
        each.Should().NotBe(MemberAccess.OnEach(DocumentPermissions.Edit, [Minutes, Budget]));
        each.ToString().Should().Contain(DocumentPermissions.Share).And.Contain(Minutes.ToString()).And.Contain(Budget.ToString());
        typeof(MemberAccess<DocumentId>.OnEach).GetConstructors().Should().BeEmpty("MemberAccess.OnEach makes it");

        FluentActions.Invoking(() => MemberAccess.OnEach(" ", [Minutes])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => MemberAccess.OnEach<DocumentId>(DocumentPermissions.Share, null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_request_with_the_marker_on_a_list_requires_the_permission_on_each_document_of_the_list()
    {
        IRequireAccess request = new ShareOnEach([Minutes, Budget], UserId.CreateSequential());

        request.RequiredAccess.Should().Be(MemberAccess.OnEach(DocumentPermissions.Share, [Minutes, Budget]));
    }

    [Fact]
    public async Task The_permission_held_on_each_resource_lets_the_request_through_and_a_hold_is_kept_for_each()
    {
        var access = new StubAccess<Document, DocumentId>(DocumentMembership.Rules)
            .Sees(Minutes, version: 4, (DocumentPermissions.Share, MemberVia.Members))
            .Sees(Budget, version: 9, (DocumentPermissions.Share, MemberVia.Above));
        var request = new ShareOnEach([Minutes, Budget, Minutes], UserId.CreateSequential());
        await using var provider = Checks(access);
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(request, Cancellation);

        access.Asked.Should().Equal(["caller", "holds " + DocumentPermissions.Share + " on 2"], "the caller first, then one statement for both documents");
        scope.ServiceProvider.GetRequiredService<Checked<MemberHold<DocumentId>>>().TakeAllFor(request).Should().Equal(
            new MemberHold<DocumentId>(Minutes, MemberVia.Members, 4, Until: null),
            new MemberHold<DocumentId>(Budget, MemberVia.Above, 9, Until: null));
    }

    [Fact]
    public async Task One_resource_the_caller_does_not_see_refuses_the_request_as_not_found_before_one_it_does_not_hold_the_permission_on()
    {
        // The minutes it sees without the permission, the outline it does not see at all: not-found comes first.
        var access = new StubAccess<Document, DocumentId>(DocumentMembership.Rules)
            .Sees(Minutes, version: 4, (DocumentPermissions.View, MemberVia.Members))
            .Sees(Budget, version: 9, (DocumentPermissions.Share, MemberVia.Members));
        var kept = new Checked<MemberHold<DocumentId>>();
        var request = new ShareOnEach([Minutes, Budget, Outline], UserId.CreateSequential());

        var refused = await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => Check(access, kept).RequireAsync(((IRequireAccess)request).RequiredAccess, request, Cancellation).AsTask());

        refused.Arguments.Values.Should().NotContain(value => Equals(value, Outline), "the refusal names no resource, as for one resource");
        FluentActions.Invoking(() => kept.TakeAllFor(request)).Should().Throw<InvalidOperationException>("nothing is kept for a request the check refused");
    }

    [Fact]
    public async Task Every_resource_seen_and_one_without_the_permission_refuses_the_request_as_not_permitted_naming_the_permission()
    {
        var access = new StubAccess<Document, DocumentId>(DocumentMembership.Rules)
            .Sees(Minutes, version: 4, (DocumentPermissions.Share, MemberVia.Members))
            .Sees(Budget, version: 9, (DocumentPermissions.View, MemberVia.Members));
        var request = new ShareOnEach([Minutes, Budget], UserId.CreateSequential());

        var refused = await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotPermitted, () => Check(access, new()).RequireAsync(((IRequireAccess)request).RequiredAccess, request, Cancellation).AsTask());

        refused.Arguments.Should().Contain("Permission", DocumentPermissions.Share);
    }

    [Fact]
    public async Task An_empty_list_requires_a_caller_and_reads_nothing()
    {
        var access = new StubAccess<Document, DocumentId>(DocumentMembership.Rules);
        var request = new ShareOnEach([], UserId.CreateSequential());

        await Check(access, new()).RequireAsync(((IRequireAccess)request).RequiredAccess, request, Cancellation);
        access.Asked.Should().Equal(["caller"]);

        // A caller the questions cannot be asked about is refused with its own refusal, also for no resources.
        var nobody = new RefusalException("host.not-signed-in", RefusalKind.NotPermitted, "Sign in first.");
        var closed = new StubAccess<Document, DocumentId>(DocumentMembership.Rules) { Nobody = nobody };
        (await FluentActions.Awaiting(() => Check(closed, new()).RequireAsync(((IRequireAccess)request).RequiredAccess, request, Cancellation).AsTask())
            .Should().ThrowAsync<RefusalException>()).Which.Should().BeSameAs(nobody);
    }

    [Fact]
    public async Task More_than_200_resources_are_read_in_parts_of_200_and_one_missing_from_the_last_part_refuses_the_request()
    {
        var documents = Enumerable.Range(0, 450).Select(_ => DocumentId.CreateSequential()).ToList();
        var access = new StubAccess<Document, DocumentId>(DocumentMembership.Rules);
        foreach (var document in documents)
        {
            access.Sees(document, version: 1, (DocumentPermissions.Share, MemberVia.Members));
        }

        var request = new ShareOnEach(documents, UserId.CreateSequential());
        await using var provider = Checks(access);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(request, Cancellation);

        access.Asked.Should().Equal(
            "caller",
            "holds " + DocumentPermissions.Share + " on 200",
            "holds " + DocumentPermissions.Share + " on 200",
            "holds " + DocumentPermissions.Share + " on 50");
        scope.ServiceProvider.GetRequiredService<Checked<MemberHold<DocumentId>>>().TakeAllFor(request).Select(hold => hold.Resource).Should().Equal(documents);

        var unseen = new ShareOnEach([.. documents, DocumentId.CreateSequential()], UserId.CreateSequential());
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => Check(access, new()).RequireAsync(((IRequireAccess)unseen).RequiredAccess, unseen, Cancellation).AsTask());
    }

    /// <summary>The module's checks, as a host registers them, over <paramref name="documents"/>: a request is in hand while they run, so every hold it keeps stays.</summary>
    private static ServiceProvider Checks(StubAccess<Document, DocumentId> documents)
        => new ServiceCollection()
            .AddSingleton<MembershipAccessManager<Document>>(documents)
            .AddSingleton<MembershipAccessManager<Folder>>(new StubAccess<Folder, FolderId>(FolderMembership.Rules))
            .AddSingleton<ICurrentUser, CurrentUser>()
            .AddAccessCheck<IFilingRequest, MemberAccessCheck<Document, DocumentId>>()
            .AddAccessCheck<IFilingRequest, MemberAccessCheck<Folder, FolderId>>()
            .BuildServiceProvider(validateScopes: true);

    private static MemberAccessCheck<Document, DocumentId> Check(MembershipAccessManager<Document> access, Checked<MemberHold<DocumentId>> kept) => new(access, kept, new CurrentUser());
}
