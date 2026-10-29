global using DDDToolkit.Supporting.Tenancy.Access;
global using DDDToolkit.Supporting.Tenancy.Managers;
global using DDDToolkit.Supporting.Tenancy.Catalogue;
global using DDDToolkit.Supporting.Tenancy.TestHost;
global using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;
global using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
global using DDDToolkit.Supporting.Tenancy.Tests.Support;
global using FluentAssertions;
global using Xunit;

// The caller closed over the host's ids, once, as an application does. The managers are the classes the toolkit's
// generator writes into the test host that declares the classes: SeatManager and the others, and HostRoleManager, under
// the name the host gives it.
global using HostUser = DDDToolkit.Supporting.Tenancy.Access.TenancyUser<
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.TenantId,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.SeatId>;

// The package's managers closed over the host's classes, as AddTenancyCore registers them: the classes the generator
// writes derive from these, and AddTenancy of the storage package registers those.
global using CoreTenantManager = DDDToolkit.Supporting.Tenancy.Managers.TenantManager<
    DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostTenant, DDDToolkit.Supporting.Tenancy.TestHost.Contracts.TenantId,
    DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostOrganization, DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostUnit,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.OrganizationUnitId, DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostSeat,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.SeatId, DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostRole,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.RoleId>;
global using CoreOrganizationManager = DDDToolkit.Supporting.Tenancy.Managers.OrganizationManager<
    DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostTenant, DDDToolkit.Supporting.Tenancy.TestHost.Contracts.TenantId,
    DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostOrganization, DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostUnit,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.OrganizationUnitId, DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostSeat,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.SeatId, DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostRole,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.RoleId>;
global using CoreSeatManager = DDDToolkit.Supporting.Tenancy.Managers.SeatManager<
    DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostTenant, DDDToolkit.Supporting.Tenancy.TestHost.Contracts.TenantId,
    DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostOrganization, DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostUnit,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.OrganizationUnitId, DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostSeat,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.SeatId, DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostRole,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.RoleId>;
global using CoreRoleManager = DDDToolkit.Supporting.Tenancy.Managers.RoleManager<
    DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostTenant, DDDToolkit.Supporting.Tenancy.TestHost.Contracts.TenantId,
    DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostOrganization, DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostUnit,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.OrganizationUnitId, DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostSeat,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.SeatId, DDDToolkit.Supporting.Tenancy.TestHost.Domain.HostRole,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.RoleId>;
