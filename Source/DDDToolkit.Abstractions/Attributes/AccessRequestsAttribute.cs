namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks the interface the commands and queries of one module implement to say what they require of their
/// caller, so the generator writes the pipeline behavior that holds them to it:
/// <code>
/// [AccessRequests]
/// public interface IBillingRequest : IRequireAccess;
/// </code>
/// In a project that references the Mediator library (<c>Mediator.Abstractions</c>), the generator writes
/// <c>BillingAccessBehavior&lt;TMessage, TResponse&gt;</c> beside the interface, for every message that
/// implements it and for no other, and <c>services.AddBillingAccessBehavior()</c>, which puts it in the
/// pipeline. The behavior asks the module's access checks (<c>AccessChecks&lt;IBillingRequest&gt;</c>) and then
/// runs the next step, so a request reaches its handler only when the caller meets what it declared.
/// <para>
/// The behavior is named after the interface: <c>IBillingRequest</c> gives <c>BillingAccessBehavior</c>. It is
/// as visible as the interface is.
/// </para>
/// <para>
/// The library sends a message that is answered with a stream (<c>IStreamQuery&lt;T&gt;</c> and the like)
/// through a pipeline of its own, so a second class is written for that one,
/// <c>BillingAccessStreamBehavior&lt;TMessage, TResponse&gt;</c>, which asks the same checks before the handler
/// streams anything. The one registration adds both.
/// </para>
/// <para>
/// In a project that does not reference the library nothing is generated, and the attribute changes nothing:
/// the interface still says which checks a request is held to, and the application calls
/// <c>AccessChecks&lt;IBillingRequest&gt;.RequireAsync(request, cancellationToken)</c> in front of its handlers
/// itself, from its own dispatcher, an endpoint filter or a behavior of another library.
/// </para>
/// </summary>
/// <remarks>
/// The interface derives from <c>DDDToolkit.Access.IRequireAccess</c>, is not declared inside another type,
/// is not file-local and has no type parameters. One that is shaped otherwise is reported, and no behavior is
/// written for it. A notification of the library is published through no pipeline, so one that implements
/// the interface is reported as well: nothing would ask what it requires.
/// </remarks>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class AccessRequestsAttribute : Attribute;
