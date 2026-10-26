using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Examples.Tenancy.Host.Languages;
using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations;
using Examples.Tenancy.Ui.Api.TryIt;
using Examples.Tenancy.Ui.Languages;
using DDDToolkit.Localization;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Host.Languages;

/// <summary>
/// The sample's texts in English and Dutch: every code the host can answer has both, through the localizer the
/// host registers; each resource file holds what its table declares, in the table's own English; the two
/// languages of a file have the same entries with the same placeholders; and no Dutch text has a word for the
/// reader.
/// </summary>
public sealed partial class TranslationTests(SampleWithoutDatabase sample) : IClassFixture<SampleWithoutDatabase>
{
    /// <summary>The marker of every pair of resource files of the sample: the modules', the host's and the UI's.</summary>
    private static readonly Type[] Markers =
    [
        typeof(ProjectFailures),
        typeof(InspectionFailures),
        typeof(SeatFailures),
        typeof(OrganizationFailures),
        typeof(HistoryFailures),
        typeof(HostFailures),
        typeof(UiTexts),
    ];

    /// <summary>The codes Inspections passes on from Projects, under Projects' own codes.</summary>
    private static readonly string[] PassedOn = [InspectionRefusals.ProjectNotFound, InspectionRefusals.ProjectNotPermitted, InspectionRefusals.ProjectClosed];

    /// <summary>The Dutch words for the reader, familiar and formal: the list the Tenancy package holds its own texts to.</summary>
    private static readonly string[] WordsForTheReader =
        ["je", "jij", "jou", "jouw", "jezelf", "jouzelf", "jullie", "u", "uw", "uwe", "uzelf", "gij", "ge"];

    [Fact]
    public void Every_code_the_host_can_answer_has_a_text_in_english_and_dutch()
    {
        // The localizer the host registered, so a module that forgot to add its texts fails here.
        var localizer = sample.Services.GetRequiredService<IFailureLocalizer>();

        FailureTranslations.Check(localizer, RequestLanguages.Supported)
            .Codes(TenancyRefusals.Codes)
            .Codes(ProjectRefusals.Codes)
            .Codes(InspectionRefusals.Codes)
            .Codes(RefusalProblems.Codes)
            .Codes(HistoryRefusals.Codes)
            .Codes(DevLoginEndpoints.UnknownPerson)
            .Invariants(typeof(Project).Assembly, typeof(Inspection).Assembly, typeof(Seat).Assembly)
            .ToolkitCodes()
            .Verify();
    }

    [Fact]
    public void A_resource_file_holds_the_codes_of_its_table_in_the_tables_own_english()
    {
        var projects = Texts(typeof(ProjectFailures), CultureInfo.InvariantCulture);
        projects.Keys.Should().BeEquivalentTo(ProjectRefusals.Codes, "ProjectFailures.resx holds exactly the codes of ProjectRefusals");
        projects.Should().OnlyContain(text => text.Value == ProjectRefusals.TemplateOf(text.Key), "the English of a code is what its table says");

        var inspections = Texts(typeof(InspectionFailures), CultureInfo.InvariantCulture);
        inspections.Keys.Should().BeEquivalentTo(InspectionRefusals.Codes.Except(PassedOn), "InspectionFailures.resx holds exactly Inspections' own codes");
        inspections.Should().OnlyContain(text => text.Value == InspectionRefusals.TemplateOf(text.Key));

        // A code has one text: what Inspections passes on from Projects reads as Projects says it.
        PassedOn.Should().OnlyContain(code => InspectionRefusals.TemplateOf(code) == ProjectRefusals.TemplateOf(code));

        Texts(typeof(HostFailures), CultureInfo.InvariantCulture).Keys
            .Should().BeEquivalentTo([.. RefusalProblems.Codes, DevLoginEndpoints.UnknownPerson], "HostFailures.resx holds exactly the host's own codes");
        Texts(typeof(SeatFailures), CultureInfo.InvariantCulture).Keys.Should().BeEquivalentTo([Seat.DisplayNameIsValid.ViolationCode, Seat.JobTitleLength.ViolationCode]);
        Texts(typeof(SeatFailures), CultureInfo.InvariantCulture)[Seat.DisplayNameIsValid.ViolationCode].Should().Be(Seat.DisplayNameIsValid.Text, "the seat's rule and its refusal read as the resource file says");
        Texts(typeof(OrganizationFailures), CultureInfo.InvariantCulture).Keys.Should().Equal(OrganizationUnit.CostCentreFormat.ViolationCode);
        Texts(typeof(HistoryFailures), CultureInfo.InvariantCulture)
            .Should().Equal(new Dictionary<string, string>
            {
                [HistoryRefusals.PageSizeInvalid] = HistoryRefusals.PageSizeInvalidText,
                [HistoryRefusals.PageFromBothEnds] = HistoryRefusals.PageFromBothEndsText,
            });
    }

    [Fact]
    public void An_entry_of_one_language_is_an_entry_of_the_other_with_the_same_placeholders()
    {
        foreach (var marker in Markers)
        {
            var english = Texts(marker, CultureInfo.InvariantCulture);
            var dutch = Texts(marker, CultureInfo.GetCultureInfo("nl"));

            english.Should().NotBeEmpty("{0}.resx holds texts", marker.Name);
            dutch.Keys.Should().BeEquivalentTo(english.Keys, "{0}.nl.resx has every entry of {0}.resx and no other", marker.Name);

            foreach (var (name, text) in dutch)
            {
                text.Should().NotBeNullOrWhiteSpace("the Dutch text of {0} says something", name);
                english[name].Should().NotBeNullOrWhiteSpace("the English text of {0} says something", name);
                Placeholders(text).Should().BeEquivalentTo(
                    Placeholders(english[name]),
                    "the Dutch text of {0} is filled from the same values as the English one: a placeholder nobody fills is shown as written", name);
            }
        }
    }

    [Fact]
    public void No_dutch_text_has_a_word_for_the_reader()
    {
        var addressing = Markers
            .SelectMany(marker => Texts(marker, CultureInfo.GetCultureInfo("nl")).Select(text => (Name: marker.Name + ": " + text.Key, Text: text.Value)))
            .Select(entry => (entry.Name, entry.Text, Words: WordsOf(entry.Text).Intersect(WordsForTheReader).ToArray()))
            .Where(entry => entry.Words.Length > 0)
            .Select(entry => entry.Name + " says \"" + string.Join("\", \"", entry.Words) + "\": " + entry.Text)
            .ToArray();

        addressing.Should().BeEmpty(
            "a Dutch text of the sample has no word for the reader, neither the familiar nor the formal one: it says what "
            + "is the case or what is needed, as the Tenancy package's texts do");

        // The check itself: it finds the words as words, in any case, and not inside other words, markup or placeholders.
        WordsOf("Je mist het recht {U}; vraag <u>uw</u> beheerder, of U zelf.").Intersect(WordsForTheReader).Should().Equal("je", "uw", "u");
        WordsOf("Deze uitleg juicht, en een menu is geen jeu; id's ook niet.").Intersect(WordsForTheReader).Should().BeEmpty();
    }

    [Fact]
    public void Every_text_a_page_of_the_ui_names_is_in_its_resource_files()
    {
        // A name the files do not know is shown as it is written, so nothing but this test finds a typing mistake.
        var directory = Path.Combine(SampleLayout.RepositoryRoot(), "Examples", "Tenancy", "Examples.Tenancy.Ui");
        var named = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file) is ".razor" or ".cs")
            .Where(file => Path.GetRelativePath(directory, file).Replace('\\', '/').Split('/')[0] is not ("bin" or "obj"))
            .SelectMany(file => NamedText().Matches(File.ReadAllText(file)).Select(match => match.Groups["name"].Value))
            .ToHashSet(StringComparer.Ordinal);

        named.Should().Contain(["login.title", "api.no-answer", "whoami.intro"], "the pages, the client and a text with markup all name their texts, or this proves nothing");

        // The actions of the try-it form name their texts in a list of their own, which the form reads them
        // from: each is one more name the files hold, and no two actions share one.
        var actions = TryItActions.All.Select(action => action.Text).ToList();
        actions.Should().OnlyHaveUniqueItems("an action is told apart by the name of its text")
            .And.OnlyContain(name => name.StartsWith("try.action.", StringComparison.Ordinal));
        named.UnionWith(actions);

        Texts(typeof(UiTexts), CultureInfo.InvariantCulture).Keys.Should().BeEquivalentTo(named, "UiTexts.resx holds exactly the texts the UI names");
    }

    /// <summary>What the resource file of <paramref name="marker"/> for exactly <paramref name="culture"/> holds, by name.</summary>
    private static Dictionary<string, string> Texts(Type marker, CultureInfo culture)
    {
        var set = new ResourceManager(marker).GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        set.Should().NotBeNull("{0} has a resource file for '{1}'", marker.Name, culture.Name);
        return set!.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    private static string[] Placeholders(string text) => [.. Placeholder().Matches(text).Select(match => match.Value).Distinct().Order(StringComparer.Ordinal)];

    /// <summary>The words of <paramref name="text"/>, in lower case, with its placeholders and its markup left out.</summary>
    private static IEnumerable<string> WordsOf(string text)
        => Word().Matches(NotText().Replace(text, " ")).Select(match => match.Value.ToLowerInvariant());

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Placeholder();

    /// <summary>A placeholder, a tag or a character reference: none of them is a word a reader reads.</summary>
    [GeneratedRegex(@"\{[^{}]*\}|</?\w+[^>]*>|&\w+;")]
    private static partial Regex NotText();

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();

    /// <summary>A text named in the UI: <c>T["name"</c>, <c>T.Html("name"</c> or <c>UiTexts.For("name"</c>.</summary>
    [GeneratedRegex(@"\bT(?:\[|\.Html\()""(?<name>[a-z0-9.-]+)""|UiTexts\.For\(""(?<name>[a-z0-9.-]+)""")]
    private static partial Regex NamedText();
}
