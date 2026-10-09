// This module's own namespaces, one per folder that holds types. Global so that moving a file between
// folders stays a change to that file, and so the using lists above the code name what comes from
// outside the module.
global using Examples.Webshop.Ordering.Domain.Orders;
global using Examples.Webshop.Ordering.Domain.ValueObjects;
global using Examples.Webshop.Ordering.Domain.Services;
global using Examples.Webshop.Ordering.Application.Orders;
global using Examples.Webshop.Ordering.Application.CatalogPrices;
global using Examples.Webshop.Ordering.Infrastructure.Persistence;
global using Examples.Webshop.Ordering.Api;
global using Examples.Webshop.Ordering.Api.GraphQL;
