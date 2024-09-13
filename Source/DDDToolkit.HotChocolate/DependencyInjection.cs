using DDDToolkit.HotChocolate.Interceptors;
using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate;
public static class DependencyInjection
{
    public static IRequestExecutorBuilder AddDDDToolkitTypes(this IRequestExecutorBuilder builder)
    {
        builder.TryAddTypeInterceptor(new IgnoreInternalFieldsInterceptor());
        builder.AddHotChocolateTypes();
        return builder;
    }


}
