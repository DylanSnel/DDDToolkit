using DDDToolkit.EntityFramework;
using DDDToolkit.ExampleApi.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

// --- Domain events, variant 1: in-process dispatch through MediatR -------------------------------
// Handlers run inside SaveChanges, before the database write: whatever they change on the scoped
// ExampleContext is saved in the same transaction and a throwing handler aborts the save. Delivery is
// best-effort (nothing is persisted about the event itself). Handlers must not call SaveChanges.
builder.Services.AddDDDToolkitEntityFramework(options =>
{
    options.DispatchInProcess(async (services, events, cancellationToken) =>
    {
        // The example events implement INotification through IBaseDomainEvent, so MediatR can publish them.
        var publisher = services.GetRequiredService<IPublisher>();
        foreach (var domainEvent in events)
        {
            await publisher.Publish(domainEvent, cancellationToken);
        }
    });
});

// --- Domain events, variant 2: transactional outbox ---------------------------------------------
// Replace the block above with this one for at-least-once delivery. SaveChanges then writes one
// OutboxMessages row per event in the aggregate's transaction (ExampleContext already maps the table)
// and the background service delivers the rows through the same MediatR delegate. Handlers must be
// idempotent, keyed by IDomainEvent.EventId.
//
// builder.Services.AddDDDToolkitEntityFramework(options =>
// {
//     options.DispatchInProcess(async (services, events, cancellationToken) =>
//     {
//         var publisher = services.GetRequiredService<IPublisher>();
//         foreach (var domainEvent in events)
//         {
//             await publisher.Publish(domainEvent, cancellationToken);
//         }
//     });
//     options.UseOutbox(outbox => outbox.RegisterEventsFromAssemblyContaining<Program>());
// });
// builder.Services.AddOutboxBackgroundService<ExampleContext>(pollingInterval: TimeSpan.FromSeconds(2));

var connectionString = builder.Configuration.GetConnectionString("ExampleContext")
    ?? $"Data Source={Path.Combine(AppContext.BaseDirectory, "example.db")}";

// UseDDDToolkit adds the domain event interceptor and the optimistic concurrency interceptor. Pass the
// scoped provider so handlers resolve the same ExampleContext instance that is being saved.
builder.Services.AddDbContext<ExampleContext>((services, options) => options
    .UseSqlite(connectionString)
    .UseDDDToolkit(services));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ExampleContext>().Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
