using DDDToolkit.Initialization;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit;
public static class DependencyInjection
{
    public static IServiceCollection AddDDDToolkit(this IServiceCollection services, Action<IDDDBuilder>? builderOptions)
    {
        DefaultDDDBuilder builder = new();
        builderOptions?.Invoke(builder);
        builder.Build(services);
        return services;
    }
}
