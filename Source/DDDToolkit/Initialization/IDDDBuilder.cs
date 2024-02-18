using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Initialization;

public interface IDDDBuilder
{
    IServiceCollection Services { get; }
    void Build(IServiceCollection applicationServices);
    void Clear();
}
