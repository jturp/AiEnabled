using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.ConfigData;

using ProtoBuf;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;

using VRage.Game;

namespace AiEnabled.Networking.Packets
{
  [ProtoContract]
  public class SettingSyncPacket : PacketBase
  {
    [ProtoMember(100)] readonly SaveData _saveData;
    public SettingSyncPacket() { }

    public SettingSyncPacket(SaveData data)
    {
      _saveData = data;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      if (AiSession.Instance.ModSaveData == null)
      {
        AiSession.Instance.ModSaveData = _saveData;
      }
      else
      {
        var data = AiSession.Instance.ModSaveData;
        _saveData.AdditionalHelperSubtypes = data.AdditionalHelperSubtypes;
        _saveData.PlayerHelperData = data.PlayerHelperData;
        AiSession.Instance.ModSaveData = _saveData;
      }

      AiSession.Instance.PlayerMenu?.UpdateAdminSettings(_saveData);
      AiSession.Instance.StartAdminUpdateCounter();

      if (MyAPIGateway.Session.Player != null)
      {
        var factoryDef = new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "RoboFactory");
        var factoryBlockDef = MyDefinitionManager.Static.GetCubeBlockDefinition(factoryDef);
        if (factoryBlockDef != null)
        {
          var maxHelpers = _saveData.MaxHelpersPerPlayer;
          var wasEnabled = factoryBlockDef.Public;

          if (wasEnabled)
          {
            if (maxHelpers == 0)
            {
              factoryBlockDef.Public = false;
              MyAPIGateway.Utilities.ShowMessage($"AiEnabled", "Max helpers set to zero, RoboFactory disabled.");
            }
            else if (!_saveData.AllowRepairBot && !_saveData.AllowCombatBot && !_saveData.AllowCrewBot && !_saveData.AllowScavengerBot)
            {
              factoryBlockDef.Public = false;
              MyAPIGateway.Utilities.ShowMessage($"AiEnabled", "All helper types disabled, RoboFactory disabled.");
            }
          }
          else if (maxHelpers > 0 && (_saveData.AllowRepairBot || _saveData.AllowCombatBot || _saveData.AllowCrewBot || _saveData.AllowScavengerBot))
          {
            factoryBlockDef.Public = true;
            MyAPIGateway.Utilities.ShowMessage($"AiEnabled", $"Max helpers set to {maxHelpers} and at least one helper type enabled, RoboFactory enabled.");
          }
        }
      }

      return MyAPIGateway.Multiplayer.MultiplayerActive && MyAPIGateway.Multiplayer.IsServer;
    }
  }
}
