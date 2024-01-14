using Microsoft.CodeAnalysis.Diagnostics;
using System;

namespace SourceGeneratorsToolkit;


public class OptionsProvider
{
    private readonly AnalyzerConfigOptionsProvider _provider;
    private readonly string? _prefix;

    internal OptionsProvider(AnalyzerConfigOptionsProvider provider, string prefix)
    {
        _provider = provider;
        _prefix = prefix;
    }

    public T GetOption<T>(string key, T defaultValue)
    {
        var option = _provider.GlobalOptions.TryGetValue(_prefix + key, out var value) ? value : null;
        return option is null ? defaultValue : (T)Convert.ChangeType(option, typeof(T));
    }
}


public static class OptionsProviderExtensions
{
    public static OptionsProvider ToProvider(this AnalyzerConfigOptionsProvider provider, string prefix = "")
    {
        return new OptionsProvider(provider, prefix);
    }
}
