global using static Examples.Tenancy.Tests.Infrastructure.CurrentUserOfTests;

using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// The current user these tests begin work as, outside any service provider: <c>TestUser.BeginAs(id)</c>,
/// <c>TestUser.BeginSystem()</c>. What it begins follows the flow of work, so every <see cref="ICurrentUser"/> sees it.
/// </summary>
internal static class CurrentUserOfTests
{
    /// <summary>The toolkit's current user, with no options and no source.</summary>
    public static ICurrentUser TestUser { get; } = new CurrentUser();

    /// <summary>The user begun for the flow of work, or <see langword="null"/> where nothing is begun.</summary>
    public static CurrentUserSnapshot? BegunUser => TestUser.IsBegun ? TestUser.Snapshot : null;

    /// <summary>A current user whose host answers <paramref name="user"/> where nothing is begun, as a request's would.</summary>
    public static ICurrentUser Answering(CurrentUserSnapshot user) => new CurrentUser(source: new Fixed(user));

    /// <summary>A host that answers one user.</summary>
    private sealed class Fixed(CurrentUserSnapshot user) : ICurrentUserSource
    {
        public CurrentUserSnapshot? Current => user;
    }
}
