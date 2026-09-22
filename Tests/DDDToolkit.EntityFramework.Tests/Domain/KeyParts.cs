using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.EntityFramework.Tests.Domain;

// A small domain keyed on more than its identifier. The key part is a region here; the toolkit does
// not know or care what it means, and a tenant, a ledger or a period would be mapped the same way.

/// <summary>The key part of <see cref="Survey"/> and everything it owns.</summary>
[EntityId<Guid>("REG")]
public readonly partial record struct RegionId;

[EntityId<Guid>("SUR")]
public readonly partial record struct SurveyId;

[EntityId<Guid>]
public readonly partial record struct QuestionId;

[EntityId<Guid>]
public readonly partial record struct CoverId;

/// <summary>
/// Keyed (RegionId, Id). Owns a collection of <see cref="Question"/> and a single <see cref="Cover"/>,
/// both of which carry the region in their foreign key.
/// </summary>
[AggregateRoot<SurveyId>]
public partial class Survey
{
    public Survey(RegionId regionId, SurveyId id, string title) : base(id)
    {
        RegionId = regionId;
        Title = title;
        Cover = new Cover(regionId, CoverId.CreateUnique(), "Cover of " + title);
    }

    [KeyPart]
    public RegionId RegionId { get; }

    public string Title { get; private set; }

    public Cover Cover { get; private set; }

    public partial IReadOnlyList<Question> Questions { get; }

    public Question Ask(string text)
    {
        var question = new Question(RegionId, QuestionId.CreateUnique(), text);
        _questions.Add(question);
        return question;
    }

    public void Withdraw(QuestionId questionId) => _questions.RemoveAll(question => question.Id == questionId);

    public void Retitle(string title) => Title = title;
}

/// <summary>An owned collection element. Carries its owner's key part, set by the owner.</summary>
[Entity<QuestionId>]
public partial class Question
{
    public Question(RegionId regionId, QuestionId id, string text) : base(id)
    {
        RegionId = regionId;
        Text = text;
    }

    [KeyPart]
    public RegionId RegionId { get; }

    public string Text { get; private set; }
}

/// <summary>An owned reference, stored in its owner's table. Carries the key part too.</summary>
[Entity<CoverId>]
public partial class Cover
{
    public Cover(RegionId regionId, CoverId id, string caption) : base(id)
    {
        RegionId = regionId;
        Caption = caption;
    }

    [KeyPart]
    public RegionId RegionId { get; }

    public string Caption { get; private set; }
}

[EntityId<Guid>("CEN")]
public readonly partial record struct CensusId;

[EntityId<Guid>]
public readonly partial record struct TallyId;

/// <summary>
/// Two key parts, declared period first and region second, which is neither alphabetical nor the
/// order of the constructor's arguments: the key must follow the declaration.
/// </summary>
[AggregateRoot<CensusId>]
public partial class Census
{
    public Census(RegionId regionId, int period, CensusId id) : base(id)
    {
        RegionId = regionId;
        Period = period;
    }

    [KeyPart]
    public int Period { get; }

    [KeyPart]
    public RegionId RegionId { get; }

    public partial IReadOnlyList<Tally> Tallies { get; }

    public void Count(int value) => _tallies.Add(new Tally(Period, RegionId, TallyId.CreateUnique(), value));
}

[Entity<TallyId>]
public partial class Tally
{
    public Tally(int period, RegionId regionId, TallyId id, int value) : base(id)
    {
        Period = period;
        RegionId = regionId;
        Value = value;
    }

    [KeyPart]
    public int Period { get; }

    [KeyPart]
    public RegionId RegionId { get; }

    public int Value { get; private set; }
}

[EntityId<Guid>("ARC")]
public readonly partial record struct ArchiveId;

[EntityId<Guid>]
public readonly partial record struct FolioId;

/// <summary>Keyed on its region, but owns a <see cref="Folio"/> that has no region to carry. Its model must not build.</summary>
[AggregateRoot<ArchiveId>]
public partial class Archive
{
    public Archive(RegionId regionId, ArchiveId id) : base(id) => RegionId = regionId;

    [KeyPart]
    public RegionId RegionId { get; }

    public partial IReadOnlyList<Folio> Folios { get; }
}

[Entity<FolioId>]
public partial class Folio
{
    public Folio(FolioId id, string text) : base(id) => Text = text;

    public string Text { get; private set; }
}
