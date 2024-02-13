using AiEnabled.API;

using ProtoBuf;
using Sandbox.Definitions;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.ModAPI;

namespace AiEnabled.Bots
{
  [ProtoContract]
  public abstract class Priorities
  {
    [ProtoMember(1)] public List<KeyValuePair<string, bool>> PriorityTypes;
    [ProtoMember(2)] public List<KeyValuePair<string, bool>> IgnoreList;

    public Priorities() { }

    internal void AssignDefaults(bool onlyIgnoreList = false)
    {
      IgnoreList = new List<KeyValuePair<string, bool>>();

      foreach (var kvp in AiSession.Instance.IgnoreTypeDictionary)
      {
        IgnoreList.Add(kvp.Value);
      }

      if (onlyIgnoreList)
        return;

      PriorityTypes = new List<KeyValuePair<string, bool>>()
      {
        new KeyValuePair<string, bool>("IMyUserControllableGun", true),
        new KeyValuePair<string, bool>("IMyShipController", true),
        new KeyValuePair<string, bool>("IMyPowerProducer", true),
        new KeyValuePair<string, bool>("IMyThrust", true),
        new KeyValuePair<string, bool>("IMyGyro", true),
        new KeyValuePair<string, bool>("IMyProductionBlock", true),
        new KeyValuePair<string, bool>("IMyDoor", true),
        new KeyValuePair<string, bool>("IMyProgrammableBlock", true),
        new KeyValuePair<string, bool>("IMyProjector", true),
        new KeyValuePair<string, bool>("IMyConveyor", true),
        new KeyValuePair<string, bool>("IMyCargoContainer", true),
        new KeyValuePair<string, bool>("IMyFunctionalBlock", true),
        new KeyValuePair<string, bool>("IMyTerminalBlock", true),
        new KeyValuePair<string, bool>("IMyCubeBlock", true),
        new KeyValuePair<string, bool>("IMySlimBlock", true),
      };

      if (this is TargetPriorities)
        PriorityTypes.Insert(0, new KeyValuePair<string, bool>("IMyCharacter", true));
    }

    internal void UpdateIgnoreList(List<string> ignoreList)
    {
      if (ignoreList == null || ignoreList.Count == 0)
      {
        if (IgnoreList == null)
          IgnoreList = new List<KeyValuePair<string, bool>>();
        else
          IgnoreList.Clear();

        return;
      }

      var list = new List<KeyValuePair<string, bool>>();
      foreach (var item in ignoreList)
      {
        var idx = item.IndexOf("]");
        if (idx >= 0)
        {
          var enabled = item.StartsWith("[X]");
          var name = item.Substring(idx + 1).Trim();

          list.Add(new KeyValuePair<string, bool>(name, enabled));
        }
      }

      UpdateIgnoreList(list);
      list.Clear();
    }

    internal void UpdateIgnoreList(List<KeyValuePair<string, bool>> ignoreList)
    {
      var allInvItems = AiSession.Instance.IgnoreTypeDictionary;

      if (IgnoreList == null)
        IgnoreList = new List<KeyValuePair<string, bool>>(allInvItems.Count);
      else
        IgnoreList.Clear();

      if (ignoreList == null)
        return;

      IgnoreList.AddRange(ignoreList);

      if (ignoreList.Count != allInvItems.Count)
      {
        foreach (var kvp in allInvItems)
        {
          if (IndexOf(kvp.Key.String) < 0)
            IgnoreList.Add(kvp.Value);
        }

        IgnoreList.Sort(AiSession.IgnoreListComparer);
      }
    }

    internal int GetBlockPriority(object item)
    {
      for (int i = 0; i < PriorityTypes.Count; i++)
      {
        var pri = PriorityTypes[i];
        var priName = GetName(pri.Key);

        if (CheckTypeFromString(item, priName))
          return pri.Value ? i : -1;
      }

      return -1;
    }

    internal int IndexOf(string itemOrPriority)
    {
      var pri = GetName(itemOrPriority);

      if (PriorityTypes?.Count > 0)
      {
        for (int i = 0; i < PriorityTypes.Count; i++)
        {
          if (PriorityTypes[i].Key?.Equals(pri, StringComparison.OrdinalIgnoreCase) == true)
            return i;
        }
      }

      if (IgnoreList?.Count > 0)
      {
        for (int i = 0; i < IgnoreList.Count; i++)
        {
          if (IgnoreList[i].Key?.Equals(pri, StringComparison.OrdinalIgnoreCase) == true)
            return i;
        }
      }

      return -1;
    }

    internal void UpdatePriority(int oldIndex, int newIndex)
    {
      PriorityTypes.Move(oldIndex, newIndex);
    }

    internal bool ContainsPriority(string priority)
    {
      if (string.IsNullOrEmpty(priority))
        return false;

      var idx = priority.IndexOf("]");

      if (idx >= 0)
        priority = priority.Substring(idx + 1);

      priority = priority.Trim();

      for (int i = 0; i < PriorityTypes.Count; i++)
      {
        if (PriorityTypes[i].Key.Equals(priority, StringComparison.OrdinalIgnoreCase))
          return true;
      }

      return false;
    }

    internal bool ContainsIgnoreItem(string item)
    {
      if (string.IsNullOrEmpty(item))
        return false;

      var idx = item.IndexOf("]");

      if (idx >= 0)
        item = item.Substring(idx + 1);

      item = item.Trim();

      for (int i = 0; i < IgnoreList.Count; i++)
      {
        if (IgnoreList[i].Key.Equals(item, StringComparison.OrdinalIgnoreCase))
          return true;
      }

      return false;
    }

    internal void AddPriority(string priority, bool enabled)
    {
      if (PriorityTypes == null)
        PriorityTypes = new List<KeyValuePair<string, bool>>();

      if (!ContainsPriority(priority))
      {
        PriorityTypes.Add(new KeyValuePair<string, bool>(priority.Trim(), enabled));
      }
    }

    internal void AddIgnore(string item, bool enabled)
    {
      if (IgnoreList == null)
        IgnoreList = new List<KeyValuePair<string, bool>>();

      if (!ContainsIgnoreItem(item))
      {
        IgnoreList.Add(new KeyValuePair<string, bool>(item.Trim(), enabled));
      }
    }

    internal bool GetEnabled(string itemOrPriority)
    {
      try
      {
        if (string.IsNullOrEmpty(itemOrPriority))
          return false;

        itemOrPriority = itemOrPriority.Trim();

        if (itemOrPriority.StartsWith("[X]"))
          return true;

        var idx = itemOrPriority.IndexOf("]");

        if (idx >= 0)
          itemOrPriority = itemOrPriority.Substring(idx + 1).Trim();

        if (PriorityTypes?.Count > 0)
        {
          for (int i = 0; i < PriorityTypes.Count; i++)
          {
            var pri = PriorityTypes[i];
            if (pri.Key.Equals(itemOrPriority, StringComparison.OrdinalIgnoreCase))
              return pri.Value;
          }
        }

        if (IgnoreList?.Count > 0)
        {
          for (int i = 0; i < IgnoreList.Count; i++)
          {
            var item = IgnoreList[i];
            if (item.Key.Equals(itemOrPriority, StringComparison.OrdinalIgnoreCase))
              return item.Value;
          }
        }

        return false;
      }
      catch { return false; }
    }

    internal string GetName(string item)
    {
      var idx = item.IndexOf("]");

      if (idx >= 0)
        item = item.Substring(idx + 1);

      return item.Trim();
    }

    bool CheckTypeFromString(object item, string priType)
    {
      var block = item as IMySlimBlock;
      var fatBlock = block?.FatBlock;

      switch (priType)
      {
        case "IMyCharacter":
          return item is IMyCharacter;
        case "IMyUserControllableGun":
          return fatBlock is IMyUserControllableGun;
        case "IMyShipController":
          return fatBlock is IMyShipController;
        case "IMyPowerProducer":
          return fatBlock is IMyPowerProducer;
        case "IMyThrust":
          return fatBlock is IMyThrust;
        case "IMyGyro":
          return fatBlock is IMyGyro;
        case "IMyProductionBlock":
          return fatBlock is IMyProductionBlock;
        case "IMyDoor":
          return fatBlock is IMyDoor;
        case "IMyProjector":
          return fatBlock is IMyProjector;
        case "IMyProgrammableBlock":
          return fatBlock is IMyProgrammableBlock;
        case "IMyConveyor":
          return fatBlock is IMyConveyor || fatBlock is IMyConveyorSorter || fatBlock is IMyConveyorTube;
        case "IMyCargoContainer":
          return fatBlock is IMyCargoContainer;
        case "IMyFunctionalBlock":
          return fatBlock is IMyFunctionalBlock;
        case "IMyTerminalBlock":
          return fatBlock is IMyTerminalBlock;
        case "IMyCubeBlock":
          return fatBlock != null;
        default:
          return block != null;
      }
    }
  }

  [ProtoContract]
  public class RepairPriorities : Priorities
  {
    [ProtoMember(1)] public bool WeldBeforeGrind = true;

    public RepairPriorities()
    {
      AssignDefaults();
    }

    public RepairPriorities(List<KeyValuePair<string, bool>> pris)
    {
      if (pris?.Count > 0)
      {
        PriorityTypes = new List<KeyValuePair<string, bool>>(pris);

        if (IgnoreList == null)
          AssignDefaults(true);
      }
      else
      {
        AssignDefaults();
      }
    }

    public RepairPriorities(List<string> pris)
    {
      if (pris?.Count > 0)
      {
        var defaultPris = RemoteBotAPI.GetDefaultRepairPriorities();

        PriorityTypes = new List<KeyValuePair<string, bool>>();

        foreach (var p in pris)
        {
          var idx = p.IndexOf("]");
          if (idx >= 0)
          {
            var enabled = p.Trim().StartsWith("[X]");
            var name = GetName(p);

            PriorityTypes.Add(new KeyValuePair<string, bool>(name, enabled));
          }
          else
          {
            PriorityTypes.Add(new KeyValuePair<string, bool>(p.Trim(), true));
          }
        }

        foreach (var p in defaultPris)
        {
          if (!ContainsPriority(p))
            PriorityTypes.Add(new KeyValuePair<string, bool>(p, false));
        }
      }
      else
      {
        AssignDefaults();
      }
    }
  }

  [ProtoContract]
  public class TargetPriorities : Priorities
  {
    [ProtoMember(1)] public bool DamageToDisable = true;

    public TargetPriorities()
    {
      AssignDefaults();
    }

    public TargetPriorities(List<KeyValuePair<string, bool>> pris)
    {
      if (pris?.Count > 0)
      {
        PriorityTypes = new List<KeyValuePair<string, bool>>(pris);
      }
      else
      {
        AssignDefaults();
      }
    }

    public TargetPriorities(List<string> pris)
    {
      if (pris?.Count > 0)
      {
        var defaultPris = RemoteBotAPI.GetDefaultTargetPriorities();

        PriorityTypes = new List<KeyValuePair<string, bool>>();

        foreach (var p in pris)
        {
          var idx = p.IndexOf("]");
          if (idx >= 0)
          {
            var enabled = p.Trim().StartsWith("[X]");
            var name = GetName(p);

            PriorityTypes.Add(new KeyValuePair<string, bool>(name, enabled));
          }
          else
          {
            PriorityTypes.Add(new KeyValuePair<string, bool>(p.Trim(), true));
          }
        }

        foreach (var p in defaultPris)
        {
          if (!ContainsPriority(p))
            PriorityTypes.Add(new KeyValuePair<string, bool>(p, false));
        }
      }
      else
      {
        AssignDefaults();
      }
    }
  }

  public class KVPComparer : IComparer<KeyValuePair<string, bool>>
  {
    public int Compare(KeyValuePair<string, bool> x, KeyValuePair<string, bool> y)
    {
      return x.Key.CompareTo(y.Key);
    }
  }
}
