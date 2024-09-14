using DDDToolkit.NugetApi.Context;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

//builder.Services.UseDomainEvents(
//    async (sp, domainEvent) =>
//        {
//            var mediator = sp.GetRequiredService<IMediator>();
//            await mediator.Publish(domainEvent);
//        }
//    );

//var connectionString = builder.Configuration.GetConnectionString("ExampleContext");
builder.Services.AddDbContext<ExampleContext>();

var app = builder.Build();

app.Services.CreateScope().ServiceProvider.GetRequiredService<ExampleContext>().Database.Migrate();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
