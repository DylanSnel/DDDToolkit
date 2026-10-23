namespace DDDToolkit.Access;

/// <summary>
/// Says on a request that anyone may send it, a caller who did not sign in too: what
/// <see cref="AccessRequirement.AllowAnonymous"/> says, written where the request is declared.
/// <code>
/// [AllowAnonymousCallers]
/// public sealed record ListPlans : IBillingRequest;
/// </code>
/// </summary>
/// <remarks>
/// It is not called <c>[AllowAnonymous]</c>, because two libraries a host or an API project uses have an attribute of
/// that name: ASP.NET Core's <c>Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute</c> and HotChocolate's
/// <c>HotChocolate.Authorization.AllowAnonymousAttribute</c>. A file that imports either namespace beside
/// <c>DDDToolkit.Access</c>, as a route or a resolver that sends requests does, would not compile where it writes
/// <c>[AllowAnonymous]</c> (CS0104). It says the same: the door is open on purpose, and it reads as meant in a
/// review. Who may send the request is not what its handler runs with: a handler that does what no caller may begins
/// system work itself, in trusted code.
/// <para>
/// The toolkit's generator turns it into <c>AccessRequirement.AllowAnonymous()</c> at compile time, in the module's
/// request interface (<c>[AccessRequests]</c>, declared <c>partial</c>), so the request answers
/// <see cref="IRequireAccess.RequiredAccess"/> with it wherever that is read. A request carries one requirement:
/// this, another requirement attribute, or the property it computes itself.
/// </para>
/// </remarks>
[StatesRequirement(typeof(AccessRequirement), nameof(AccessRequirement.AllowAnonymous))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class AllowAnonymousCallersAttribute : Attribute;
