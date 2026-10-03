global using Campus;
global using Campus.Access;
global using Campus.Courses;
global using Campus.Labs;
global using Campus.Persistence;
global using Campus.Tenants;
global using DDDToolkit.Abstractions.Access;
global using DDDToolkit.Access;
global using DDDToolkit.EntityFramework.Tests.Infrastructure;
global using DDDToolkit.Supporting.Membership;
global using DDDToolkit.Supporting.Membership.Access;
global using DDDToolkit.Supporting.Membership.EntityFramework;
global using DDDToolkit.Supporting.Membership.EntityFramework.Tests.Infrastructure;
global using DDDToolkit.Supporting.Membership.UseCases;
global using DDDToolkit.Supporting.Tenancy;
global using DDDToolkit.Supporting.Tenancy.Access;
global using DDDToolkit.Supporting.Tenancy.Catalogue;
global using DDDToolkit.Supporting.Tenancy.Tests.Support;
global using DDDToolkit.Supporting.Together.Tests.Infrastructure;
global using FluentAssertions;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using Xunit;

// The caller and the use cases closed over the application's ids and classes, once, as an application does.
global using CampusCaller = DDDToolkit.Supporting.Tenancy.Access.TenancyCaller<Campus.Tenants.TenantId, Campus.Tenants.SeatId>;
global using CampusTenancy = DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<
    Campus.Tenants.Tenant,
    Campus.Tenants.TenantId,
    Campus.Tenants.Organization,
    Campus.Tenants.OrganizationUnit,
    Campus.Tenants.OrganizationUnitId,
    Campus.Tenants.Seat,
    Campus.Tenants.SeatId,
    Campus.Tenants.Role,
    Campus.Tenants.RoleId>;
