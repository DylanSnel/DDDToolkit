﻿using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.NugetApi.Common.ValueObjects;
using DDDToolkit.NugetApi.Domain.UserAggregate.Entities;
using DDDToolkit.NugetApi.Domain.UserAggregate.ValueObjects;
using System.ComponentModel.DataAnnotations;

namespace DDDToolkit.NugetApi.Domain.UserAggregate;

[AggregateRoot<UserId>()]
public partial class User
{

    public User(UserId id, PersonName name, EmailAddress? email) : base(id)
    {
        Name = name;
        Email = email;
    }


    public PersonName Name { get; private set; }

    public EmailAddress? Email { get; private set; }

    [MaxLength(300)]
    public string PasswordHash { get; private set; } = string.Empty;

    public List<Order> Orders { get; private set; } = new();

    public void AddOrder(Order order)
    {
        Orders.Add(order);
    }


}
