using DDDToolkit.BaseTypes;

namespace DDDToolkit.ProtoTyping;

public record Class1 : ValueObject
{
    public int Thing { get; protected set; }

    public List<string> MyList { get; init; } = new();

    protected override bool Validate()
    {
        return Thing > 0;
    }



    public override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Thing;
    }

    public ValidClass1 ToValid() => new(this);
}
public record ValidClass1 : Class1
{
    internal ValidClass1(Class1 raw)
    {
        raw.EnsureValidated();
        _isValid = true;
        Thing = raw.Thing;
    }


}


public class MyEntity
{
    public Class1 MyProperty { get; set; }

    public MyEntity(Class1 myProperty)
    {
        if (myProperty.IsValid)
        {

        }
    }

}


