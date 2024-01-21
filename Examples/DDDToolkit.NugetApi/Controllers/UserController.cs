using DDDToolkit.NugetApi.Context;
using Microsoft.AspNetCore.Mvc;

namespace DDDToolkit.NugetApi.Controllers;
[Route("api/[controller]")]
[ApiController]
public class UserController(ExampleContext context) : ControllerBase
{

    [HttpGet(Name = "CreateUser")]
    public IActionResult Post()
    {

        //var product = new Product(ProductId.CreateUnique())
        //{
        //    Name = "Test",
        //    Price = 10
        //};


        //context.Products.Add(product);
        //context.SaveChanges();
        //var user = new User(UserId.CreateUnique(), new PersonName("John", "Doe"), EmailAddress.Create("johndoe@gmail.com"));

        //var order = new Order(OrderId.CreateUnique(), [ProductId.CreateUnique(), ProductId.CreateUnique(), ProductId.CreateUnique()])
        //{
        //};

        //user.AddOrder(order);

        //context.Users.Add(user);
        //context.SaveChanges();


        return Ok();
    }
}
