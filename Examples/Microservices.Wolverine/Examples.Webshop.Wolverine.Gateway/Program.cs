// The shop's front door when it runs as services. A client, the .http file or the end-to-end tests, talks to
// one address and never learns that three processes answer.
//
// REST goes through YARP: /orders to Storefront, /payments to Payments, /shipments to Fulfilment, as
// appsettings.json says. "http://storefront" is an Aspire resource name, resolved by service discovery.
//
// GraphQL goes through a Fusion gateway: one schema, composed from the three services' source schemas by the
// AppHost before this process starts, and handed over as gateway.far next to this project. A query for an
// order with its lines' products, its payment and its shipment is planned here and answered by all three.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

// The client the gateway calls the source schemas with; their addresses are in the archive.
builder.Services.AddHttpClient("fusion");

builder.AddGraphQLGateway()
    .AddFileSystemConfiguration(Path.Combine(builder.Environment.ContentRootPath, "gateway.far"));

var app = builder.Build();

app.MapReverseProxy();
app.MapGraphQL();
app.MapDefaultEndpoints();

await app.RunAsync();
