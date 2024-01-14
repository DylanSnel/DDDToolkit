using DDDToolkit.Interfaces;

namespace DDDToolkit.EntityFramework.Options;
public class DDDEntityFrameworkOptions
{
    public required Func<IServiceProvider, IDomainEvent, Task> InterceptorAction { get; set; }
}
