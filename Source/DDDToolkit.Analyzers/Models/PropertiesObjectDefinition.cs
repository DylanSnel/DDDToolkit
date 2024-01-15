using System.Collections.Generic;

namespace DDDToolkit.Analyzers.Models;
public class PropertiesObjectDefinition : BaseObjectDefinition
{
    public List<string> Properties { get; set; } = new List<string>();
}
