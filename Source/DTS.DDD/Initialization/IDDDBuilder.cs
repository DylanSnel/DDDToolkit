using Microsoft.Extensions.DependencyInjection;

namespace DTS.DDD.Initialization;

public interface IDDDBuilder
{
    IServiceCollection Services { get; }
    void Build(IServiceCollection applicationServices);
    void Clear();
}
