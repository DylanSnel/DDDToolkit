using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Tests.Access;

/// <summary>
/// Who is acting, as a record keeps it: a kind and an id, read from the toolkit's own caller each time it is
/// asked, by an accessor that keeps nothing between two answers.
/// </summary>
public class ActedByTests
{
    private static readonly Guid Ada = Guid.Parse("ada00000-0000-4000-8000-000000000001");

    [Fact]
    public void Each_kind_of_caller_is_kept_as_a_kind_and_an_id()
    {
        ActedBy.From(Caller.User(Ada)).Should().Be(new ActedBy(ActedByKinds.User, "ada00000-0000-4000-8000-000000000001"));
        ActedBy.From(Callers.FromClaims("""{"sub":"auth0|ada","role":"authenticated"}""")).Should().Be(new ActedBy("user", "auth0|ada"), "a user whose id is no uuid is kept by the token's sub");
        ActedBy.From(Caller.User(userId: null)).Should().Be(new ActedBy("user", null), "a user nobody can name is still a user");
        ActedBy.From(Caller.SystemIn("projects")).Should().Be(new ActedBy(ActedByKinds.System, "projects"), "scoped system work is the system, in its scope");
        ActedBy.From(Caller.System).Should().Be(new ActedBy("system", null));
        ActedBy.From(Caller.Anonymous).Should().Be(new ActedBy(ActedByKinds.Anonymous, null));

        FluentActions.Invoking(() => ActedBy.From(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Two_records_of_one_actor_are_equal_and_a_package_adds_kinds_of_its_own()
    {
        var seat = new ActedBy("seat", "42");

        seat.Should().Be(new ActedBy("seat", "42")).And.NotBe(new ActedBy("seat", "43")).And.NotBe(new ActedBy(ActedByKinds.User, "42"));
        var (kind, id) = seat;
        (kind, id).Should().Be(("seat", "42"));
        default(ActedBy).Kind.Should().BeNull("a record nobody filled says nothing, not somebody");
    }

    [Fact]
    public void The_accessor_reads_the_ambient_caller_each_time_it_is_asked()
    {
        var accessor = new CallerActedByAccessor();

        accessor.Current.Should().Be(new ActedBy(ActedByKinds.System, null), "outside any caller the application itself acts");
        using (Callers.Begin(Caller.User(Ada)))
        {
            accessor.Current.Should().Be(new ActedBy(ActedByKinds.User, Ada.ToString()));
            using (Callers.Begin(Caller.SystemIn("projects")))
            {
                accessor.Current.Should().Be(new ActedBy(ActedByKinds.System, "projects"));
            }

            accessor.Current.Kind.Should().Be(ActedByKinds.User, "and the one before it again afterwards");
        }

        accessor.Current.Kind.Should().Be(ActedByKinds.System);
    }

    [Fact]
    public void The_accessor_asks_the_hosts_caller_accessor_where_there_is_one()
    {
        var request = new OfTheRequest { Current = Caller.Anonymous };

        // Built by the container, which hands it the host's accessor; without one it reads the ambient caller.
        using var withHost = new ServiceCollection().AddSingleton<ICallerAccessor>(request).AddSingleton<IActedByAccessor, CallerActedByAccessor>().BuildServiceProvider(validateScopes: true);
        using var without = new ServiceCollection().AddSingleton<IActedByAccessor, CallerActedByAccessor>().BuildServiceProvider(validateScopes: true);

        using (Callers.Begin(Caller.User(Ada)))
        {
            withHost.GetRequiredService<IActedByAccessor>().Current.Should().Be(new ActedBy(ActedByKinds.Anonymous, null), "the host's accessor knows the request, and the ambient caller is not asked");
            without.GetRequiredService<IActedByAccessor>().Current.Should().Be(new ActedBy(ActedByKinds.User, Ada.ToString()));
        }

        request.Current = Caller.User(Ada);
        withHost.GetRequiredService<IActedByAccessor>().Current.Should().Be(new ActedBy(ActedByKinds.User, Ada.ToString()), "asked again, it answers for the caller there is now");

        // Where the host requires explicit callers and nobody is calling, there is nobody to name.
        using var strict = new ServiceCollection().RequireExplicitCallers().AddSingleton<ICallerAccessor, AmbientCallerAccessor>().AddSingleton<IActedByAccessor, CallerActedByAccessor>().BuildServiceProvider(validateScopes: true);
        FluentActions.Invoking(() => strict.GetRequiredService<IActedByAccessor>().Current).Should().Throw<NoCallerException>();
    }

    private sealed class OfTheRequest : ICallerAccessor
    {
        public Caller Current { get; set; } = Caller.System;
    }
}
