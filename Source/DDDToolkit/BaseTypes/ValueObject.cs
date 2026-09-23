using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Validation;

namespace DDDToolkit.BaseTypes;
public abstract record ValueObject : IValueObject
{
    /// <summary>True once <see cref="IsValid"/> has been read and the verdict cached.</summary>
    [Internal]
    public bool IsValidated => _isValid != null;

    /// <summary>
    /// Whether the value satisfies its own rules. Computed on first read and cached for the lifetime of
    /// the instance, which is safe because a value object never changes after construction.
    /// </summary>
    [Internal]
    public bool IsValid => _isValid ??= RunValidation();

    /// <summary>The cached verdict: null until something asks for it.</summary>
    protected bool? _isValid = null;

    /// <summary>The failures behind the cached verdict: null until validation has run.</summary>
    private IReadOnlyList<ValidationError>? _validationErrors;

    /// <summary>
    /// Why the value is invalid, in full. Empty when the value is valid. Reading this runs the rules if
    /// nothing has run them yet, exactly as reading <see cref="IsValid"/> does.
    /// <para>
    /// This is the failure model that does not throw. <c>DDDToolkit.FluentValidation</c> fills it from the
    /// generated validator's failures; without that package it is filled by
    /// <see cref="Validate(ValidationErrorBuilder)"/>, and a <see cref="Validate()"/> that returns
    /// <see langword="false"/> without describing itself produces one failure carrying
    /// <see cref="ValidationError.UnspecifiedCode"/>.
    /// </para>
    /// </summary>
    [Internal]
    [NotMapped]
    [JsonIgnore]
    public IReadOnlyList<ValidationError> ValidationErrors
    {
        get
        {
            _ = IsValid;
            return _validationErrors ?? [];
        }
    }

    /// <summary>
    /// The rules, as a yes or no. The default accepts everything; DDDToolkit.FluentValidation overrides
    /// this with a generated body that runs the nested <c>Validator</c>.
    /// </summary>
    [Internal]
    protected virtual bool Validate() => true;

    /// <summary>
    /// The rules, as a list of reasons. Override this instead of <see cref="Validate()"/> when a caller
    /// needs to know <i>why</i> a value was refused. A run that adds nothing to
    /// <paramref name="errors"/> is a pass.
    /// <para>
    /// Both overloads run, and both must be happy: the value is valid when <see cref="Validate()"/>
    /// returned <see langword="true"/> and no failure was added here. Overriding only one is the normal
    /// case. With DDDToolkit.FluentValidation referenced this overload is generated for you, so override
    /// neither and write rules instead.
    /// </para>
    /// </summary>
    /// <param name="errors">Collects this run's failures.</param>
    [Internal]
    protected virtual void Validate(ValidationErrorBuilder errors)
    {
    }

    /// <summary>
    /// Throws <see cref="InvalidValueObjectException"/> unless <see cref="IsValid"/>. The exception
    /// carries <see cref="ValidationErrors"/>, so even the throwing path says why.
    /// </summary>
    [Internal]
    public virtual void EnsureValidated()
    {
        if (!IsValid)
        {
            throw new InvalidValueObjectException(this.GetType(), ValidationErrors);
        }
    }

    /// <summary>
    /// Runs both halves of the rules once and records the failures. Called only from
    /// <see cref="IsValid"/>, which caches the verdict.
    /// </summary>
    private bool RunValidation()
    {
        var verdict = Validate();

        var errors = new ValidationErrorBuilder();
        Validate(errors);

        if (!verdict && !errors.HasErrors)
        {
            // Validate() refused without saying why. Say the little that is known rather than hand the
            // caller an empty list that reads like a pass.
            errors.Add(
                new ValidationError($"{GetType().Name} is not valid.", code: ValidationError.UnspecifiedCode)
                    .With(ValidationError.ValueObjectArgument, GetType().Name));
        }

        _validationErrors = errors.HasErrors ? errors.ToReadOnlyList() : [];
        return verdict && !errors.HasErrors;
    }

    [Internal]
    protected abstract IEnumerable<object?> GetEqualityComponents();


    public override int GetHashCode()
        => GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);

    protected ValueObject()
    {
    }

    /// <summary>
    /// Copy constructor used by <c>record with</c>. It deliberately does <b>not</b> copy the cached
    /// verdict: a copy differs from its source in at least one component, so the source's verdict says
    /// nothing about it. The copy revalidates the first time anything asks.
    /// </summary>
    protected ValueObject(ValueObject original)
    {
        ArgumentNullException.ThrowIfNull(original);
        _isValid = null;
        _validationErrors = null;
    }
}

//public abstract record ValueObject<TInterface> : IValueObject where TInterface : class, IValueObject<TInterface>
//{
//    [Internal]
//    public bool IsValidated => _isValid != null;
//    [Internal]
//    public bool IsValid => _isValid ??= Validate((this as TInterface)!);


//    protected bool? _isValid = null;

//    [Internal]
//    public virtual bool Validate(TInterface valueObject) => true;

//    [EditorBrowsable(EditorBrowsableState.Never)]
//    protected static bool ValidateRaw(TInterface valueObject)
//    {
//        return valueObject.Validate(valueObject);
//    }

//    [Internal]
//    public virtual void EnsureValidated()
//    {
//        if (!IsValid)
//        {
//            throw new InvalidValueObjectException(this.GetType());
//        }
//    }

//    [Internal]
//    public abstract IEnumerable<object?> GetEqualityComponents();


//    public override int GetHashCode()
//        => GetEqualityComponents()
//            .Select(x => x?.GetHashCode() ?? 0)
//            .Aggregate((x, y) => x ^ y);

//    protected ValueObject()
//    {
//    }
//}

