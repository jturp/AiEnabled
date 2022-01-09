using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace AiEnabled.Graphics.Support
{
  public struct InventoryMapItem
  {
    public IMyInventoryItem InventoryItem;
    public string ItemName;
    public string IconName;

    public InventoryMapItem(IMyInventoryItem item)
    {
      InventoryItem = item;
      var defId = item.Content.GetId();
      var itemDef = MyDefinitionManager.Static.GetDefinition(defId);

      var name = itemDef != null ? itemDef.DisplayNameText : item.Content.SubtypeName;
      ItemName = name.Length > 50 ? name.Substring(0, 50) : name;

      if (itemDef.Context.IsBaseGame)
        IconName = defId.ToString();
      else
      {
        if (itemDef is MyAmmoMagazineDefinition)
          IconName = "GenericAmmo";
        else if (itemDef is MyToolItemDefinition)
          IconName = "GenericTool";
        else if (itemDef is MyComponentDefinition)
          IconName = "GenericComponent";
        else
        {
          var invItem = itemDef as MyPhysicalItemDefinition;
          if (invItem.IsIngot)
            IconName = "GenericIngot";
          else if (invItem.IsOre)
            IconName = "GenericOre";
          else
            IconName = "GenericUnknown";
        }
      }
    }

    public string GetItemAmountString()
    {
      var itemAmount = (float)InventoryItem.Amount;
      string amount;

      if (itemAmount > 1000000)
      {
        amount = $"{itemAmount * 0.000001f:0.#}M";
      }
      else if (itemAmount > 1000)
      {
        amount = $"{itemAmount * 0.001f:0.#}k";
      }
      else
      {
        amount = $"{itemAmount:0.##}";
      }

      return amount;
    }
  }
}
