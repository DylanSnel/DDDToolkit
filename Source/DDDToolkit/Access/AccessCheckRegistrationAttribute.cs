namespace DDDToolkit.Access;

/// <summary>
/// Says, on the requirement a package ships, which registration of the package adds the check that decides
/// its cases. Read for one message only: a request whose requirement none of its module's checks decides is
/// stopped (<see cref="AccessChecks{TRequests}"/>), and the message then names that registration rather than
/// the general <c>AddAccessCheck&lt;TRequests, TCheck&gt;()</c>, which leaves the reader to find the check.
/// <code>
/// [AccessCheckRegistration("services.AddTenancyAccess&lt;{TRequests}, TContext&gt;()")]
/// public abstract partial record TenancyRequirement : AccessRequirement
/// </code>
/// </summary>
/// <remarks>
/// Put it on the requirement the cases derive from: it is inherited, so every case says it. <c>{TRequests}</c>
/// is replaced by the request interface the checks were registered for, as its author writes it; the rest is
/// said as it is, so the call reads as a reader would write it.
/// </remarks>
/// <param name="registration">The call that adds the check, with <c>{TRequests}</c> where the module's request interface goes.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class AccessCheckRegistrationAttribute(string registration) : Attribute
{
    /// <summary>The call that adds the check, with <c>{TRequests}</c> where the module's request interface goes.</summary>
    public string Registration { get; } = registration;
}
