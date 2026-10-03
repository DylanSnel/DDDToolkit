global using DDDToolkit.Supporting.Tenancy.Access;
global using DDDToolkit.Supporting.Tenancy.EntityFramework.Tests.Infrastructure;
global using DDDToolkit.Supporting.Tenancy.TestHost;
global using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;
global using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
global using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
global using DDDToolkit.Supporting.Tenancy.Tests.Support;
global using FluentAssertions;
global using Microsoft.EntityFrameworkCore;
global using Xunit;

// The use cases and the caller closed over the host's classes and ids, once, as an application does.
global using HostCaller = DDDToolkit.Supporting.Tenancy.Access.TenancyCaller<
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.TenantId,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.SeatId>;
global using HostTenancy = DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<
    DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostTenant,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.TenantId,
    DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostOrganization,
    DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostUnit,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.OrganizationUnitId,
    DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostSeat,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.SeatId,
    DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostRole,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.RoleId>;
