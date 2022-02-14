﻿using Sandbox.Common.ObjectBuilders.Definitions;
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
      var itemDef = MyDefinitionManager.Static.GetDefinition(defId) as MyPhysicalItemDefinition;
      var tmDef = MyDefinitionManager.Static.GetDefinition<MyTransparentMaterialDefinition>(defId.SubtypeName);

      var name = itemDef != null ? itemDef.DisplayNameText : item.Content.SubtypeName;
      ItemName = name.Length > 50 ? name.Substring(0, 50) : name;

      if (tmDef != null)
        IconName = tmDef.Id.SubtypeName;
      else if (itemDef == null)
        IconName = "AiEnabled_GenericUnknown";
      else if (itemDef.Context.IsBaseGame || (itemDef.Context.ModItem.PublishedFileId == 2344068716 && !itemDef.IsOre && !itemDef.IsIngot))
        IconName = $"AiEnabled_{defId.ToString()}";
      else if (itemDef is MyConsumableItemDefinition)
        IconName = "AiEnabled_MyObjectBuilder_ConsumableItem/ClangCola";
      else if (itemDef is MyAmmoMagazineDefinition)
        IconName = "AiEnabled_GenericAmmo";
      else if (itemDef is MyToolItemDefinition)
        IconName = "AiEnabled_GenericTool";
      else if (itemDef is MyComponentDefinition)
        IconName = "AiEnabled_GenericComponent";
      else if (itemDef.IsIngot)
        IconName = "AiEnabled_GenericIngot";
      else if (itemDef.IsOre)
        IconName = "AiEnabled_GenericOre";
      else
        IconName = "AiEnabled_GenericUnknown";
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
