using System.Globalization;
using DDDToolkit.Localization;
using Microsoft.AspNetCore.Localization;

namespace Examples.Tenancy.Host.Languages;

/// <summary>
/// The languages the host answers in, English and Dutch, and how a request chooses one: by its
/// <c>Accept-Language</c> header, and English when it asks for neither.
/// </summary>
/// <remarks>
/// Only the header chooses. The framework would also read a query string and a cookie; both are taken off, so a
/// link cannot carry a language to somebody else's browser and nothing about a caller is kept between requests.
/// <para>
/// What follows the language is the text a person reads: a refusal's title, and the message of each broken rule.
/// Codes, arguments, keys and the names a tenant gave its own roles and units are the same in every language.
/// </para>
/// </remarks>
public static class RequestLanguages
{
    /// <summary>The languages a request can be answered in. The first is the one the neutral resource files are written in.</summary>
    public static IReadOnlyList<string> Supported { get; } = ["en", "nl"];

    /// <summary>The language of a request that asks for none of <see cref="Supported"/>.</summary>
    public static CultureInfo Default { get; } = CultureInfo.GetCultureInfo(Supported[0]);

    /// <summary>
    /// Registers the toolkit's localizer with the texts of the host's own codes, and the choice of a request's
    /// language. Call it before the modules: each module adds the texts of its own codes after these, and the
    /// localizer asks its sources in the order they were added.
    /// </summary>
    /// <param name="services">The host's services.</param>
    public static IServiceCollection AddRequestLanguages(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLocalization();
        services.AddDDDToolkitLocalization(texts => texts.AddResource<HostFailures>());

        return services.Configure<RequestLocalizationOptions>(options =>
        {
            var supported = Supported.ToArray();
            options.SetDefaultCulture(supported[0]).AddSupportedCultures(supported).AddSupportedUICultures(supported);
            options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider { Options = options }];
        });
    }

    /// <summary>
    /// The language the request localization middleware chose for <paramref name="context"/>, or
    /// <see cref="Default"/> where it has not run. For code that answers outside the middleware's own flow of
    /// work, where the current culture is no longer the request's: the exception handler.
    /// </summary>
    /// <param name="context">The request.</param>
    public static CultureInfo LanguageOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture ?? Default;
    }
}
