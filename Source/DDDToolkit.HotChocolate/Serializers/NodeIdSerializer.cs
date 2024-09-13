namespace DDDToolkit.HotChocolate.Serializers;
//public class NodeIdSerializer : INodeIdSerializer
//{
//    public string Format(string typeName, object internalId)
//    {
//        if (internalId is IEntityId entityId)
//        {
//            var formattedId = typeIdDecoded.);
//            if (String.IsNullOrEmpty(formattedId))
//                throw new ArgumentException("Formatted ID is null or empty");
//            return formattedId;
//        }

//        throw new ArgumentException($"Ids should always be of type TypeIdDecoded, but was {internalId.GetType()}");
//    }

//    public NodeId Parse(string formattedId, INodeIdRuntimeTypeLookup runtimeTypeLookup)
//    {
//        return ParseNodeId(formattedId);
//    }

//    public NodeId Parse(string formattedId, Type runtimeType)
//    {
//        return ParseNodeId(formattedId);
//    }

//    private static NodeId ParseNodeId(string formattedId)
//    {
//        if (!TypeId.TryParse(formattedId, out var typeId))
//        {
//            throw new ArgumentException("Invalid TypeId");
//        }

//        var decodedTypeId = typeId.Decode();

//        return new NodeId(decodedTypeId.Type, decodedTypeId.Id);
//    }
//}
