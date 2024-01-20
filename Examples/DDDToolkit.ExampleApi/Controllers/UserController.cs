using DDDToolkit.ExampleApi.Context;
using DDDToolkit.ExampleApi.Domain.ProductAggregate;
using DDDToolkit.ExampleApi.Domain.ProductAggregate.ValueObjects;
using DDDToolkit.ExampleApi.Domain.UserAggregate;
using DDDToolkit.ExampleApi.Domain.UserAggregate.Entities;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace DDDToolkit.ExampleApi.Controllers;
[Route("api/[controller]")]
[ApiController]
public class UserController(ExampleContext context) : ControllerBase
{

    [HttpGet(Name = "CreateUser")]
    public IActionResult Post()
    {

        var product = new Product(ProductId.CreateUnique())
        {
            Name = "Test",
            Price = 10
        };


        context.Products.Add(product);
        context.SaveChanges();
        var user = new User(UserId.CreateUnique(), new PersonName("John", "Doe"), EmailAddress.Create("Dylansnel@gmail.com"));

        var order = new Order()
        {
            Products = /*[1, 2, 3]//*/[ProductId.CreateUnique(), ProductId.CreateUnique(), ProductId.CreateUnique()]
        };

        user.AddOrder(order);

        context.Users.Add(user);
        context.SaveChanges();


        return Ok();
    }
}
