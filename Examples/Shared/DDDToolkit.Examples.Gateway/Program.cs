using DDDToolkit.Examples.Microservices;
using Yarp.ReverseProxy.Configuration;

// The shop's front door when it runs as services. A client, the .http file or the end-to-end tests, talks
// to one address and never learns that three processes answer: /orders goes to Storefront, /payments to
// Payments, /shipments to Fulfilment. The routes come from ShopServices, the same place that decides which
// modules each service runs, so the two cannot drift apart.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var routes = ShopServices.Routes.Select(route => new RouteConfig
{
    RouteId = route.Key.Trim('/'),
    ClusterId = ShopServices.NameOf(route.Value),
    Match = new RouteMatch { Path = route.Key + "/{**rest}" },
}).Concat(ShopServices.Routes.Select(route => new RouteConfig
{
    // The collection itself, /orders as well as /orders/{id}.
    RouteId = route.Key.Trim('/') + "-root",
    ClusterId = ShopServices.NameOf(route.Value),
    Match = new RouteMatch { Path = route.Key },
})).ToList();

// "http://storefront" is an Aspire resource name, resolved by service discovery.
var clusters = Enum.GetValues<ShopService>().Select(service => new ClusterConfig
{
    ClusterId = ShopServices.NameOf(service),
    Destinations = new Dictionary<string, DestinationConfig>
    {
        [ShopServices.NameOf(service)] = new() { Address = "http://" + ShopServices.NameOf(service) },
    },
}).ToList();

builder.Services.AddReverseProxy()
    .LoadFromMemory(routes, clusters)
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.MapReverseProxy();
app.MapDefaultEndpoints();

await app.RunAsync();
