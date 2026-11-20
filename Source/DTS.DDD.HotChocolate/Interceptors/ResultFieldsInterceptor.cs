using DTS.DDD.Results;
using HotChocolate.Configuration;
using HotChocolate.Internal;
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;

namespace DTS.DDD.HotChocolate.Interceptors;

/// <summary>
/// A field whose resolver returns a <see cref="DTS.DDD.Results.Result{T}"/> has the type of <c>T</c> in the schema, and a field that
/// returns a <see cref="DTS.DDD.Results.Result"/> is a <c>Boolean</c> that is <c>true</c> after a success. A failure becomes a GraphQL
/// error: the formatter throws the error as an <see cref="ErrorException"/>, and the toolkit's error filter writes its
/// code, kind, field and arguments.
/// </summary>
/// <remarks>The resolver may return the result directly or in a <see cref="Task{T}"/> or <see cref="ValueTask{T}"/>.</remarks>
public sealed class ResultFieldsInterceptor : TypeInterceptor
{
    private const string FormatterKey = "dts.ddd.result";

    private static readonly ResultFormatterConfiguration Unwrap = new(UnwrapResult, isRepeatable: false, key: FormatterKey);

    /// <inheritdoc />
    public override void OnBeforeRegisterDependencies(ITypeDiscoveryContext discoveryContext, TypeSystemConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(discoveryContext);

        if (configuration is not ObjectTypeConfiguration type)
        {
            return;
        }

        foreach (var field in type.Fields)
        {
            if (field.IsIntrospectionField || field.Type is not ExtendedTypeReference inferred || ValueTypeOf(inferred.Type, discoveryContext.TypeInspector) is not { } value)
            {
                continue;
            }

            field.Type = inferred.WithType(value);
            field.FormatterConfigurations.Add(Unwrap);
        }
    }

    // The type a field shows for a result: T of Result<T>, Boolean for Result, null for every other type.
    private static IExtendedType? ValueTypeOf(IExtendedType type, ITypeInspector inspector)
    {
        if (type.Type == typeof(DTS.DDD.Results.Result))
        {
            return inspector.GetType(typeof(bool));
        }

        return type.Type.IsGenericType && type.Type.GetGenericTypeDefinition() == typeof(DTS.DDD.Results.Result<>)
            ? type.TypeArguments[0]
            : null;
    }

    private static object? UnwrapResult(IResolverContext context, object? result)
        => result switch
        {
            IResultState { ErrorOrNull: { } error } => throw error.ToException(),
            DTS.DDD.Results.Result => true,
            IResultState success => success.ValueOrNull,
            _ => result,
        };
}