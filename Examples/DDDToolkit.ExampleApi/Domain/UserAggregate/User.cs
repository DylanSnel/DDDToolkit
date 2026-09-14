using System.ComponentModel.DataAnnotations;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.ExampleApi.Domain.UserAggregate.Entities;
using DDDToolkit.ExampleApi.Domain.UserAggregate.Events;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;

namespace DDDToolkit.ExampleApi.Domain.UserAggregate;

[AggregateRoot<UserId>]
public partial class User
{
    public User(UserId id, PersonName name, EmailAddress? email) : base(id)
    {
        Name = name;
        Email = email;
        RaiseDomainEvent(new UserCreated(id));
    }

    public PersonName Name { get; private set; }

    public EmailAddress? Email { get; private set; }

    [MaxLength(300)]
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>Orders placed by this user. Read-only outside the aggregate; backed by the generated <c>_orders</c> list.</summary>
    public partial IReadOnlyList<Order> Orders { get; }

    public void AddOrder(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        _orders.Add(order);
        RaiseDomainEvent(new OrderPlaced(Id, order.Id));
    }
}
