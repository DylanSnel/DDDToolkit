namespace DDDToolkit.Access;

/// <summary>
/// Says on a request that anyone may send it, a caller who did not sign in too: what
/// <see cref="AccessRequirement.AllowAnonymous"/> says, written where the request is declared.
/// <code>
/// [AllowAnonymous]
/// public sealed record ListPlans : IBillingRequest;
/// </code>
/// </summary>
/// <remarks>
/// Named as ASP.NET Core's and HotChocolate's attributes are, and for the same reason: it says that the door is open on
/// purpose, and it reads as meant in a review. It goes on a request, which is declared in the application layer, and
/// that layer references neither of those libraries, so the name is free there. A file that imports
/// <c>Microsoft.AspNetCore.Authorization</c> or <c>HotChocolate.Authorization</c> beside <c>DDDToolkit.Access</c>, a
/// route or a resolver that declares a request of its own, finds two attributes of the name (CS0104): write it in full,
/// <c>[DDDToolkit.Access.AllowAnonymous]</c>, or name one with an alias,
/// <c>using AllowAnonymous = DDDToolkit.Access.AllowAnonymousAttribute;</c>. Who may send the request is not what its
/// handler runs with: a handler that does what no caller may begins system work itself, in trusted code.
/// <para>
/// The toolkit's generator turns it into <c>AccessRequirement.AllowAnonymous()</c> at compile time, in the module's
/// request interface (<c>[AccessRequests]</c>, declared <c>partial</c>), so the request answers
/// <see cref="IRequireAccess.RequiredAccess"/> with it wherever that is read. Requirement attributes stacked on one
/// request all hold, so this one goes with no other: beside a key it would open the door the key closes, and the
/// generator reports it (DDD00079).
/// </para>
/// </remarks>
[StatesRequirement(typeof(AccessRequirement), nameof(AccessRequirement.AllowAnonymous))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class AllowAnonymousAttribute : Attribute;
