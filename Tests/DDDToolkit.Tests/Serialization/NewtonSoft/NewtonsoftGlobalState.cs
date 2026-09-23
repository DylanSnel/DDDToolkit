namespace DDDToolkit.Tests.Serialization.NewtonSoft;

/// <summary>
/// Every test class that serializes through <c>JsonConvert</c>.
/// <para>
/// <c>JsonConvert.SerializeObject(value, settings)</c> applies <c>JsonConvert.DefaultSettings</c> first and
/// the settings it is handed on top, so a test that registers the toolkit as the global default changes
/// what every other <c>JsonConvert</c> call in the process writes while it runs. xUnit runs test classes
/// in parallel; putting these in one collection runs them one after another instead.
/// </para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class NewtonsoftGlobalState
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Newtonsoft global state";
}
