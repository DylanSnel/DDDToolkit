using System.ComponentModel.DataAnnotations;
using DTS.DDD.Abstractions.Attributes;
using DTS.DDD.ExampleApi.Domain.UserAggregate.Entities;
using DTS.DDD.ExampleApi.Domain.UserAggregate.Events;
using DTS.DDD.ExampleApi.Domain.UserAggregate.ValueObjects;
using DTS.DDD.ExampleLibrary.Common.ValueObjects;

namespace DTS.DDD.ExampleApi.Domain.UserAggregate;

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
