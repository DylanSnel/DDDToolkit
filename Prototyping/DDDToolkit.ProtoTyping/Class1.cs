namespace DDDToolkit.ProtoTyping;

public record Class1
{
    public int Id { get; init; }

    public Class2 ToClass2() => new(this);
}
public record Class2 : Class1
{
    public Class2(Class1 raw) => Id = raw.Id;


}


