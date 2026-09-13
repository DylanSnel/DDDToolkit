using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace DDDToolkit.NewtonSoft.Json.Resolver;

/// <summary>
/// Contract resolver for domain types. On value objects, entities and ids it
/// <list type="bullet">
/// <item>skips every member marked <c>[Internal]</c> - the bookkeeping of the base types
/// (<c>IsValid</c>, <c>IsValidated</c>, an aggregate's <c>DomainEvents</c>) is not part of the document;</item>
/// <item>and lets the rest be written back through a non-public setter, which is how domain types keep
/// their state private while still being deserializable.</item>
/// </list>
/// Struct ids are left alone: they are immutable and their converter replaces them wholesale, so promoting
/// a setter on them would only ever write to a copy.
/// </summary>
public class DDDContractResolver : DefaultContractResolver
{
    private static readonly Type[] DomainTypes = [typeof(IValueObject), typeof(IEntity), typeof(IEntityId)];

    /// <inheritdoc />
    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        ArgumentNullException.ThrowIfNull(member);

        var property = base.CreateProperty(member, memberSerialization);

        var declaringType = property.DeclaringType;
        if (declaringType is null || !Array.Exists(DomainTypes, domainType => domainType.IsAssignableFrom(declaringType)))
        {
            return property;
        }

        if (member.GetCustomAttributes(typeof(InternalAttribute), inherit: true).Length != 0)
        {
            property.Ignored = true;
            return property;
        }

        // Enable deserialization through protected/internal/private setters. Never for value types: the
        // setter would run against a boxed copy and the write would be lost.
        if (!property.Writable && !declaringType.IsValueType && member is PropertyInfo propertyInfo && propertyInfo.GetSetMethod(nonPublic: true) is not null)
        {
            property.Writable = true;
        }

        return property;
    }
}
