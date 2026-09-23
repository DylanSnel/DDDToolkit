// This module's own namespaces, one per folder that holds types. Global so that moving a file between
// folders stays a change to that file, and so the using lists above the code name what comes from
// outside the module.
global using DDDToolkit.Examples.Payments.Domain.Payments;
global using DDDToolkit.Examples.Payments.Domain.Services;
global using DDDToolkit.Examples.Payments.Application.IntegrationEvents;
global using DDDToolkit.Examples.Payments.Infrastructure.PaymentProvider;
global using DDDToolkit.Examples.Payments.Infrastructure.Persistence;
global using DDDToolkit.Examples.Payments.Api;
global using DDDToolkit.Examples.Payments.Api.GraphQL;
