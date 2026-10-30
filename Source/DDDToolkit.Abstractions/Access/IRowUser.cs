using System.Diagnostics.CodeAnalysis;

namespace DDDToolkit.Abstractions.Access;

/// <summary>
/// Who a row access rule asks about: the caller, as the database knows them. A rule takes it as its second parameter,
/// and everything the rule asks, it asks of it:
/// <code>
/// [RowAccess&lt;Board&gt;(RowOperations.Read, To = [RowAccessRoles.User])]
/// public static partial class SeatsReadTheBoardsTheySee
/// {
///     public static bool Allows(Board board, IRowUser user) =&gt; user.Sees&lt;BoardId&gt;().Contains(board.Id);
/// }
/// </code>
/// A question only the database answers is an extension method on this interface, such as
/// <see cref="ResourceQuestions.Sees{TKey}"/> or a method of an <c>[AccessFunctions]</c> class. So it can only be called
/// where a rule has its user: <see cref="ICurrentUser"/> does not derive from this interface, and this one does not
/// derive from it, so <c>currentUser.Sees&lt;BoardId&gt;()</c> in a handler does not compile.
/// </summary>
/// <remarks>
/// The members are what the generator translates: <see cref="Id"/>, <see cref="IsAuthenticated"/>, <see cref="Role"/> and
/// <see cref="FindClaim"/>. They hold nothing else, so <c>user.RequireId()</c>, <c>user.IsSystem</c> and <c>user.Kind</c>
/// do not compile in a rule. A rule still runs in C# for what it can answer: a test makes the user with
/// <see cref="RowUser.From"/>.
/// </remarks>
public interface IRowUser
{
    /// <summary>
    /// The signed-in user's id, the <c>sub</c> of their token: <c>auth.uid()</c> in a policy. <see langword="null"/> for
    /// somebody who has not signed in, and for a token whose <c>sub</c> is no <see cref="Guid"/>.
    /// </summary>
    Guid? Id { get; }

    /// <summary>
    /// Whether a user signed in, which is <c>auth.uid() IS NOT NULL</c> in a policy. It is true when <see cref="Id"/> has
    /// a value, and after it answers true the compiler treats <see cref="Id"/> as not null.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Id))]
    bool IsAuthenticated { get; }

    /// <summary>
    /// The role the user's token carries: <c>auth.role()</c> in a policy. <see langword="null"/> for a token without a
    /// role claim.
    /// </summary>
    string? Role { get; }

    /// <summary>
    /// A claim of the user's token, as text, or <see langword="null"/> when the token has no such claim.
    /// <c>FindClaim("email")</c> is <c>auth.jwt() -&gt;&gt; 'email'</c> in a policy, and <c>FindClaim("app_metadata.role")</c>
    /// is <c>auth.jwt() #&gt;&gt; '{app_metadata,role}'</c>.
    /// </summary>
    /// <param name="path">The claim's name, or a path of names separated by dots.</param>
    string? FindClaim(string path);
}
