using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

using VRage.Game;
using ProtoBuf;
using VRage.ObjectBuilders;

namespace AiEnabled.ConfigData
{
  public class BotPrice
  {
    public AiSession.BotType? BotType;
    public long? SpaceCredits;

    [XmlArrayItem("DefinitionId")]
    public List<SerialId> Components;
  }

  [ProtoContract]
  public class SerialId
  {
    [XmlAttribute("TypeId")]
    [ProtoMember(1)] public string TypeId;

    [XmlAttribute("SubtypeId")]
    [ProtoMember(2)] public string SubtypeId;

    [XmlAttribute("Amount")]
    [ProtoMember(3)] public int Amount;

    [XmlIgnore]
    [ProtoIgnore] MyDefinitionId _definitionId = new MyDefinitionId();

    public SerialId() { }

    public SerialId(SerializableDefinitionId id, int amount)
    {
      bool isNull = id.IsNull();
      TypeId = isNull ? "MyObjectBuilder_TypeGoesHere" : id.TypeIdString;
      SubtypeId = isNull ? "SubtypeGoesHere" : id.SubtypeId;
      Amount = amount;

      _definitionId = isNull ? new MyDefinitionId() : (MyDefinitionId)id;
    }

    public MyDefinitionId DefinitionId
    {
      get
      {
        if (_definitionId.TypeId.IsNull)
        {
          MyObjectBuilderType typeId;
          if (!MyObjectBuilderType.TryParse(TypeId, out typeId))
            throw new Exception($"Incorrect TypeId given for Definition: '{TypeId}'");

          var subtype = SubtypeId.IndexOf("null", StringComparison.OrdinalIgnoreCase) >= 0 ? string.Empty : SubtypeId;
          _definitionId = new MyDefinitionId(typeId, subtype);
        }

        return _definitionId;
      }
    }
  }

  [ProtoContract]
  public class SerializableBotPrice
  {
    [ProtoMember(1)] public int BotType;
    [ProtoMember(2)] public long SpaceCredits;
    [ProtoMember(3)] public List<SerialId> Components;

    public SerializableBotPrice() { }

    public SerializableBotPrice(AiSession.BotType botType, long credits, List<SerialId> comps)
    {
      BotType = (int)botType;
      SpaceCredits = credits;
      Components = comps;
    }
  }

  [XmlType("BotPricing")]
  public class BotPricing
  {
    [XmlElement("BotPrice")]
    public List<BotPrice> BotPrices;
  }
}
