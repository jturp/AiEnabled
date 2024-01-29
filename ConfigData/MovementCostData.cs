using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

using AiEnabled.Ai.Support;

using ProtoBuf;

namespace AiEnabled.ConfigData
{
  public class MovementCostData
  {
    public string Info;
    public short BaseMovementCost = 5;
    public short OffsetCost_Voxel = 1;
    public short OffsetCost_AirNode = 2;
    public short OffsetCost_WaterNode = 2;
    public short OffsetCost_Door = 10;
    public short OffsetCost_HangarDoor = 25;
    public short OffsetCost_Catwalk = 1;
    public short OffsetCost_Ladder = 3;
    public short OffsetCost_Tunnel = 2;

    [XmlIgnore]
    public Dictionary<string, short> MovementCostDict;

    public MovementCostData() { }

    public void Update()
    {
      Info = $@"
  ATTENTION! Here are some things to know before editing this document:
  - Base Movement Cost applies to all path nodes and cannot be less than 1; the script will assert this.
  - All values are in addition to the base cost!
  - Valid values are between {short.MinValue} and {short.MaxValue}.
  - The overall movement cost cannot be less than 1; the script will assert this.
  ";
      MovementCostDict = new Dictionary<string, short>()
      {
        { "Base", Math.Max((short)1, BaseMovementCost) },
        { "Voxel", OffsetCost_Voxel },
        { "Tunnel", OffsetCost_Tunnel },
        { "Air", OffsetCost_AirNode },
        { "Water", OffsetCost_WaterNode },
        { "Door", OffsetCost_Door },
        { "Hangar", OffsetCost_HangarDoor },
        { "Catwalk", OffsetCost_Catwalk },
        { "Ladder", OffsetCost_Ladder },
      };
    }

    public void Close()
    {
      Info = null;
      MovementCostDict?.Clear();
      MovementCostDict = null;
    }
  }
}
