using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// The managers the generator writes for the host's member classes and role classes, as the resource's registration
/// registers them over the host's context: each change loads the resource on the request's own context, changes it and
/// saves it there, and the reads answer what the caller sees, under the context's own rules.
/// </summary>
public sealed class ManagersTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void The_registration_of_a_resource_registers_the_managers_the_resource_names()
    {
        using var filing = new SqliteFiling();
        using var garden = new SqliteGarden();

        typeof(Document).GetInterfaces().Should().Contain(typeof(IMembershipResource<DocumentShare, UserId, NamedRole>));
        FluentActions.Invoking(() => filing.Services.Provider.GetService<DocumentShareManager>()).Should().Throw<InvalidOperationException>("a manager is made per scope");
        using (var scope = filing.Services.Provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<DocumentShareManager>().Rules.Should().BeSameAs(DocumentMembership.Rules);
            scope.ServiceProvider.GetRequiredService<FolderMemberManager>().Rules.Should().BeSameAs(FolderMembership.Rules);
        }

        // A resource whose roles a customer makes names the role manager of its role class as well, with the store it saves through.
        using (var scope = garden.Services.Provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<PlotGardenerManager>().Should().NotBeNull();
            scope.ServiceProvider.GetRequiredService<PlotRoleManager>().Rules.Should().BeSameAs(PlotMembership.Rules);
            scope.ServiceProvider.GetRequiredService<ShedRoleManager>().Rules.Should().BeSameAs(ShedMembership.Rules);
            scope.ServiceProvider.GetRequiredService<ICustomRoleStore<PlotRole, PlotRoleId>>().Should().NotBeNull();
        }
    }

    [Fact]
    public async Task A_member_is_added_on_the_requests_context_and_saved_with_the_resource()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var newcomer = UserId.CreateSequential();

        // Ada owns the minutes, and the owner holds the permission that shares them by owning them.
        var answered = await filing.Services.AsAsync(TestUsers.User(data.Ada), provider =>
            provider.GetRequiredService<DocumentShareManager>().AddAsync(data.Minutes, newcomer, DocumentMembership.Onlooker, until: null, Cancellation));

        var read = await filing.Services.ReadAsync(data.Minutes);
        read.Version.Should().Be(answered.Version, "the answer is the resource as it was saved");
        var share = read.Shares.Single(each => each.MemberId == newcomer);
        (share.AddedBy, share.StartsAt).Should().Be((data.Ada, filing.Clock.Now));
        share.Roles.Single().RoleId.Should().Be(DocumentMembership.Onlooker);
        filing.Services.Raised.OfType<DocumentShared>().Should().Contain(raised => raised.With == newcomer, "the document's hook raised it, and the save sent it");

        // Ben only contributes: the share permission is not his, and nothing is written.
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotPermitted, () => filing.Services.AsAsync(TestUsers.User(data.Ben), provider =>
            provider.GetRequiredService<DocumentShareManager>().RemoveAsync(data.Minutes, newcomer, Cancellation)));
        (await filing.Services.ReadAsync(data.Minutes)).Version.Should().Be(answered.Version);
    }

    [Fact]
    public async Task The_members_are_listed_for_what_the_caller_sees_owner_first()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // Ben sees the minutes, where he contributes, and the budget, which he owns.
        var listed = await filing.Services.AsAsync(TestUsers.User(data.Ben), provider =>
            provider.GetRequiredService<DocumentShareManager>().ListAsync([data.Minutes, data.Budget], Cancellation));
        listed.Keys.Should().BeEquivalentTo([data.Minutes, data.Budget]);
        listed[data.Minutes][0].MemberId.Should().Be(data.Ada, "the owner comes first");
        listed[data.Budget].Select(member => member.MemberId).Should().Equal(data.Ben, data.Ada);

        // Hal is on neither: he is answered about neither, and a list of one he does not see is not found.
        (await filing.Services.AsAsync(TestUsers.User(data.Hal), provider => provider.GetRequiredService<DocumentShareManager>().ListAsync([data.Minutes, data.Budget], Cancellation)))
            .Should().NotContainKey(data.Minutes).And.NotContainKey(data.Budget);
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => filing.Services.AsAsync(TestUsers.User(data.Hal), provider =>
            provider.GetRequiredService<DocumentShareManager>().ListAsync(data.Minutes, Cancellation)));
    }

    [Fact]
    public async Task The_role_manager_reads_and_changes_the_roles_the_requests_context_lets_through()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;

        // In the meadow, the meadow's roles: the host's rule on its context says whose roles a request reads.
        var meadow = await garden.Services.InAsync(data.Meadow, provider => provider.GetRequiredService<PlotRoleManager>().ListAsync(Cancellation));
        meadow.Select(role => role.GardenId).Should().OnlyContain(id => id == data.Meadow);
        meadow.Select(role => role.Name).Should().BeInAscendingOrder(StringComparer.Ordinal);

        // A role of the orchard is not there for a request of the meadow.
        await Refused.WithCodeAsync(PlotMembership.Codes, MembershipRefusals.RoleNotFound, () => garden.Services.InAsync(data.Meadow, provider =>
            provider.GetRequiredService<PlotRoleManager>().RenameAsync(data.OrchardTender.Id, "Grower", null, Cancellation)));

        // The application's own work renames a role of its garden, and the role raises the host's event.
        await garden.Services.InAsync(data.Meadow, provider => provider.GetRequiredService<PlotRoleManager>().RenameAsync(data.Fencer, "Hedger", "Keeps the rabbits out.", Cancellation));
        (await garden.Services.ReadAsync(data.Fencer.Id)).Name.Should().Be("Hedger");
        garden.Services.Raised.OfType<PlotRoleRenamed>().Should().ContainSingle().Which.Name.Should().Be("Hedger");

        // The plots' rules name no permission for their roles: a gardener changes none.
        await Refused.WithCodeAsync(PlotMembership.Codes, MembershipRefusals.NotPermitted, () => garden.Services.AsAsync(data.Ada, provider =>
            provider.GetRequiredService<PlotRoleManager>().ArchiveAsync(data.Fencer.Id, Cancellation)));
    }
}
