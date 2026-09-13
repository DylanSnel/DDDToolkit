using DDDToolkit.Exceptions;
using DDDToolkit.ExampleApi.Context;
using DDDToolkit.ExampleApi.Domain.ProductAggregate;
using DDDToolkit.ExampleApi.Domain.ProductAggregate.ValueObjects;
using DDDToolkit.ExampleApi.Domain.UserAggregate;
using DDDToolkit.ExampleApi.Domain.UserAggregate.Entities;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.ExampleApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UserController(ExampleContext context) : ControllerBase
{
    /// <summary>
    /// Creates a product and a user with one order. Saving the user raises UserCreated and OrderPlaced,
    /// which the interceptor dispatches through MediatR before the rows are written (see Program.cs),
    /// and sets User.Version to 1.
    /// </summary>
    [HttpPost(Name = "CreateUser")]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        var product = new Product(ProductId.CreateUnique())
        {
            Name = "Test",
            Price = 10,
        };
        context.Products.Add(product);

        var user = new User(UserId.CreateUnique(), new PersonName("John", "Doe"), EmailAddress.Create("johndoe@gmail.com"));
        user.AddOrder(new Order(OrderId.CreateUnique(), [product.Id, ProductId.CreateUnique()]));
        context.Users.Add(user);

        await context.SaveChangesAsync(cancellationToken);

        return CreatedAtRoute("GetUser", new { id = user.Id.ToString() }, new { id = user.Id.ToString(), version = user.Version });
    }

    /// <summary>Loads a user by id; the id is accepted with or without its prefix (USER_...).</summary>
    [HttpGet("{id}", Name = "GetUser")]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        if (!UserId.TryParse(id, out var userId))
        {
            return BadRequest($"'{id}' is not a valid user id.");
        }

        var user = await context.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            id = user.Id.ToString(),
            name = user.Name.FullName,
            email = user.Email?.Value,
            version = user.Version,
            orders = user.Orders.Select(o => new { id = o.Id.ToString(), products = o.Products.Select(p => p.ToString()), o.PlacedAt }),
        });
    }

    /// <summary>
    /// Adds an order to an existing user. Saving bumps User.Version; a concurrent change of the same
    /// user surfaces as a ConcurrencyConflictException, translated here into 409 Conflict.
    /// </summary>
    [HttpPost("{id}/orders", Name = "PlaceOrder")]
    public async Task<IActionResult> PlaceOrder(string id, CancellationToken cancellationToken)
    {
        if (!UserId.TryParse(id, out var userId))
        {
            return BadRequest($"'{id}' is not a valid user id.");
        }

        var user = await context.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        user.AddOrder(new Order(OrderId.CreateUnique(), [ProductId.CreateUnique()]));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Conflict(exception.Message);
        }

        return Ok(new { id = user.Id.ToString(), version = user.Version, orders = user.Orders.Count });
    }
}
