using DDDToolkit.Abstractions.Attributes;
using HotChocolate.Configuration;
using HotChocolate.Types.Descriptors.Definitions;
using System.Reflection;

namespace DDDToolkit.HotChocolate.Interceptors;

/// <summary>
/// Ignores all fields with the InternalAttribute
/// </summary>
public class IgnoreInternalFieldsInterceptor : TypeInterceptor
{
    public override void OnBeforeCompleteType(
        ITypeCompletionContext completionContext,
        DefinitionBase definition)
    {
        if (definition is ObjectTypeDefinition objectTypeDef)
        {
            foreach (var field in objectTypeDef.Fields)
            {
                var memberInfo = field.Member;
                if (memberInfo != null && memberInfo.GetCustomAttribute<InternalAttribute>() != null)
                {
                    field.Ignore = true;
                }
            }
        }
    }
}
