global using Xunit;

// The modules' namespaces follow their folders; the tests read across all of them.
global using DDDToolkit.Examples.Catalog.Domain.Products;
global using DDDToolkit.Examples.Inventory.Domain.Services;
global using DDDToolkit.Examples.Inventory.Domain.StockItems;
global using DDDToolkit.Examples.Inventory.Domain.StockReservations;
global using DDDToolkit.Examples.Ordering.Application.CatalogPrices;
global using DDDToolkit.Examples.Ordering.Domain.Orders;
global using DDDToolkit.Examples.Ordering.Domain.Services;
global using DDDToolkit.Examples.Ordering.Domain.ValueObjects;
global using DDDToolkit.Examples.Ordering.Infrastructure.Persistence;
global using DDDToolkit.Examples.Payments.Domain.Payments;
global using DDDToolkit.Examples.Payments.Infrastructure.PaymentProvider;
global using DDDToolkit.Examples.Payments.Infrastructure.Persistence;
global using DDDToolkit.Examples.Catalog.Infrastructure.Persistence;
global using DDDToolkit.Examples.Inventory.Infrastructure.Persistence;
global using DDDToolkit.Examples.Shipping.Domain.Shipments;
global using DDDToolkit.Examples.Shipping.Infrastructure.Persistence;
