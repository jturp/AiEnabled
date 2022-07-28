using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Utilities;

using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Bots.Roles
{
  public class EnforcerBot : NeutralBotBase
  {
    public EnforcerBot(IMyCharacter bot, GridBase gridBase, string toolType = null) : base(bot, 12, 20, gridBase)
    {
      Behavior = new NeutralBehavior(this);

      if (toolType == null)
      {
        var rand = MyUtils.GetRandomInt(100);

        if (rand >= 95)
          toolType = "BasicHandHeldLauncherItem";
        else if (rand >= 50)
          toolType = "RapidFireAutomaticRifleItem";
        else
          toolType = "SemiAutoPistolItem";
      }

      _shotAngleDeviationTan = (float)Math.Tan(MathHelper.ToRadians(2.5f));
      ToolDefinition = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), toolType));

      if (ToolDefinition != null)
        MyAPIGateway.Utilities.InvokeOnGameThread(AddWeapon, "AiEnabled");
    }

    public override void AddWeapon()
    {
      var inventory = Character?.GetInventory();
      if (inventory == null)
      {
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING: Inventory was NULL!", MessageType.WARNING);
        return;
      }

      if (ToolDefinition == null)
      {
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING: Tool Definition was NULL!", MessageType.WARNING);
        return;
      }

      var weaponDefinition = ToolDefinition.PhysicalItemId;

      if (inventory.CanItemsBeAdded(1, weaponDefinition))
      {
        var weapon = (MyObjectBuilder_PhysicalGunObject)MyObjectBuilderSerializer.CreateNewObject(weaponDefinition);
        inventory.AddItems(1, weapon);

        string ammoSubtype = null;

        var weaponItemDef = MyDefinitionManager.Static.GetPhysicalItemDefinition(weaponDefinition) as MyWeaponItemDefinition;
        if (weaponItemDef != null)
        {
          var weaponDef = MyDefinitionManager.Static.GetWeaponDefinition(weaponItemDef.WeaponDefinitionId);
          ammoSubtype = weaponDef?.AmmoMagazinesId?.Length > 0 ? weaponDef.AmmoMagazinesId[0].SubtypeName : null;
        }
        else
        {
          AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WeaponItemDef was null for {weaponDefinition}", MessageType.WARNING);
        }

        if (ammoSubtype == null)
        {
          AiSession.Instance.Logger.Log($"AmmoSubtype was still null");

          if (ToolDefinition.WeaponType == MyItemWeaponType.Rifle)
          {
            ammoSubtype = "NATO_5p56x45mm";
          }
          else if (ToolDefinition.WeaponType == MyItemWeaponType.RocketLauncher)
          {
            ammoSubtype = "Missile200mm";
          }
          else if (ToolDefinition.PhysicalItemId.SubtypeName.IndexOf("auto", StringComparison.OrdinalIgnoreCase) >= 0)
          {
            ammoSubtype = ToolDefinition.PhysicalItemId.SubtypeName.StartsWith("Full") ? "FullAutoPistolMagazine" : "SemiAutoPistolMagazine";
          }
          else
          {
            ammoSubtype = "ElitePistolMagazine";
          }
        }

        var ammoDefinition = new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), ammoSubtype);
        var amountThatFits = ((MyInventory)inventory).ComputeAmountThatFits(ammoDefinition);
        var amount = Math.Min((int)amountThatFits, 10);

        if (inventory.CanItemsBeAdded(amount, ammoDefinition))
        {
          var ammo = (MyObjectBuilder_AmmoMagazine)MyObjectBuilderSerializer.CreateNewObject(ammoDefinition);
          inventory.AddItems(amount, ammo);

          var charController = Character as Sandbox.Game.Entities.IMyControllableEntity;
          if (charController.CanSwitchToWeapon(weaponDefinition))
          {
            charController.SwitchToWeapon(weaponDefinition);
            HasWeaponOrTool = true;
            SetShootInterval();
          }
          else
            AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING! Added weapon and ammo but unable to switch to weapon!", MessageType.WARNING);
        }
        else
          AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING! Added weapon but unable to add ammo!", MessageType.WARNING);
      }
      else
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING! Unable to add weapon to inventory!", MessageType.WARNING);
    }

    internal override bool Update()
    {
      if (!base.Update())
        return false;

      if (_tickCount % 100 == 0)
      {
        if (Target.Entity != null && Target.PositionsValid)
        {
          if (Vector3D.DistanceSquared(Character.WorldAABB.Center, Target.CurrentActualPosition) > 150 * 150)
            Target.RemoveTarget();
        }

        if (Target.Entity == null || Target.IsDestroyed())
        {
          if (_botState.IsRunning)
            Character.SwitchWalk();
        }

        if (_shouldMove && Character.EquippedTool == null && ToolDefinition != null)
        {
          var controlEnt = Character as Sandbox.Game.Entities.IMyControllableEntity;
          controlEnt.SwitchToWeapon(ToolDefinition.PhysicalItemId);
        }
      }

      return true;
    }
  }
}
