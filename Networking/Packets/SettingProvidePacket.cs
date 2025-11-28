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

using VRage;
using VRage.Game;
using VRage.Utils;

namespace AiEnabled.Networking.Packets
{
  [ProtoContract]
  public class SettingProvidePacket : PacketBase
  {
    [ProtoMember(1)] List<SerializableBotPrice> BotRequirements;
    [ProtoMember(2)] List<string> AdditionalHelperSubtypes;
    [ProtoMember(3)] SaveData Settings;

    public SettingProvidePacket() { }

    public SettingProvidePacket(SaveData data, List<SerializableBotPrice> prices, List<string> helperSubtypes)
    {
      BotRequirements = prices;
      AdditionalHelperSubtypes = helperSubtypes;
      Settings = data;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      if (MyAPIGateway.Session.Player == null)
        return false;

      if (BotRequirements?.Count > 0)
      {
        for (int i = 0; i < BotRequirements.Count; i++)
        {
          var botReq = BotRequirements[i];
          var botType = (AiSession.BotType)botReq.BotType;

          AiSession.Instance.BotComponents[botType] = botReq.Components;
          AiSession.Instance.BotPrices[botType] = botReq.SpaceCredits;
        }
      }

      if (AdditionalHelperSubtypes?.Count > 0)
      {
        var sb = new StringBuilder(32);
        for (int i = 0; i < AdditionalHelperSubtypes.Count; i++)
        {
          var subtype = AdditionalHelperSubtypes[i];
          sb.Clear();

          string nameToUse;

          if (subtype == "Default_Astronaut")
            nameToUse = "Male Engineer";
          else if (subtype == "Default_Astronaut_Female")
            nameToUse = "Female Engineer";
          else if (subtype == "RoboDog")
            nameToUse = "Robo Dog";
          else
          {
            sb.Clear();
            foreach (var ch in subtype)
            {
              if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                sb.Append(ch);
              else if (ch == '_')
                sb.Append(' ');
            }

            nameToUse = sb.ToString();
          }

          var hashId = MyStringId.GetOrCompute(nameToUse);
          if (!AiSession.Instance.BotModelDict.ContainsKey(hashId))
            AiSession.Instance.BotModelDict[hashId] = subtype;
        }

        sb.Clear();
      }

      AiSession.Instance.BotModelList.Clear();
      foreach (var kvp in AiSession.Instance.BotModelDict)
      {
        AiSession.Instance.BotModelList.Add(kvp.Key);
      }

      var efficiency = MyAPIGateway.Session.AssemblerEfficiencyMultiplier;
      var fixedPoint = (MyFixedPoint)(1f / efficiency);
      var remainder = 1 - (fixedPoint * efficiency);
      var componentReqs = new Dictionary<MyDefinitionId, float>(MyDefinitionId.Comparer);

      foreach (var kvp in AiSession.Instance.BotComponents)
      {
        var bType = kvp.Key;
        var subtype = $"AiEnabled_Comp_{bType}BotMaterial";
        var comp = new MyDefinitionId(typeof(MyObjectBuilder_Component), subtype);
        var bpDef = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(comp);

        if (bpDef != null)
        {
          var items = kvp.Value;
          if (items.Count == 0)
            items.Add(new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate"), 1));

          componentReqs.Clear();
          for (int i = 0; i < items.Count; i++)
          {
            var item = items[i];
            var amount = item.Amount;

            var compBp = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(item.DefinitionId);
            if (compBp != null)
            {
              var compReqs = compBp.Prerequisites;
              if (compReqs?.Length > 0)
              {
                for (int j = 0; j < compReqs.Length; j++)
                {
                  var compReq = compReqs[j];

                  float num;
                  componentReqs.TryGetValue(compReq.Id, out num);
                  componentReqs[compReq.Id] = num + (float)compReq.Amount * amount;
                }
              }
            }
          }

          if (componentReqs.Count == 0)
            componentReqs[new MyDefinitionId(typeof(MyObjectBuilder_Ingot), "Iron")] = 100 * efficiency;

          var reqs = new MyBlueprintDefinitionBase.Item[componentReqs.Count];
          int k = 0;

          foreach (var item in componentReqs)
          {
            var req = new MyBlueprintDefinitionBase.Item
            {
              Amount = (MyFixedPoint)item.Value,
              Id = item.Key
            };

            req.Amount *= efficiency;

            if (remainder > 0)
              req.Amount += req.Amount * remainder + remainder;

            reqs[k] = req;
            k++;
          }

          bpDef.Atomic = true;
          bpDef.Prerequisites = reqs;
        }
      }

      componentReqs.Clear();

      if (AiSession.Instance.ModSaveData == null)
      {
        AiSession.Instance.ModSaveData = Settings;
      }
      else
      {
        var data = AiSession.Instance.ModSaveData;

        Settings.AllowedHelperSubtypes = data.AllowedHelperSubtypes;
        Settings.PlayerHelperData = data.PlayerHelperData;
        Settings.AllHumanSubtypes = data.AllHumanSubtypes;
        Settings.AllowedBotRoles = data.AllowedBotRoles;
        Settings.AllowedBotSubtypes = data.AllowedBotSubtypes;
        Settings.InventoryItemsToKeep = data.InventoryItemsToKeep;
        Settings.PlayerHelperData = data.PlayerHelperData;

        AiSession.Instance.ModSaveData = Settings;
      }

      AiSession.Instance.PlayerMenu?.UpdateAdminSettings(Settings);
      AiSession.Instance.StartAdminUpdateCounter();

      var maxHelpers = Settings.MaxHelpersPerPlayer;
      var factoryDef = new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "RoboFactory");
      var factoryBlockDef = MyDefinitionManager.Static.GetCubeBlockDefinition(factoryDef);
      if (factoryBlockDef != null)
      {
        var wasEnabled = factoryBlockDef.Public;

        if (wasEnabled)
        {
          if (maxHelpers == 0)
          {
            factoryBlockDef.Public = false;
            MyAPIGateway.Utilities.ShowMessage($"AiEnabled", "Max helpers set to zero, RoboFactory disabled.");
          }
          else if (!Settings.AllowRepairBot && !Settings.AllowCombatBot && !Settings.AllowCrewBot && !Settings.AllowScavengerBot)
          {
            factoryBlockDef.Public = false;
            MyAPIGateway.Utilities.ShowMessage($"AiEnabled", "All helper types disabled, RoboFactory disabled.");
          }
        }
        else if (maxHelpers > 0 && (Settings.AllowRepairBot || Settings.AllowCombatBot || Settings.AllowCrewBot || Settings.AllowScavengerBot))
        {
          factoryBlockDef.Public = true;
          MyAPIGateway.Utilities.ShowMessage($"AiEnabled", $"Max helpers set to {maxHelpers} and at least one helper type enabled, RoboFactory enabled.");
        }
      }

      var component = new MyDefinitionId(typeof(MyObjectBuilder_Component), "AiEnabled_Comp_CombatBotMaterial");
      var compDef = MyDefinitionManager.Static.GetComponentDefinition(component);
      if (compDef != null)
        compDef.Public = maxHelpers > 0 && Settings.AllowCombatBot;

      var bp = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(component);
      if (bp != null)
        bp.Public = maxHelpers > 0 && Settings.AllowCombatBot;

      component = new MyDefinitionId(typeof(MyObjectBuilder_Component), "AiEnabled_Comp_RepairBotMaterial");
      compDef = MyDefinitionManager.Static.GetComponentDefinition(component);
      if (compDef != null)
        compDef.Public = maxHelpers > 0 && Settings.AllowRepairBot;

      bp = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(component);
      if (bp != null)
        bp.Public = maxHelpers > 0 && Settings.AllowRepairBot;

      component = new MyDefinitionId(typeof(MyObjectBuilder_Component), "AiEnabled_Comp_ScavengerBotMaterial");
      compDef = MyDefinitionManager.Static.GetComponentDefinition(component);
      if (compDef != null)
        compDef.Public = maxHelpers > 0 && Settings.AllowScavengerBot;

      bp = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(component);
      if (bp != null)
        bp.Public = maxHelpers > 0 && Settings.AllowScavengerBot;

      component = new MyDefinitionId(typeof(MyObjectBuilder_Component), "AiEnabled_Comp_CrewBotMaterial");
      compDef = MyDefinitionManager.Static.GetComponentDefinition(component);
      if (compDef != null)
        compDef.Public = maxHelpers > 0 && Settings.AllowCrewBot;

      bp = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(component);
      if (bp != null)
        bp.Public = maxHelpers > 0 && Settings.AllowCrewBot;

      return false;
    }
  }
}
