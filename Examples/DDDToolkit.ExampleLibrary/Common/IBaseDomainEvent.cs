using DDDToolkit.Interfaces;
using MediatR;

namespace DDDToolkit.ExampleLibrary.Common;
public interface IBaseDomainEvent : IDomainEvent, INotification
{
}
