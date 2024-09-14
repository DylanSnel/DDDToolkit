using DDDToolkit.Interfaces;

namespace DDDToolkit.EntityFramework.Options;
public class DDDEntityFrameworkOptions
{
    public required Func<IServiceProvider, List<IDomainEvent>, Task> InterceptorAction { get; set; }
}
