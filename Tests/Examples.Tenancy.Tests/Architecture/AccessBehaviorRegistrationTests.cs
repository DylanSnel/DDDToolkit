using System.Reflection;
using DDDToolkit.Access;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// What the start-up check of the access behaviors holds the sample's host to, read from what the host registers: every
/// command and query of every module passes the behavior the toolkit wrote for its request interface. A module that
/// left it out would have requests that reach their handlers with nothing asking what they require; the check names
/// it, and the line that adds it, before the host serves anything.
/// </summary>
public sealed class AccessBehaviorRegistrationTests
{
    /// <summary>The request interface of every module, each marked <c>[AccessRequests]</c>.</summary>
    private static readonly Type[] RequestInterfaces = [typeof(ITenantsRequest), typeof(IProjectsRequest), typeof(IInspectionsRequest)];

    [Fact]
    public void The_toolkit_wrote_both_behaviors_and_their_registration_for_every_module()
    {
        foreach (var requests in RequestInterfaces)
        {
            var written = requests.Assembly.GetCustomAttributes<AccessBehaviorAttribute>().Should().ContainSingle(each => each.Requests == requests, "{0} is marked [AccessRequests]", requests.Name).Subject;

            written.StreamBehavior.Should().NotBeNull("Mediator has a pipeline of its own for streams, so {0} has a second behavior", requests.Name);
            written.Registration.Should().Be("services.Add" + requests.Name[1..^"Request".Length] + "AccessBehavior()");
        }
    }

    [Fact]
    public void The_host_registers_the_checks_and_the_behaviors_of_every_module()
    {
        var registered = Registrations();

        HostRegistrations.All.Select(descriptor => descriptor.ServiceType)
            .Should().Contain([typeof(AccessChecks<ITenantsRequest>), typeof(AccessChecks<IProjectsRequest>), typeof(AccessChecks<IInspectionsRequest>)]);
        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(registered)).Should().NotThrow();
    }

    [Fact]
    public void A_module_that_leaves_its_behavior_out_is_named_with_the_line_that_adds_it()
    {
        var registered = Registrations();
        registered.Remove(registered.Single(descriptor => descriptor.ImplementationType == typeof(InspectionsAccessBehavior<,>)));

        var check = () => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(registered);

        check.Should().Throw<InvalidOperationException>().Which.Message.Should()
            .StartWith("InspectionsAccessBehavior<TMessage, TResponse>, the pipeline behavior the toolkit wrote to ask the access checks of IInspectionsRequest, is not in the pipeline:")
            .And.EndWith("Add services.AddInspectionsAccessBehavior() where the module registers its checks.");
    }

    [Fact]
    public void A_module_without_a_query_answered_with_a_stream_needs_nothing_in_that_pipeline()
    {
        // No module of the sample streams, so the behaviors for streams, which the generated registration adds as
        // well, are there for the day one does: without them the host is not short of anything yet.
        var registered = Registrations();
        foreach (var streams in new[] { typeof(TenantsAccessStreamBehavior<,>), typeof(ProjectsAccessStreamBehavior<,>), typeof(InspectionsAccessStreamBehavior<,>) })
        {
            registered.Remove(registered.Single(descriptor => descriptor.ImplementationType == streams));
        }

        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(registered)).Should().NotThrow();
    }

    /// <summary>A copy of everything the host registers, to take a registration out of.</summary>
    private static IServiceCollection Registrations()
    {
        IServiceCollection registered = new ServiceCollection();
        foreach (var descriptor in HostRegistrations.All)
        {
            registered.Add(descriptor);
        }

        return registered;
    }
}
