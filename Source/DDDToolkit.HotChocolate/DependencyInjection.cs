using DDDToolkit.HotChocolate.Interceptors;
using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate;

/// <summary>Wires the DDDToolkit types and conventions into a HotChocolate schema.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the toolkit's schema conventions: the <see cref="IgnoreInternalFieldsInterceptor"/>,
    /// which hides every member marked <c>[Internal]</c>, and the <c>DomainEvent</c> interface type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This registers conventions only. The scalar bindings and type converters for your ids and
    /// single value objects are generated per assembly by DDDToolkit.HotChocolate.Analyzers; call
    /// that assembly's <c>Add{Module}GraphQlRuntimeBindings()</c> as well — once per assembly that
    /// declares them:
    /// </para>
    /// <code>
    /// services
    ///     .AddGraphQLServer()
    ///     .AddDDDToolkitTypes()
    ///     .AddCommonGraphQlRuntimeBindings()   // generated into {Assembly}.GraphQl
    ///     .AddQueryType&lt;Query&gt;();
    /// </code>
    /// <para>Calling it more than once on the same builder is harmless.</para>
    /// </remarks>
    /// <param name="builder">The request executor builder to configure.</param>
    /// <returns>The same builder, so calls can be chained.</returns>
    public static IRequestExecutorBuilder AddDDDToolkitTypes(this IRequestExecutorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.TryAddTypeInterceptor(typeof(IgnoreInternalFieldsInterceptor));
        builder.AddHotChocolateTypes();
        return builder;
    }
}
