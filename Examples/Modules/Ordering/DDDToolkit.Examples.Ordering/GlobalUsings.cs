// This module's own namespaces, one per folder that holds types. Global so that moving a file between
// folders stays a change to that file, and so the using lists above the code name what comes from
// outside the module.
global using DDDToolkit.Examples.Ordering.Domain.Orders;
global using DDDToolkit.Examples.Ordering.Domain.ValueObjects;
global using DDDToolkit.Examples.Ordering.Domain.Services;
global using DDDToolkit.Examples.Ordering.Application.IntegrationEvents;
global using DDDToolkit.Examples.Ordering.Application.DomainEvents;
global using DDDToolkit.Examples.Ordering.Application.ReadModels;
global using DDDToolkit.Examples.Ordering.Infrastructure.Persistence;
global using DDDToolkit.Examples.Ordering.Api;
global using DDDToolkit.Examples.Ordering.Api.GraphQL;
