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
        ActedBy.From(CurrentUserSnapshot.User(Ada)).Should().Be(new ActedBy(ActedByKinds.User, "ada00000-0000-4000-8000-000000000001"));
        ActedBy.From(CurrentUserSnapshot.FromClaims("""{"sub":"auth0|ada","role":"authenticated"}""")).Should().Be(new ActedBy("user", "auth0|ada"), "a user whose id is no uuid is kept by the token's sub");
        ActedBy.From(CurrentUserSnapshot.User(id: null)).Should().Be(new ActedBy("user", null), "a user nobody can name is still a user");
        ActedBy.From(CurrentUserSnapshot.SystemIn("projects")).Should().Be(new ActedBy(ActedByKinds.System, "projects"), "scoped system work is the system, in its scope");
        ActedBy.From(CurrentUserSnapshot.System).Should().Be(new ActedBy("system", null));
        ActedBy.From(CurrentUserSnapshot.Anonymous).Should().Be(new ActedBy(ActedByKinds.Anonymous, null));

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
        var accessor = new CurrentUserActedByAccessor();

        accessor.Current.Should().Be(new ActedBy(ActedByKinds.System, null), "outside any caller the application itself acts");
        using (TestUser.BeginAs(Ada))
        {
            accessor.Current.Should().Be(new ActedBy(ActedByKinds.User, Ada.ToString()));
            using (TestUser.BeginSystem("projects"))
            {
                accessor.Current.Should().Be(new ActedBy(ActedByKinds.System, "projects"));
            }

            accessor.Current.Kind.Should().Be(ActedByKinds.User, "and the one before it again afterwards");
        }

        accessor.Current.Kind.Should().Be(ActedByKinds.System);
    }

    [Fact]
    public void The_accessor_asks_the_current_user_which_answers_a_begun_user_first_and_the_host_where_nothing_is_begun()
    {
        var request = new OfTheRequest { Current = CurrentUserSnapshot.Anonymous };

        // Built by the container, which hands it the current user, which asks the host's source; without either it makes
        // a current user of its own.
        using var withHost = new ServiceCollection().AddSingleton<ICurrentUserSource>(request).AddCurrentUser().AddSingleton<IActedByAccessor, CurrentUserActedByAccessor>().BuildServiceProvider(validateScopes: true);
        using var without = new ServiceCollection().AddSingleton<IActedByAccessor, CurrentUserActedByAccessor>().BuildServiceProvider(validateScopes: true);

        withHost.GetRequiredService<IActedByAccessor>().Current.Should().Be(new ActedBy(ActedByKinds.Anonymous, null), "where nothing is begun, the host's answer is who acts");
        without.GetRequiredService<IActedByAccessor>().Current.Should().Be(new ActedBy(ActedByKinds.System, null), "without a host, work that began nothing is system work");

        using (TestUser.BeginAs(Ada))
        {
            withHost.GetRequiredService<IActedByAccessor>().Current.Should().Be(new ActedBy(ActedByKinds.User, Ada.ToString()), "a user begun for the flow wins over the host's answer");
            without.GetRequiredService<IActedByAccessor>().Current.Should().Be(new ActedBy(ActedByKinds.User, Ada.ToString()));
        }

        request.Current = CurrentUserSnapshot.User(Ada);
        withHost.GetRequiredService<IActedByAccessor>().Current.Should().Be(new ActedBy(ActedByKinds.User, Ada.ToString()), "asked again, it answers for the user there is now");

        // Where the host requires explicit callers and nobody is calling, there is nobody to name.
        using var strict = new ServiceCollection().RequireExplicitCurrentUser().AddSingleton<ICurrentUser, CurrentUser>().AddSingleton<IActedByAccessor, CurrentUserActedByAccessor>().BuildServiceProvider(validateScopes: true);
        FluentActions.Invoking(() => strict.GetRequiredService<IActedByAccessor>().Current).Should().Throw<NoCurrentUserException>();
    }

    private sealed class OfTheRequest : ICurrentUserSource
    {
        public CurrentUserSnapshot? Current { get; set; } = CurrentUserSnapshot.System;
    }
}
