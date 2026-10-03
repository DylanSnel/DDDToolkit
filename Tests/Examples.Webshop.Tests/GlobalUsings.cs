global using Xunit;

// The modules' namespaces follow their folders; the tests read across all of them.
global using Examples.Webshop.Catalog.Domain.Products;
global using Examples.Webshop.Inventory.Domain.Services;
global using Examples.Webshop.Inventory.Domain.StockItems;
global using Examples.Webshop.Inventory.Domain.StockReservations;
global using Examples.Webshop.Ordering.Application.CatalogPrices;
global using Examples.Webshop.Ordering.Domain.Orders;
global using Examples.Webshop.Ordering.Domain.Services;
global using Examples.Webshop.Ordering.Domain.ValueObjects;
global using Examples.Webshop.Ordering.Infrastructure.Persistence;
global using Examples.Webshop.Payments.Domain.Payments;
global using Examples.Webshop.Payments.Infrastructure.PaymentProvider;
global using Examples.Webshop.Payments.Infrastructure.Persistence;
global using Examples.Webshop.Catalog.Infrastructure.Persistence;
global using Examples.Webshop.Inventory.Infrastructure.Persistence;
global using Examples.Webshop.Shipping.Domain.Shipments;
global using Examples.Webshop.Shipping.Infrastructure.Persistence;
