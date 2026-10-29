global using static AccessOfTests;

using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;

/// <summary>
/// The access manager these tests begin work with, outside any service provider: <c>TestAccess.BeginAs(id)</c>,
/// <c>TestAccess.BeginSystem(scope)</c>. What it begins follows the flow of work, so every <see cref="ICurrentUser"/> sees it.
/// </summary>
internal static class AccessOfTests
{
    /// <summary>The core's access manager, over the toolkit's current user with no options and no source.</summary>
    public static AccessManager TestAccess { get; } = new(new CurrentUser());

    /// <summary>The toolkit's current user, with no options and no source: what <see cref="TestAccess"/> answers.</summary>
    public static ICurrentUser TestUser => TestAccess.CurrentUser;

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
