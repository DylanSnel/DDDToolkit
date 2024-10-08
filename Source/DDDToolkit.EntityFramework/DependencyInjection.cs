﻿using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework;
public static class DependencyInjection
{
    public static IServiceCollection UseDomainEvents(this IServiceCollection services, Func<IServiceProvider, List<IDomainEvent>, Task> interceptorAction)
    {
        services.AddSingleton<PublishDomainEventsInterceptor>();
        services.AddSingleton(serviceProvider =>
        {
            return new DDDEntityFrameworkOptions
            {
                InterceptorAction = interceptorAction
            };
        });

        return services;
    }

    public static void AddDomainEventInterceptor(this DbContextOptionsBuilder ctx, IServiceProvider scvc)
    {
        ctx.AddInterceptors(scvc.GetRequiredService<PublishDomainEventsInterceptor>());
    }
}
