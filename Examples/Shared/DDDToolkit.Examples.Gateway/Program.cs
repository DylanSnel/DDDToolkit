// The shop's front door when it runs as services. A client, the .http file or the end-to-end tests, talks to
// one address and never learns that three processes answer: /orders goes to Storefront, /payments to
// Payments, /shipments to Fulfilment. Which path goes where is configuration, in appsettings.json; the
// gateway references no module and no service. "http://storefront" is an Aspire resource name, resolved by
// service discovery.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.MapReverseProxy();
app.MapDefaultEndpoints();

await app.RunAsync();
