using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage;
using VRage.Game.ModAPI;

using VRageMath;
using ProtoBuf;
using VRage.ObjectBuilders;
using Sandbox.Game;
using VRage.Game.Entity;
using Sandbox.Game.Weapons;
using VRage.Game;

namespace AiEnabled.ConfigData
{
  [ProtoContract]
  public class InventoryItem
  {
    [ProtoMember(200)] public SerializableDefinitionId ItemDefinition;
    [ProtoMember(201)] public MyFixedPoint Amount;

    public InventoryItem() { }

    public InventoryItem(MyDefinitionId id, MyFixedPoint amount)
    {
      ItemDefinition = id;
      Amount = amount;
    }
  }

  [ProtoContract]
  public class HelperInfo
  {
    [ProtoMember(100)] public long HelperId;
    [ProtoMember(101)] public long GridEntityId;
    [ProtoMember(102)] public string CharacterSubtype;
    [ProtoMember(103)] public string DisplayName;
    [ProtoMember(104)] public string ToolSubtype;
    [ProtoMember(105)] public bool IsActiveHelper;
    [ProtoMember(106)] public SerializableVector3D Position;
    [ProtoMember(107)] public SerializableQuaternion Orientation;
    [ProtoMember(108)] public int Role;
    [ProtoMember(109)] public Color BotColor;
    [ProtoMember(110)] public List<InventoryItem> InventoryItems;

    public HelperInfo() { }

    public HelperInfo(IMyCharacter bot, AiSession.BotType botType, MyCubeGrid grid = null)
    {
      HelperId = bot.EntityId;
      GridEntityId = grid?.EntityId ?? 0L;
      CharacterSubtype = bot.Definition.Id.SubtypeName;
      ToolSubtype = (bot.EquippedTool as IMyHandheldGunObject<MyDeviceBase>)?.DefinitionId.SubtypeName ?? null;
      DisplayName = bot.Name ?? "";
      Position = bot.GetPosition();
      Orientation = Quaternion.CreateFromRotationMatrix(bot.WorldMatrix);
      IsActiveHelper = true;
      Role = (int)botType;

      var hsvOffset = ((MyObjectBuilder_Character)bot.GetObjectBuilder()).ColorMaskHSV;
      var hsv = MyColorPickerConstants.HSVOffsetToHSV(hsvOffset);
      BotColor = hsv.HSVtoColor();

      var inventory = bot.GetInventory() as MyInventory;
      if (inventory?.ItemCount > 0)
      {
        InventoryItems = new List<InventoryItem>();

        var items = inventory.GetItems();
        for (int i = 0; i < items.Count; i++)
        {
          var item = items[i];
          InventoryItems.Add(new InventoryItem(item.Content.GetId(), item.Amount));
        }
      }
    }
  }

  public class HelperData
  {
    public long OwnerIdentityId;
    public Vector3? RepairBotIgnoreColorMask;
    public List<HelperInfo> Helpers;

    public HelperData() { }

    public HelperData(long ident, Vector3? hsv)
    {
      OwnerIdentityId = ident;
      RepairBotIgnoreColorMask = hsv;
      Helpers = new List<HelperInfo>();
    }

    public void AddHelper(IMyCharacter helper, AiSession.BotType botType, MyCubeGrid grid)
    {
      if (Helpers == null)
        Helpers = new List<HelperInfo>();

      Helpers.Add(new HelperInfo(helper, botType, grid));
    }

    public bool RemoveHelper(long id)
    {
      if (Helpers != null)
      {
        for (int i = Helpers.Count - 1; i >= 0; i--)
        {
          if (Helpers[i].HelperId == id)
          {
            Helpers.RemoveAtFast(i);
            return true;
          }
        }
      }

      return false;
    }

    public void Close()
    {
      Helpers?.Clear();
      Helpers = null;
    }
  }
}
