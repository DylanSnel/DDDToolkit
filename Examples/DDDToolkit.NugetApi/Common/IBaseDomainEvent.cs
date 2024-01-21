using DDDToolkit.Interfaces;
using MediatR;

namespace DDDToolkit.NugetApi.Common;
public interface IBaseDomainEvent : IDomainEvent, INotification
{
}
