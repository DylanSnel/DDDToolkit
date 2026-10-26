using DDDToolkit.BaseTypes;
using Examples.Tenancy.Inspections.Domain.Aggregates.Inspections.Events;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Examples.Tenancy.Tests.Persistence;

/// <summary>
/// The names the sample stores, pinned as the text they are. A class's namespace follows its folder, so a class
/// that moves changes its full name; nothing that is written to a database may change with it. A domain event is
/// stored in the outbox under its module's name and its class's, in kebab case, and under nothing of its
/// namespace; and no model stores the name of a class to tell rows apart.
/// </summary>
public sealed class StoredNameTests
{
    /// <summary>Every domain event of the sample's own modules, with the name the outbox stores it under.</summary>
    public static TheoryData<Type, string> DomainEvents => new()
    {
        { typeof(ProjectOpened), "projects.project-opened" },
        { typeof(ProjectRenamed), "projects.project-renamed" },
        { typeof(ProjectPlanned), "projects.project-planned" },
        { typeof(ProjectMoved), "projects.project-moved" },
        { typeof(ProjectClosed), "projects.project-closed" },
        { typeof(ProjectReopened), "projects.project-reopened" },
        { typeof(CrewMemberAdded), "projects.crew-member-added" },
        { typeof(CrewRoleGiven), "projects.crew-role-given" },
        { typeof(CrewRoleTaken), "projects.crew-role-taken" },
        { typeof(CrewMemberRemoved), "projects.crew-member-removed" },
        { typeof(OwnerChanged), "projects.owner-changed" },
        { typeof(ProjectRoleMade), "projects.project-role-made" },
        { typeof(ProjectRoleRenamed), "projects.project-role-renamed" },
        { typeof(ProjectRoleKeysChanged), "projects.project-role-keys-changed" },
        { typeof(ProjectRoleArchived), "projects.project-role-archived" },
        { typeof(InspectionRecorded), "inspections.inspection-recorded" },
    };

    [Theory]
    [MemberData(nameof(DomainEvents))]
    public void A_domain_event_is_stored_under_its_module_and_its_class_whatever_its_namespace(Type type, string name)
    {
        DomainEventName.For(type).Should().Be(name, "a row an older build wrote under this name is still read as {0}", type.Name);
        type.Namespace.Should().EndWith(".Events", "the event is in its aggregate's Events folder, and its stored name says nothing of that");
    }

    [Fact]
    public void Every_domain_event_of_the_modules_is_pinned()
    {
        var declared = SampleLayout.Projects
            .Where(project => project.Layer == Layer.Domain)
            .SelectMany(project => TypeScan.TypesOf(project.Anchor.Assembly))
            .Where(type => typeof(DomainEvent).IsAssignableFrom(type) && !type.IsAbstract);

        declared.Should().BeEquivalentTo(
            DomainEvents.Select(row => row.Data.Item1),
            "an event added to a module is added above, with the name it is stored under");
    }

    [Fact]
    public void No_model_stores_the_name_of_a_class()
    {
        // A discriminator column holds a class's name for as long as its rows live. The sample has no hierarchy
        // that needs one, so no class's name is in any row, and a namespace can follow its folder. The models are
        // the ones the design-time factories build, which connect to nothing.
        DbContext[] contexts =
        [
            new TenantsContextDesignTimeFactory().CreateDbContext([]),
            new ProjectsContextDesignTimeFactory().CreateDbContext([]),
            new InspectionsContextDesignTimeFactory().CreateDbContext([]),
        ];

        foreach (var context in contexts)
        {
            using (context)
            {
                context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
                    .Where(entity => entity.FindDiscriminatorProperty() is not null)
                    .Select(entity => entity.Name)
                    .Should().BeEmpty("{0} tells no rows apart by the name of a class", context.GetType().Name);
            }
        }
    }
}
