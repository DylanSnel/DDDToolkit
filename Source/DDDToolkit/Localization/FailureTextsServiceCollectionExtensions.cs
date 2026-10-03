using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Localization;

/// <summary>What a package's registration calls to offer the texts of its failures.</summary>
public static class FailureTextsServiceCollectionExtensions
{
    /// <summary>
    /// Offers the texts a package ships for its failures to the application's failure localizer, from the
    /// package's own registration, so the application adds no line for them:
    /// <code>
    /// services.AddFailureTexts&lt;SubscriptionFailures&gt;();
    /// </code>
    /// and, for a package whose failures carry codes the application chooses, which code reads which entry:
    /// <code>
    /// services.AddFailureTexts&lt;SubscriptionFailures&gt;(codes.TextKeys);
    /// </code>
    /// <para>
    /// Nothing is read unless the application localizes its failures, with
    /// <c>AddDDDToolkitLocalization</c> of <c>DDDToolkit.Localization</c>, before this call or after it. That
    /// localizer asks the application's own sources first, then every offer in the order it was made, then the
    /// toolkit's own messages (<see cref="FailureTexts"/>).
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="entries">
    /// For each code a failure carries, the name of the entry that holds its text; left out when the failures
    /// carry the entries' own names.
    /// </param>
    /// <typeparam name="TResource">The marker type the resx is named after, in the package's assembly.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">A code or an entry's name in <paramref name="entries"/> is empty.</exception>
    public static IServiceCollection AddFailureTexts<TResource>(this IServiceCollection services, IReadOnlyDictionary<string, string>? entries = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new FailureTexts(typeof(TResource), entries));
        return services;
    }
}
