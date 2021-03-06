using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Networking;
using AiEnabled.Support;
using AiEnabled.Utilities;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using SpaceEngineers.Game.ModAPI;

using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace AiEnabled.Bots.Roles.Helpers
{
  public class CrewBot : NeutralBotBase
  {
    public enum CrewType { NONE, ENGINEER, WEAPONS, FABRICATOR }

    public CrewType CrewFunction;
    public MyCubeBlock AttachedBlock { get; private set; }

    readonly List<IMySlimBlock> _goToBlocks;
    bool _refetchBlocks = true;
    long _lastGridId;

    public CrewBot(IMyCharacter bot, GridBase gridBase, long ownerId, string toolType = null) : base(bot, 12, 20, gridBase)
    {
      BotType = AiSession.BotType.Crew;
      Owner = AiSession.Instance.Players.GetValueOrDefault(ownerId, null);
      Behavior = new CrewBehavior(this);

      _sideNodeWaitTime = 30;
      _followDistanceSqd = 25;
      _ticksSinceFoundTarget = 241;
      _ticksBetweenAttacks = 150;
      _blockDamagePerSecond = 200;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);

      bool hasOwner = Owner != null;
      var jetAllowed = RequiresJetpack || hasOwner || AiSession.Instance.ModSaveData.AllowEnemiesToFly;

      CanUseSpaceNodes = jetAllowed;
      CanUseAirNodes = jetAllowed;
      EnableDespawnTimer = !hasOwner;

      var toolSubtype = toolType ?? "SemiAutoPistolItem";
      ToolDefinition = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), toolSubtype));

      if (!AiSession.Instance.SlimListStack.TryPop(out _goToBlocks) || _goToBlocks == null)
        _goToBlocks = new List<IMySlimBlock>();
      else
        _goToBlocks.Clear();
    }

    internal override void Close(bool cleanConfig = false, bool removeBot = true)
    {
      if (_goToBlocks != null && AiSession.Instance?.SlimListStack != null)
      {
        _goToBlocks.Clear();
        AiSession.Instance.SlimListStack.Push(_goToBlocks);
      }

      base.Close(cleanConfig, removeBot);
    }

    public override void AddWeapon()
    {
      var inventory = Character?.GetInventory();
      if (inventory == null)
      {
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING: Inventory was NULL!", MessageType.WARNING);
        return;
      }

      var weaponDefinition = ToolDefinition?.PhysicalItemId ?? new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), "SemiAutoPistolItem");

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
          AiSession.Instance.Logger.Log($"WeaponItemDef was null for {weaponDefinition}");
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
        var amount = Math.Min((int)amountThatFits, 25);

        if (inventory.CanItemsBeAdded(amount, ammoDefinition))
        {
          var ammo = (MyObjectBuilder_AmmoMagazine)MyObjectBuilderSerializer.CreateNewObject(ammoDefinition);
          inventory.AddItems(amount, ammo);
        }
        else
          AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING! Added weapon but unable to add ammo!", MessageType.WARNING);
      }
      else
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING! Unable to add weapon to inventory!", MessageType.WARNING);
    }

    public void AssignToCrew(MyCubeBlock cube)
    {
      string msg;
      if (cube is IMyPowerProducer || cube is IMyShipController)
      {
        CrewFunction = CrewType.ENGINEER;
        msg = $"{Character.Name} will assume Engineer role.";
      }
      else if (cube is IMyLargeTurretBase)
      {
        CrewFunction = CrewType.WEAPONS;
        msg = $"{Character.Name} will assume Weapons Specialist role.";
      }
      else if (cube is IMyProductionBlock || cube?.BlockDefinition.Id.SubtypeName.IndexOf("kitchen", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        CrewFunction = CrewType.FABRICATOR;
        msg = $"{Character.Name} will assume Fabricator role.";
      }
      else
      {
        CrewFunction = CrewType.NONE;
        msg = $"{Character.Name} will assume no role.";
      }

      if (Owner != null)
      {
        var pkt = new MessagePacket(msg, "White", 3000);
        AiSession.Instance.Network.SendToPlayer(pkt, Owner.SteamUserId);
      }

      _refetchBlocks = true;
      AttachedBlock = (CrewFunction == CrewType.NONE) ? null : cube;
    }

    internal override bool Update()
    {
      if (!base.Update())
        return false;

      var gridGraph = _currentGraph as CubeGridMap;
      if (gridGraph != null)
      {
        if (gridGraph.Dirty)
        {
          _refetchBlocks = true;
        }

        if (gridGraph.MainGrid?.EntityId > 0 && gridGraph.MainGrid.EntityId != _lastGridId)
        {
          _refetchBlocks = true;
          _lastGridId = gridGraph.MainGrid.EntityId;
        }
      }

      if (_tickCount % 100 == 0 && (Target.Entity == null || !(Target.Entity is IMyCharacter) || (Owner != null && Target.Player?.IdentityId == Owner.IdentityId) || Target.IsDestroyed()))
      {
        if (_botState.IsRunning)
          Character.SwitchWalk();

        if (HasWeaponOrTool)
        {
          var controlEnt = Character as Sandbox.Game.Entities.IMyControllableEntity;
          controlEnt?.SwitchToWeapon(null);
          HasWeaponOrTool = false;
          HasLineOfSight = false;
        }
      }

      return true;
    }

    internal override void UseBehavior(bool force = false)
    {
      if (CrewFunction == CrewType.NONE || force)
        base.UseBehavior();
    }

    void CrewBehavior()
    {
      if (CrewFunction != CrewType.NONE)
      {
        UseBehavior(true);

        if (MyUtils.GetRandomInt(0, 100) > 40)
        {
          switch (CrewFunction)
          {
            case CrewType.ENGINEER:
              CheckAndFillBattery();
              break;
            case CrewType.FABRICATOR:
              CreateConsumable();
              break;
            case CrewType.WEAPONS:
              CheckAndFillWeapon();
              break;
          }
        }
      }
    }

    void CheckAndFillWeapon()
    {
      var gridGraph = _currentGraph as CubeGridMap;
      if (gridGraph?.InventoryCache == null || gridGraph.InventoryCache.Locked)
        return;

      var turret = AttachedBlock as IMyLargeInteriorTurret;
      if (turret != null && turret.IsFunctional)
      {
        var inventory = turret.GetInventory() as MyInventoryBase;
        if (inventory != null)
        {
          var weaponBlockDef = MyDefinitionManager.Static.GetCubeBlockDefinition(turret.BlockDefinition) as MyWeaponBlockDefinition;
          if (weaponBlockDef != null)
          {
            var weaponDef = MyDefinitionManager.Static.GetWeaponDefinition(weaponBlockDef.WeaponDefinitionId);
            if (weaponDef?.AmmoMagazinesId.Length > 0)
            {
              var ammoDef = weaponDef.AmmoMagazinesId[0];
              if (inventory.ComputeAmountThatFits(ammoDef) >= 1)
              {
                gridGraph.InventoryCache.TryMoveItem(ammoDef, 1, inventory);
              }
            }
          }
        }
      }
    }

    void CheckAndFillBattery()
    {
      var battery = AttachedBlock as MyBatteryBlock;
      if (battery != null && battery.IsFunctional && battery.CurrentStoredPower < battery.MaxStoredPower)
      {
        float amount = battery.MaxStoredPower * 0.01f;

        battery.CurrentStoredPower += amount;
      }
    }

    void CreateConsumable()
    {
      var cargo = AttachedBlock as IMyCargoContainer;
      if (cargo != null && cargo.IsFunctional && AiSession.Instance?.ConsumableItemList?.Count > 0)
      {
        var rand = MyUtils.GetRandomInt(AiSession.Instance.ConsumableItemList.Count);
        var item = AiSession.Instance.ConsumableItemList[rand];

        var inventory = cargo.GetInventory() as MyInventoryBase;
        if (inventory.ComputeAmountThatFits(item.Id) >= 1)
        {
          inventory.AddItems(1, item.GetObjectBuilder());
        }
      }
    }

    public override void SetTarget()
    {
      if (Owner?.Character != null)
      {
        var character = Owner.Character;

        if (FollowMode)
        {
          var ownerParent = character.GetTopMostParent();
          var currentEnt = Target.Entity as IMyEntity;

          if (currentEnt?.EntityId != ownerParent.EntityId)
          {
            Target.SetTarget(ownerParent);
            _pathCollection?.CleanUp(true);
          }

          return;
        }

        if (PatrolMode && _patrolList?.Count > 0)
        {
          if (Target.Entity != null)
            Target.RemoveTarget();

          if (Target.Override.HasValue)
            return;

          var patrolPoint = GetNextPatrolPoint();

          if (patrolPoint.HasValue)
          {
            _pathCollection?.CleanUp(true);
            Target.SetOverride(patrolPoint.Value);
          }

          return;
        }
      }

      base.SetTarget();
    }

    bool FaceCube()
    {
      var worldPosition = AttachedBlock.PositionComp.GetPosition();
      var vector = Vector3D.TransformNormal(worldPosition - GetPosition(), MatrixD.Transpose(WorldMatrix));

      if (vector.Z < 0 && Math.Abs(vector.X) < 0.5)
        return true;

      var rotation = new Vector2(0, Math.Sign(vector.X) * 75);
      Character.MoveAndRotate(Vector3.Zero, rotation, 0);
      return false;
    }

    internal override void SimulateIdleMovement(bool getMoving, bool towardOwner = false, double distanceCheck = 3)
    {
      try
      {
        if (FollowMode || PatrolMode || !_shouldMove || !AiSession.Instance.ModSaveData.AllowIdleMovement)
          return;

        var gridGraph = _currentGraph as CubeGridMap;

        if (CrewFunction == CrewType.NONE || gridGraph == null || !gridGraph.Ready)
        {
          base.SimulateIdleMovement(getMoving, towardOwner);
          return;
        }

        var botPosition = GetPosition();
        distanceCheck = (_botState.IsRunning || _botState.IsFlying) ? 1 : 0.5;

        if (_moveTo.HasValue)
        {
          Vector3D checkVector = _moveTo.Value;
          if (_pathCollection?.HasPath == true)
          {
            var lastNode = _pathCollection.PathToTarget[_pathCollection.PathToTarget.Count - 1];
            checkVector = _currentGraph.LocalToWorld(lastNode.Position) + lastNode.Offset;
          }

          var vector = Vector3D.TransformNormal(checkVector - botPosition, Matrix.Transpose(WorldMatrix));
          var flattenedVector = new Vector3D(vector.X, 0, vector.Z);

          if (flattenedVector.LengthSquared() <= distanceCheck)
          {
            var cubeOK = AttachedBlock != null;
            if (cubeOK && !FaceCube())
              return;

            _moveTo = null;

            if (cubeOK)
              CrewBehavior();
          }
        }

        if (_moveTo == null)
        {
          if (_idlePathTimer <= 1000)
            return;

          _idlePathTimer = 0;

          if (_refetchBlocks && gridGraph.MainGrid != null)
          {
            var blocks = gridGraph.MainGrid.GetFatBlocks();
            _goToBlocks.Clear();

            switch (CrewFunction)
            {
              case CrewType.FABRICATOR:

                for (int i = 0; i < blocks.Count; i++)
                {
                  var b = blocks[i];
                  if (b == null || b.MarkedForClose || b.Closed || b.Position == AttachedBlock?.Position)
                    continue;

                  if (b is IMyProductionBlock || (b is IMyCargoContainer && b.BlockDefinition.Id.SubtypeName.Contains("Cargo")))
                  {
                    Vector3I _;
                    if (gridGraph.GetClosestValidNode(this, b.Position, out _, isSlimBlock: true))
                      _goToBlocks.Add(b.SlimBlock);
                  }
                }

                break;
              case CrewType.ENGINEER:

                for (int i = 0; i < blocks.Count; i++)
                {
                  var b = blocks[i];
                  if (b == null || b.MarkedForClose || b.Closed || b.Position == AttachedBlock?.Position)
                    continue;

                  if (b is IMyPowerProducer || b is IMyTextPanel)
                  {
                    Vector3I _;
                    if (gridGraph.GetClosestValidNode(this, b.Position, out _, isSlimBlock: true))
                      _goToBlocks.Add(b.SlimBlock);
                  }
                }

                break;
              case CrewType.WEAPONS:

                for (int i = 0; i < blocks.Count; i++)
                {
                  var b = blocks[i];
                  if (b == null || b.MarkedForClose || b.Closed || b.Position == AttachedBlock?.Position)
                    continue;

                  if (b is IMyLargeTurretBase)
                  {
                    Vector3I _;
                    if (gridGraph.GetClosestValidNode(this, b.Position, out _, isSlimBlock: true))
                      _goToBlocks.Add(b.SlimBlock);
                  }
                }

                break;
            }

            _refetchBlocks = false;
          }

          AttachedBlock = null;

          if (_goToBlocks.Count > 0)
          {
            var index = MyUtils.GetRandomInt(_goToBlocks.Count);
            var testBlock = _goToBlocks[index]?.FatBlock;

            if (testBlock == null || testBlock.MarkedForClose || testBlock.Closed)
            {
              _refetchBlocks = true;
            }
            else
            {
              AttachedBlock = testBlock as MyCubeBlock;

              Node n;
              Vector3I outPos;
              if (gridGraph.GetClosestValidNode(this, AttachedBlock.Position, out outPos, isSlimBlock: true) && gridGraph.TryGetNodeForPosition(outPos, out n))
              {
                _prevMoveTo = botPosition;
                _moveTo = gridGraph.LocalToWorld(n.Position) + n.Offset;
              }
              else
              {
                AttachedBlock = null;
                AiSession.Instance.Logger.Log($"{this.GetType().FullName}: Current Cube returned no valid node after assignment", MessageType.WARNING);
              }
            }
          }
        }

        if (_moveTo.HasValue)
        {
          _idlePathTimer = 0;
          base.SimulateIdleMovement(getMoving, towardOwner, distanceCheck);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CrewBot.SimulateIdleMovement: {ex.Message}\n{ex.StackTrace}");
      }
    }
  }
}
