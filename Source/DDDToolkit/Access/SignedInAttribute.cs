namespace DDDToolkit.Access;

/// <summary>
/// Says on a request that its caller is a signed-in user, who need not hold anything yet: what
/// <see cref="AccessRequirement.SignedIn"/> says, written where the request is declared.
/// <code>
/// [SignedIn]
/// public sealed record MyInvoices : IBillingRequest;
/// </code>
/// Anyone else is refused with <see cref="Exceptions.ToolkitRefusals.NotSignedIn"/>: a caller who did not sign in,
/// and the application's own work, which is nobody's sign-in.
/// </summary>
/// <remarks>
/// The toolkit's generator turns it into <c>AccessRequirement.SignedIn()</c> at compile time, in the module's
/// request interface (<c>[AccessRequests]</c>, declared <c>partial</c>), so the request answers
/// <see cref="IRequireAccess.RequiredAccess"/> with it wherever that is read. Stacked beside another requirement
/// attribute both hold, <c>AccessRequirement.All(...)</c>; beside one that already lets only a signed-in user through,
/// <c>[Operator]</c> say, it says nothing more, and the generator warns of it (DDD00084). On the module's
/// interface, <c>[AccessRequests, SignedIn]</c>, it is the module's default: a request of it that states nothing of its
/// own requires a signed-in user.
/// </remarks>
[StatesRequirement(typeof(AccessRequirement), nameof(AccessRequirement.SignedIn))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
public sealed class SignedInAttribute : Attribute;
