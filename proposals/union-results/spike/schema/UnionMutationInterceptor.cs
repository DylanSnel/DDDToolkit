using System.Reflection;
using System.Runtime.CompilerServices;
using HotChocolate;
using HotChocolate.Configuration;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;

// Prototype: a mutation field whose resolver returns a C# union gets the success case as its type and each error
// case as an error type of the mutation. The formatter turns the union value into an IFieldResult for the conventions.
public sealed class UnionMutationInterceptor : TypeInterceptor
{
    public override void OnBeforeRegisterDependencies(ITypeDiscoveryContext discoveryContext, TypeSystemConfiguration configuration)
    {
        if (configuration is not ObjectTypeConfiguration { Name: "Mutation" } type)
        {
            return;
        }

        foreach (var field in type.Fields)
        {
            if (field.Type is not ExtendedTypeReference inferred || UnionOf(inferred.Type.Type) is not { } union)
            {
                continue;
            }

            var cases = CasesOf(union);
            var success = cases.Single(c => !typeof(IDomainError).IsAssignableFrom(c));
            field.Type = inferred.WithType(discoveryContext.TypeInspector.GetType(success));
            foreach (var error in cases.Where(c => typeof(IDomainError).IsAssignableFrom(c)))
            {
                field.AddErrorType(discoveryContext.DescriptorContext, error);
            }
            field.FormatterConfigurations.Add(new ResultFormatterConfiguration(ToFieldResult, isRepeatable: false, key: "union"));
        }
    }

    private static Type? UnionOf(Type type)
    {
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            type = type.GetGenericArguments()[0];
        }
        return type.GetCustomAttribute<UnionAttribute>() is not null ? type : null;
    }

    // The cases are the parameter types of the public constructors with one parameter.
    private static Type[] CasesOf(Type union)
        => [.. union.GetConstructors().Where(c => c.GetParameters().Length == 1).Select(c => c.GetParameters()[0].ParameterType)];

    private static object? ToFieldResult(IResolverContext context, object? result)
        => result is IUnion { Value: var value }
            ? new UnionFieldResult(value is IDomainError ? new[] { value } : value, value is IDomainError)
            : result;

    private readonly record struct UnionFieldResult(object? Value, bool IsError) : IFieldResult
    {
        public bool IsSuccess => !IsError;
    }
}
