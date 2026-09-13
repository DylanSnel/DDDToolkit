namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a type as part of this module's published contract, so other modules may name it. Only means
/// something in an assembly that carries <c>[assembly: Module]</c>.
/// <code>
/// [ModuleContract]
/// public readonly partial record struct CustomerId;
/// </code>
/// <para>
/// A type marked with <c>[IntegrationEvent]</c> is published as well and does not need this attribute:
/// an integration event exists to be read by somebody else.
/// </para>
/// <para>
/// Publishing a nested type publishes nothing else, but a type nested inside a published type is
/// published with it.
/// </para>
/// </summary>
/// <remarks>
/// Publishing a type is not the same as making it safe to hold. An entity or aggregate root of another
/// module stays off limits as stored state even when it is published, because a navigation to it makes
/// two modules share one load and one transaction. That reports
/// <see href="https://github.com/DylanSnel/DDDToolkit/blob/main/docs/diagnostics.md#ddd00023">DDD00023</see>.
/// Publish ids, value objects, integration events and read models instead.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Delegate,
    AllowMultiple = false,
    Inherited = false)]
public sealed class ModuleContractAttribute : Attribute
{
}
