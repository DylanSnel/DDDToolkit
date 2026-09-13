using DDDToolkit.Interfaces;
using Mediator;

namespace DDDToolkit.ExampleLibrary.Common;

/// <summary>
/// The marker every domain event in this example carries. <c>IDomainEvent</c> is the toolkit's;
/// <c>INotification</c> is Mediator's, and it is what lets <c>DispatchWithMediator()</c> publish the
/// event. Declaring it once here means no individual event has to remember.
/// </summary>
public interface IBaseDomainEvent : IDomainEvent, INotification
{
}
