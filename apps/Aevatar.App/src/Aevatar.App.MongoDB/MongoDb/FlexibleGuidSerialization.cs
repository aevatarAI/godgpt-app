using System;
using System.Diagnostics;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace Aevatar.App.MongoDB;

/// <summary>
/// Entry point for MongoDB GUID serialization configuration.
/// Call <see cref="Configure"/> before any MongoDB operations.
/// </summary>
public static class MongoGuidSerialization
{
    /// <summary>
    /// Register the flexible GUID convention globally.
    /// Writes as CSharpLegacy (UuidLegacy); reads both UuidLegacy and UuidStandard.
    /// </summary>
    public static void Configure()
    {
        try
        {
            var conventionPack = new ConventionPack { new FlexibleGuidConvention() };
            ConventionRegistry.Register("LegacyGuidConvention", conventionPack, _ => true);
            Trace.TraceInformation("MongoDB GUID serialization configured: FlexibleGuid (Legacy-priority)");
        }
        catch (Exception)
        {
            // Convention may already be registered - safe to ignore
        }
    }
}

/// <summary>
/// Convention that applies <see cref="FlexibleGuidSerializer"/> to all Guid properties.
/// Priority: CSharpLegacy for writes; reads both UuidLegacy and UuidStandard.
/// </summary>
public class FlexibleGuidConvention : ConventionBase, IMemberMapConvention
{
    public void Apply(BsonMemberMap memberMap)
    {
        if (memberMap.MemberType == typeof(Guid))
        {
            memberMap.SetSerializer(new FlexibleGuidSerializer());
        }
        else if (memberMap.MemberType == typeof(Guid?))
        {
            memberMap.SetSerializer(new NullableSerializer<Guid>(new FlexibleGuidSerializer()));
        }
    }
}

/// <summary>
/// GUID serializer that writes as CSharpLegacy (UuidLegacy, subtype 3) but can
/// read both UuidLegacy and UuidStandard (subtype 4) on deserialization.
/// Solves mixed GUID binary format after MongoDB driver migration.
/// </summary>
public class FlexibleGuidSerializer : SerializerBase<Guid>
{
    public override Guid Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonType = context.Reader.GetCurrentBsonType();
        return bsonType switch
        {
            BsonType.Binary => DeserializeFromBinary(context.Reader),
            BsonType.String => Guid.Parse(context.Reader.ReadString()),
            _ => throw new FormatException($"Cannot deserialize Guid from BsonType {bsonType}.")
        };
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Guid value)
    {
        var bytes = GuidConverter.ToBytes(value, GuidRepresentation.CSharpLegacy);
        context.Writer.WriteBinaryData(new BsonBinaryData(bytes, BsonBinarySubType.UuidLegacy));
    }

    private static Guid DeserializeFromBinary(global::MongoDB.Bson.IO.IBsonReader reader)
    {
        var binaryData = reader.ReadBinaryData();
        return binaryData.SubType switch
        {
            BsonBinarySubType.UuidLegacy =>
                GuidConverter.FromBytes(binaryData.Bytes, GuidRepresentation.CSharpLegacy),
            BsonBinarySubType.UuidStandard =>
                GuidConverter.FromBytes(binaryData.Bytes, GuidRepresentation.Standard),
            _ => new Guid(binaryData.Bytes)
        };
    }
}
