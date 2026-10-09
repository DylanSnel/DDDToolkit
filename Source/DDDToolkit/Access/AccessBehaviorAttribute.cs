namespace DDDToolkit.Access;

/// <summary>
/// Says which pipeline behavior asks the access checks of a request interface, so the start-up check
/// <see cref="AccessBehaviorChecks.BehaviorsRegisteredCheck"/> can hold a host to having it in the pipeline. The
/// toolkit's generator writes it into the assembly that declares an interface marked <c>[AccessRequests]</c>, beside
/// the behavior it writes there:
/// <code>
/// [assembly: AccessBehavior(typeof(IBillingRequest), typeof(BillingAccessBehavior&lt;,&gt;),
///     StreamBehavior = typeof(BillingAccessStreamBehavior&lt;,&gt;),
///     Registration = "services.AddBillingAccessBehavior()")]
/// </code>
/// </summary>
/// <remarks>
/// It says what was written and changes nothing by itself. A host that handles a request of the interface without the
/// behavior in its pipeline has a request that reaches its handler with nothing asking what it requires; the start-up
/// check reads this to tell, and to name the line that puts the behavior in.
/// </remarks>
/// <param name="requests">The request interface whose checks the behavior asks.</param>
/// <param name="behavior">The behavior, as a generic type definition: <c>typeof(BillingAccessBehavior&lt;,&gt;)</c>.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class AccessBehaviorAttribute(Type requests, Type behavior) : Attribute
{
    /// <summary>The request interface whose checks the behavior asks.</summary>
    public Type Requests { get; } = requests;

    /// <summary>The behavior in the pipeline of commands and queries, as a generic type definition.</summary>
    public Type Behavior { get; } = behavior;

    /// <summary>
    /// The behavior in the pipeline of the messages answered with a stream, where the library has that pipeline, or
    /// null where it has none.
    /// </summary>
    public Type? StreamBehavior { get; set; }

    /// <summary>
    /// The call that puts the behaviors in their pipelines, as a reader writes it, or null where none was written:
    /// <c>services.AddBillingAccessBehavior()</c>.
    /// </summary>
    public string? Registration { get; set; }
}
