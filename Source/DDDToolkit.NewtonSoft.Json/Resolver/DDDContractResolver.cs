using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace DDDToolkit.NewtonSoft.Json.Resolver;
public class DDDContractResolver : DefaultContractResolver
{
    private readonly List<Type> _dddTypes = [typeof(IValueObject), typeof(IEntity)];

    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        var property = base.CreateProperty(member, memberSerialization);

        // Check if the declaring type implements IValueObject
        var implementsIValueObject = _dddTypes.Any(x => x.IsAssignableFrom(property.DeclaringType));

        if (implementsIValueObject)
        {
            // Check for the InternalAttribute to ignore the property
            var hasInternalAttribute = member.GetCustomAttributes(typeof(InternalAttribute), true).Any();
            if (hasInternalAttribute)
            {
                property.Ignored = true; // Ignore properties with InternalAttribute
            }
            else
            {
                // Enable deserialization for protected/internal/private setters if they don't have the InternalAttribute
                if (!property.Writable)
                {
                    var propertyInfo = member as PropertyInfo;
                    var hasNonPublicSetter = propertyInfo?.GetSetMethod(true) != null;
                    if (hasNonPublicSetter)
                    {
                        property.Writable = true;
                    }
                }
            }
        }

        return property;
    }
}
