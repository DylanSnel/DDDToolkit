using DDDToolkit.EntityFramework;
using DDDToolkit.ExampleApi;
using DDDToolkit.ExampleApi.Context;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// The toolkit's generator writes AddApiHandlers() into this project: every INotificationHandler in it,
// registered scoped, so a handler that injects ExampleContext gets the context of the scope that saves.
builder.Services.AddApiHandlers();

// --- Domain events, variant 1: in-process dispatch through the toolkit's mediator ---------------
// Handlers run inside SaveChanges, before the database write: whatever they change on the scoped
// ExampleContext is saved in the same transaction and a throwing handler aborts the save. Delivery is
// best-effort (nothing is persisted about the event itself). Handlers must not call SaveChanges.
//
// DispatchWithMediator() is the delegate below, written out:
//     options.DispatchInProcess(async (services, events, cancellationToken) =>
//     {
//         var publisher = services.GetRequiredService<IPublisher>();
//         foreach (var domainEvent in events)
//         {
//             await publisher.Publish(domainEvent, cancellationToken);
//         }
//     });
// Every domain event is a notification, so no event needs a marker. An event without a handler is
// published to nobody.
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

// --- Domain events, variant 2: transactional outbox ---------------------------------------------
// Replace the line above with this block for at-least-once delivery. SaveChanges then writes one
// OutboxMessages row per event in the aggregate's transaction (ExampleContext already maps the table)
// and the background service delivers the rows through the same dispatch. Handlers must be
// idempotent, keyed by IDomainEvent.EventId.
//
// builder.Services.AddDDDToolkitEntityFramework(options =>
// {
//     options.DispatchWithMediator();
//     options.UseOutbox(outbox => outbox.RegisterEventsFromAssemblyContaining<Program>());
// });
// builder.Services.AddOutboxBackgroundService<ExampleContext>(pollingInterval: TimeSpan.FromSeconds(2));

var connectionString = builder.Configuration.GetConnectionString("ExampleContext")
    ?? $"Data Source={Path.Combine(AppContext.BaseDirectory, "example.db")}";

// UseDDDToolkit adds the toolkit's interceptors, for domain events, invariants, optimistic concurrency and a
// save the database refused, and then what the packages the host registered bring: none in this one. Pass the
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
