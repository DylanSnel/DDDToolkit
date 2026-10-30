using System.Diagnostics.CodeAnalysis;

namespace DDDToolkit.Abstractions.Access;

/// <summary>
/// Makes the <see cref="IRowUser"/> a test hands a row access rule, to run the rule in C#:
/// <code>
/// ACustomerHasTheirOrders.Allows(order, RowUser.From(CurrentUserSnapshot.User(alice))).Should().BeTrue();
/// </code>
/// </summary>
/// <remarks>
/// It answers what the rule can answer in C#. A question only the database answers still throws
/// <see cref="DatabaseOnlyException"/>: ask the database for that, by loading the row through a context that runs as the
/// caller. <see cref="CurrentUserSnapshot"/> does not implement <see cref="IRowUser"/>, so a call to this method is what
/// gives a rule a user outside the database.
/// </remarks>
public static class RowUser
{
    /// <summary>The user a rule sees for <paramref name="user"/>, with the same answers.</summary>
    /// <param name="user">Who the rule is asked about.</param>
    /// <exception cref="ArgumentNullException"><paramref name="user"/> is null.</exception>
    public static IRowUser From(CurrentUserSnapshot user)
        => new Answering(user ?? throw new ArgumentNullException(nameof(user)));

    private sealed class Answering(CurrentUserSnapshot user) : IRowUser
    {
        public Guid? Id => user.Id;

        [MemberNotNullWhen(true, nameof(Id))]
        public bool IsAuthenticated => Id.HasValue;

        public string? Role => user.Role;

        public string? FindClaim(string path) => user.FindClaim(path);
    }
}
