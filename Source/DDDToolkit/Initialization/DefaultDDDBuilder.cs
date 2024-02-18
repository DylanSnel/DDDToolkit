using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Initialization;

public class DefaultDDDBuilder : IDDDBuilder
{
    public IServiceCollection Services { get; }

    public DefaultDDDBuilder()
    {
        Services = new ServiceCollection();
    }

    public void Build(IServiceCollection applicationServices)
    {
        foreach (var service in Services)
        {
            applicationServices.Add(service);
        }
    }

    public void Clear()
    {
        Services.Clear();
    }
}
