using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Inspections.Application.Access;

/// <summary>
/// What every command and query of this module implements, to say what it requires of its caller.
/// </summary>
/// <remarks>
/// The one line of access plumbing the module writes. Most requests answer <see cref="IRequireAccess.RequiredAccess"/>
/// with a case of the module's own (<see cref="InspectionsRequirement"/>), which no attribute stands for; they
/// implement the member explicitly, so the requirement is said next to the request's own fields without becoming
/// one of them. One says a case of Tenancy's with its attribute, <c>[Operator]</c>, and the interface is
/// partial because the toolkit's generator writes <see cref="IRequireAccess.RequiredAccess"/> into it for that one,
/// beside the pipeline behavior, <c>InspectionsAccessBehavior&lt;TMessage, TResponse&gt;</c>, which asks the checks
/// registered for this interface before every handler of the module.
/// <para>
/// A request that implemented no such interface would pass the behavior untouched, which is why a test fails
/// for it: every command and query declares what it requires.
/// </para>
/// </remarks>
[AccessRequests]
public partial interface IInspectionsRequest : IRequireAccess;
