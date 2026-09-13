using DDDToolkit.EntityFramework;
using DDDToolkit.ExampleApi.Context;
using DDDToolkit.Mediator;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Mediator (github.com/martinothamar/Mediator) is source generated, so AddMediator and the handler
// registrations are written into THIS assembly at compile time. That is why the generator package
// belongs here and not in DDDToolkit.ExampleLibrary: handlers anywhere in the reference graph are
// found, but the registration code is emitted only where the generator runs.
//
// Scoped, not the Singleton default. A handler that injects ExampleContext needs the scope's context,
// and a singleton cannot depend on a scoped service. The generator reads this very call, so the
// lifetime has to be written here; setting it anywhere else throws at start-up.
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

// --- Domain events, variant 1: in-process dispatch through Mediator ------------------------------
// Handlers run inside SaveChanges, before the database write: whatever they change on the scoped
// ExampleContext is saved in the same transaction and a throwing handler aborts the save. Delivery is
// best-effort (nothing is persisted about the event itself). Handlers must not call SaveChanges.
//
// DispatchWithMediator() comes from DDDToolkit.Mediator and is the delegate below, written out:
//     options.DispatchInProcess(async (services, events, cancellationToken) =>
//     {
//         var publisher = services.GetRequiredService<IPublisher>();
//         foreach (var domainEvent in events)
//         {
//             await publisher.Publish(domainEvent, cancellationToken);
//         }
//     });
// The example events implement Mediator's INotification through IBaseDomainEvent, which is what makes
// them publishable; an event that does not throws with its type name instead of being dropped.
builder.Services.AddDDDToolkitEntityFramework(options => options.DispatchWithMediator());

// --- Domain events, variant 2: transactional outbox ---------------------------------------------
// Replace the line above with this block for at-least-once delivery. SaveChanges then writes one
// OutboxMessages row per event in the aggregate's transaction (ExampleContext already maps the table)
// and the background service delivers the rows through the same Mediator dispatch. Handlers must be
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
