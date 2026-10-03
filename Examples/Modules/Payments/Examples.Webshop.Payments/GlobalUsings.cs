// This module's own namespaces, one per folder that holds types. Global so that moving a file between
// folders stays a change to that file, and so the using lists above the code name what comes from
// outside the module.
global using Examples.Webshop.Payments.Domain.Payments;
global using Examples.Webshop.Payments.Domain.Services;
global using Examples.Webshop.Payments.Application.Payments;
global using Examples.Webshop.Payments.Infrastructure.PaymentProvider;
global using Examples.Webshop.Payments.Infrastructure.Persistence;
global using Examples.Webshop.Payments.Api;
global using Examples.Webshop.Payments.Api.GraphQL;
