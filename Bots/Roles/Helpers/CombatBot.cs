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

using VRage;
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
  public class CombatBot : BotBase
  {
    uint _lineOfSightTimer;
    int _lastMovementX, _ticksSinceFirePacket;
    bool _firePacketSent;
    List<float> _randoms = new List<float>(10);

    public CombatBot(IMyCharacter bot, GridBase gridBase, long ownerId, string toolType = null) : base(bot, 3, 10, gridBase)
    {
      Owner = AiSession.Instance.Players[ownerId];
      Behavior = new FriendlyBehavior(bot);
      ToolSubtype = toolType ?? "RapidFireAutomaticRifleItem";
      bool hasOwner = Owner != null;

      _sideNodeWaitTime = 30;
      _followDistanceSqd = 25;
      _ticksSinceFoundTarget = 241;
      _ticksBetweenAttacks = 150;
      _blockDamagePerSecond = 200;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);
      _shotAngleDeviationTan = (float)Math.Tan(MathHelper.ToRadians(1.5f));

      var jetRequired = bot.Definition.Id.SubtypeName == "Drone_Bot";
      var jetAllowed = jetRequired || hasOwner || AiSession.Instance.ModSaveData.AllowEnemiesToFly;

      RequiresJetpack = jetRequired;
      CanUseSpaceNodes = jetAllowed;
      CanUseAirNodes = jetAllowed;
      GroundNodesFirst = !jetRequired;
      EnableDespawnTimer = !hasOwner;
      CanUseWaterNodes = true;
      WaterNodesOnly = false;
      CanUseSeats = true;
      CanUseLadders = true;
      WantsTarget = true;
      ShouldLeadTargets = true;

      if (!AiSession.Instance.SoundListStack.TryPop(out _attackSounds))
        _attackSounds = new List<MySoundPair>();
      else
        _attackSounds.Clear();

      if (!AiSession.Instance.StringListStack.TryPop(out _attackSoundStrings))
        _attackSoundStrings = new List<string>();
      else
        _attackSoundStrings.Clear();

      _attackSounds.Add(new MySoundPair("DroneLoopSmall"));
      _attackSoundStrings.Add("DroneLoopSmall");

      var jetpack = bot.Components.Get<MyCharacterJetpackComponent>();
      if (jetpack != null)
      {
        if (RequiresJetpack)
        {
          if (!jetpack.TurnedOn)
          {
            var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
            MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
            jetpack.TurnOnJetpack(true);
            MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
          }
        }
        else if (jetpack.TurnedOn)
        {
          jetpack.SwitchThrusts();
        }
      }
    }

    public override void AddWeapon()
    {
      var inventory = Character?.GetInventory();
      if (inventory == null)
      {
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING: Inventory was NULL!", MessageType.WARNING);
        return;
      }

      var weaponDefinition = new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), ToolSubtype);

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

          if (ToolSubtype.IndexOf("rifle", StringComparison.OrdinalIgnoreCase) >= 0)
          {
            ammoSubtype = "NATO_5p56x45mm";
          }
          else if (ToolSubtype.IndexOf("launcher", StringComparison.OrdinalIgnoreCase) >= 0)
          {
            ammoSubtype = "Missile200mm";
          }
          else if (ToolSubtype.IndexOf("auto", StringComparison.OrdinalIgnoreCase) >= 0)
          {
            ammoSubtype = ToolSubtype.StartsWith("Full") ? "FullAutoPistolMagazine" : "SemiAutoPistolMagazine";
          }
          else
          {
            ammoSubtype = "ElitePistolMagazine";
          }
        }

        var ammoDefinition = new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), ammoSubtype);
        var amountThatFits = ((MyInventory)inventory).ComputeAmountThatFits(ammoDefinition);
        var amount = MyFixedPoint.Min(amountThatFits, 25);

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

    internal override void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1)
    {
      Vector3 movement;
      Vector2 rotation;
      float roll;
      bool shouldAttack, shouldFire;
      TrySwitchWalk();

      GetMovementAndRotation(isTgt, point, out movement, out rotation, out roll, out shouldAttack, out shouldFire, distanceCheck);
      CheckFire(shouldFire, shouldAttack, ref movement);
      MoveToPoint(movement, rotation, roll);
    }

    internal override void MoveToTarget()
    {
      if (!IsInRangeOfTarget())
      {
        if (!UseAPITargets)
          SimulateIdleMovement(true);

        return;
      }

      if (!Target.PositionsValid)
        return;

      var actualPosition = Target.CurrentActualPosition;

      if (UsePathFinder)
      {
        var gotoPosition = Target.CurrentGoToPosition;
        UsePathfinder(gotoPosition, actualPosition);
        return;
      }

      Vector3 movement;
      Vector2 rotation;
      float roll;
      bool shouldAttack, shouldFire;
      GetMovementAndRotation(Target.Entity != null, actualPosition, out movement, out rotation, out roll, out shouldAttack, out shouldFire);
      CheckFire(shouldFire, shouldAttack, ref movement);
      Character.MoveAndRotate(movement, rotation, roll);
    }

    byte _sideNodeTimer, _sideNodeWaitTime;
    internal override bool Update()
    {
      if (!base.Update())
        return false;
      
      if (_sideNodeTimer < byte.MaxValue)
        ++_sideNodeTimer;

      if (_firePacketSent)
      {
        _ticksSinceFirePacket++;
        if (_ticksSinceFirePacket > TicksBetweenProjectiles * 20)
          _firePacketSent = false;
      }

      if (WaitForLOSTimer)
      {
        ++_lineOfSightTimer;
        if (_lineOfSightTimer > 100)
        {
          _lineOfSightTimer = 0;
          WaitForLOSTimer = false;
        }
      }

      if (HasWeaponOrTool)
      {
        var gun = Character?.EquippedTool as IMyHandheldGunObject<MyGunBase>;
        if (gun != null)
        {
          var ammoCount = gun.CurrentMagazineAmount;
          if (ammoCount <= 0 && !MyAPIGateway.Session.CreativeMode)
          {
            var ammoType = gun.GunBase.CurrentAmmoMagazineDefinition;
            var controlEnt = Character as Sandbox.Game.Entities.IMyControllableEntity;
            controlEnt?.SwitchToWeapon(null);
            HasWeaponOrTool = HasLineOfSight = false;

            if (Owner != null)
            {
              var pkt = new MessagePacket($"{Character.Name} is out of ammunition ({ammoType.DisplayNameText})!");
              AiSession.Instance.Network.SendToPlayer(pkt, Owner.SteamUserId);
            }

            if (_pathCollection != null)
              _pathCollection.Dirty = true;
          }
          else if (Target.HasTarget && !(Character.Parent is IMyCockpit))
            MyAPIGateway.Utilities.InvokeOnGameThread(CheckLineOfSight, "AiEnabled");
          else
            HasLineOfSight = false;
        }
        else
          HasLineOfSight = false;
      }

      return true;
    }

    internal override bool IsInRangeOfTarget() => true;

    public override void SetTarget()
    {
      var character = Owner?.Character;
      if (character == null || !WantsTarget)
      {
        return;
      }

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

      var botPosition = GetPosition();
      if (Target.IsDestroyed())
      {
        Target.RemoveTarget();
      }
      else if (Target.Entity != null && HasLineOfSight)
      {
        // if the current target is viable there's not reason to keep trying to switch targets
        var ch = Target.Entity as IMyCharacter;
        if (ch != null && ch.EntityId != character.EntityId && Vector3D.DistanceSquared(ch.WorldAABB.Center, botPosition) < 3000)
          return;
      }

      List<IHitInfo> hitList;
      if (!AiSession.Instance.HitListStack.TryPop(out hitList))
        hitList = new List<IHitInfo>();
      else
        hitList.Clear();

      List<MyEntity> entities;
      if (!AiSession.Instance.EntListStack.TryPop(out entities))
        entities = new List<MyEntity>();
      else
        entities.Clear();

      List<MyEntity> gridTurrets;
      if (!AiSession.Instance.EntListStack.TryPop(out gridTurrets))
        gridTurrets = new List<MyEntity>();

      List<IMyCubeGrid> gridGroups;
      if (!AiSession.Instance.GridGroupListStack.TryPop(out gridGroups))
        gridGroups = new List<IMyCubeGrid>();

      HashSet<long> checkedGridIDs;
      if (!AiSession.Instance.GridCheckHashStack.TryPop(out checkedGridIDs))
        checkedGridIDs = new HashSet<long>();
      else
        checkedGridIDs.Clear();

      var onPatrol = PatrolMode && _patrolList?.Count > 0;
      var ownerPos = character.WorldAABB.Center;
      var ownerHeadPos = character.GetHeadMatrix(true).Translation;
      var botHeadPos = Character.GetHeadMatrix(true).Translation;

      var centerPoint = ownerPos;
      var distance = AiSession.Instance.ModSaveData.MaxBotHuntingDistanceFriendly;

      if (onPatrol)
      {
        centerPoint = botPosition;
        var enemyHuntDistance = AiSession.Instance.ModSaveData.MaxBotHuntingDistanceEnemy;

        if (enemyHuntDistance > distance && distance < 300)
          distance = Math.Min(300, enemyHuntDistance);
      }

      var sphere = new BoundingSphereD(centerPoint, distance);
      var blockDestroEnabled = MyAPIGateway.Session.SessionSettings.DestructibleBlocks;
      var queryType = blockDestroEnabled ? MyEntityQueryType.Both : MyEntityQueryType.Dynamic;

      MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entities, queryType);
      entities.ShellSort(centerPoint);

      IMyEntity tgt = null;
      List<BotBase> helpers;
      AiSession.Instance.PlayerToHelperDict.TryGetValue(Owner.IdentityId, out helpers);
      var ownerId = Owner?.IdentityId ?? Character.ControllerInfo.ControllingIdentityId;

      for (int i = 0; i < entities.Count; i++)
      {
        var ent = entities[i];
        if (ent?.MarkedForClose != false)
          continue;

        long entOwnerId;
        var ch = ent as IMyCharacter;
        var grid = ent as MyCubeGrid;

        if (ch != null)
        {
          if (ch.IsDead || ch.MarkedForClose || ch.EntityId == character.EntityId || ch.EntityId == Character.EntityId)
            continue;

          BotBase bot;
          if (AiSession.Instance.Bots.TryGetValue(ch.EntityId, out bot) && bot != null && !bot.IsDead)
          {
            if (bot._botState?.IsOnLadder == true)
              continue;

            if (helpers != null)
            {
              bool found = false;
              foreach (var otherBot in helpers)
              {
                if (ch.EntityId == otherBot.Character?.EntityId)
                {
                  found = true;
                  break;
                }
              }

              if (found)
                continue;
            }
          }

          var worldHeadPos = ch.GetHeadMatrix(true).Translation;

          if (!onPatrol)
            MyAPIGateway.Physics.CastRay(ownerHeadPos, worldHeadPos, hitList, CollisionLayers.CharacterCollisionLayer);

          if (onPatrol || hitList.Count > 0)
          {
            bool valid = true;
            foreach (var hit in hitList)
            {
              var hitEnt = hit.HitEntity as IMyCharacter;
              if (hitEnt != null)
              {
                if (hitEnt.EntityId == character.EntityId || hitEnt.EntityId == Character.EntityId || hitEnt.EntityId == ch.EntityId)
                  continue;

                if (helpers != null)
                {
                  bool found = false;
                  foreach (var otherBot in helpers)
                  {
                    if (hitEnt.EntityId == otherBot.Character?.EntityId)
                    {
                      found = true;
                      break;
                    }
                  }

                  if (found)
                    continue;
                }

                valid = false;
                break;
              }
              else
              {
                valid = false;
                break;
              }
            }

            if (!valid)
            {
              hitList.Clear();
              MyAPIGateway.Physics.CastRay(botHeadPos, worldHeadPos, hitList, CollisionLayers.CharacterCollisionLayer);

              valid = true;
              foreach (var hit in hitList)
              {
                var hitEnt = hit.HitEntity as IMyCharacter;
                if (hitEnt != null)
                {
                  if (hitEnt.EntityId == character.EntityId || hitEnt.EntityId == Character.EntityId || hitEnt.EntityId == ch.EntityId)
                    continue;

                  if (helpers != null)
                  {
                    bool found = false;
                    foreach (var otherBot in helpers)
                    {
                      if (hitEnt.EntityId == otherBot.Character?.EntityId)
                      {
                        found = true;
                        break;
                      }
                    }

                    if (found)
                      continue;
                  }

                  valid = false;
                  break;
                }
                else
                {
                  valid = false;
                  break;
                }
              }

              if (!valid)
                continue;
            }
          }

          var relation = MyIDModule.GetRelationPlayerPlayer(ownerId, ch.ControllerInfo.ControllingIdentityId, MyRelationsBetweenFactions.Neutral);
          if (relation == MyRelationsBetweenPlayers.Enemies)
          {
            tgt = ch;
            break;
          }
        }
        else if (HasWeaponOrTool && blockDestroEnabled && grid?.Physics != null)
        {
          if (grid.IsPreview || grid.MarkedAsTrash || grid.MarkedForClose || checkedGridIDs.Contains(grid.EntityId))
            continue;

          gridTurrets.Clear();
          gridGroups.Clear();
          grid.GetGridGroup(GridLinkTypeEnum.Logical).GetGrids(gridGroups);
          var thisGridOwner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : 0;

          foreach (var g in gridGroups)
          {
            var myGrid = g as MyCubeGrid;
            if (myGrid == null || myGrid.MarkedForClose)
              continue;

            checkedGridIDs.Add(g.EntityId);
            var myGridOwner = myGrid.BigOwners?.Count > 0 ? myGrid.BigOwners[0] : myGrid.SmallOwners?.Count > 0 ? myGrid.SmallOwners[0] : 0;

            if (myGridOwner != 0 && (thisGridOwner == 0 || grid.BlocksCount < myGrid.BlocksCount))
            {
              thisGridOwner = myGridOwner;
              grid = myGrid;
            }
          }

          var playerControlling = MyAPIGateway.Players.GetPlayerControllingEntity(ent);
          if (playerControlling != null)
            entOwnerId = playerControlling.IdentityId;
          else if (thisGridOwner != 0)
            entOwnerId = thisGridOwner;
          else
            continue;

          var relation = MyIDModule.GetRelationPlayerPlayer(ownerId, entOwnerId);
          if (relation != MyRelationsBetweenPlayers.Enemies)
            continue;

          foreach (var g in gridGroups)
          {
            var myGrid = g as MyCubeGrid;
            if (myGrid == null || myGrid.MarkedForClose)
              continue;

            var blockCounter = myGrid?.BlocksCounters;
            var hasTurretOrRotor = (AiSession.Instance.WcAPILoaded && AiSession.Instance.WcAPI.HasGridAi(myGrid))
              || (blockCounter != null
              && (blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_LargeGatlingTurret), 0) > 0
              || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_LargeMissileTurret), 0) > 0
              || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_InteriorTurret), 0) > 0
              || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_SmallMissileLauncher), 0) > 0
              || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_SmallMissileLauncherReload), 0) > 0
              || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_SmallGatlingGun), 0) > 0
              || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_MotorStator), 0) > 0
              || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_MotorAdvancedStator), 0) > 0));

            if (hasTurretOrRotor)
            {
              var blocks = myGrid.GetFatBlocks();
              for (int j = 0; j < blocks.Count; j++)
              {
                var b = blocks[j];
                if (b == null || b.MarkedForClose)
                  continue;

                var stator = b as IMyMotorStator;
                if (stator != null)
                {
                  if (!stator.MarkedForClose && stator.Enabled && stator.TopGrid != null)
                    gridTurrets.Add(b);
                }
                else if (b.IsWorking && (AiSession.Instance.AllCoreWeaponDefinitions.Contains(b.BlockDefinition.Id)
                  || b is IMyMotorStator || b is IMyLargeTurretBase || b is IMySmallGatlingGun
                  || b is IMySmallMissileLauncher || b is IMySmallMissileLauncherReload))
                {
                  gridTurrets.Add(b);
                }
              }
            }
          }

          if (gridTurrets.Count > 0)
          {
            var dToTurret = double.MaxValue;
            MyEntity turretEnt = null;

            // check for weapons or rotors
            for (int j = gridTurrets.Count - 1; j >= 0; j--)
            {
              var turret = gridTurrets[j];

              var d = Vector3D.DistanceSquared(turret.PositionComp.GetPosition(), botPosition);
              if (d < dToTurret)
              {
                turretEnt = turret;
                dToTurret = d;
              }
            }

            if (turretEnt == null)
              continue;

            tgt = turretEnt;
          }
        }
        else
          continue;

        if (tgt != null)
          break;
      }

      hitList.Clear();
      entities.Clear();
      gridTurrets.Clear();
      gridGroups.Clear();
      checkedGridIDs.Clear();

      AiSession.Instance.HitListStack.Push(hitList);
      AiSession.Instance.EntListStack.Push(entities);
      AiSession.Instance.EntListStack.Push(gridTurrets);
      AiSession.Instance.GridGroupListStack.Push(gridGroups);
      AiSession.Instance.GridCheckHashStack.Push(checkedGridIDs);

      if (tgt == null)
      {
        if (onPatrol)
        {
          if (Target.Entity != null)
            Target.RemoveTarget();

          if (Target.Override.HasValue)
            return;

          var patrolPoint = GetNextPatrolPoint();

          if (patrolPoint.HasValue)
          {
            Target.SetOverride(patrolPoint.Value);
          }

          return;
        }
        else
        {
          tgt = character;
        }
      }

      if (onPatrol && Target.Override.HasValue)
      {
        _patrolIndex = Math.Max((short)-1, (short)(_patrolIndex - 1));
        Target.RemoveOverride(false);
      }

      var parent = tgt is IMyCharacter ? tgt.GetTopMostParent() : tgt;
      var currentTgt = Target.Entity as IMyEntity;
      if (currentTgt?.EntityId == parent.EntityId)
        return;

      Target.SetTarget(parent);
      _pathCollection?.CleanUp(true);
    }

    internal override void Close(bool cleanConfig = false)
    {
      try
      {
        _randoms?.Clear();
        _randoms = null;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CombatBot.Close: {ex.Message}\n{ex.StackTrace}");
      }
      finally
      {
        base.Close(cleanConfig);
      }
    }

    bool FireWeapon()
    {
      var gun = Character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
      if (gun == null)
        return false;

      var targetEnt = Target.Entity as IMyEntity;
      if (targetEnt == null)
        return false;

      if (gun.NeedsReload)
      {
        gun.Reload();
        return false;
      }
      else if (gun.IsReloading)
        return false;

      if (!_firePacketSent)
      {
        _randoms.Clear();
        for (int i = 0; i < 10; i++)
        {
          var rand = MyUtils.GetRandomFloat(0, 1);
          _randoms.Add(rand);
        }

        bool isLauncher = ToolSubtype.IndexOf("handheldlauncher", StringComparison.OrdinalIgnoreCase) >= 0;

        if (isLauncher)
        {
          AiSession.Instance.Projectiles.AddMissileForBot(this, targetEnt);
        }
        else
        {
          int ammoCount;
          if (MyAPIGateway.Session.CreativeMode || AiSession.Instance.InfiniteAmmoEnabled)
          {
            if (ToolSubtype.IndexOf("pistol", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              ammoCount = ToolSubtype.IndexOf("fullauto", StringComparison.OrdinalIgnoreCase) >= 0 ? 20 : 10;
            }
            else
            {
              ammoCount = 30;
            }
          }
          else
          {
            ammoCount = gun.GunBase.CurrentAmmo;
          }

          bool leadTargets = ShouldLeadTargets;

          if (MyAPIGateway.Multiplayer.MultiplayerActive)
          {
            var packet = new WeaponFirePacket(Character.EntityId, targetEnt.EntityId, 10, _shotAngleDeviationTan, _randoms, TicksBetweenProjectiles, ammoCount, false, false, leadTargets);
            AiSession.Instance.Network.RelayToClients(packet);
          }

          AiSession.Instance.StartWeaponFire(Character.EntityId, targetEnt.EntityId, 10, _shotAngleDeviationTan, _randoms, TicksBetweenProjectiles, ammoCount, false, false, leadTargets);
        }

        _firePacketSent = true;
        _ticksSinceFirePacket = 0;
      }

      return true;
    }

    bool _moveFromLadder;

    public void GetMovementAndRotation(bool isTarget, Vector3D waypoint, out Vector3 movement, out Vector2 rotation, out float roll,
      out bool fistAttack, out bool rifleAttack, double distanceCheck = 4)
    {
      roll = 0;
      rifleAttack = false;
      var botPosition = GetPosition();
      var botMatrix = WorldMatrix;
      var graphMatrix = _currentGraph.WorldMatrix;
      var graphUpVector = graphMatrix.Up;
      var jpEnabled = JetpackEnabled;

      var vecToWP = waypoint - botPosition;
      var relVectorBot = Vector3D.TransformNormal(vecToWP, MatrixD.Transpose(botMatrix));
      var tgtFriendly = Target.IsFriendly();
      var isFriendly = isTarget && tgtFriendly;
      var flatDistanceCheck = isFriendly ? _followDistanceSqd : distanceCheck;

      if (_botState.IsOnLadder)
      {
        movement = relVectorBot.Y > 0 ? Vector3.Forward : Vector3.Backward;
        rotation = Vector2.Zero;
        fistAttack = false;
        return;
      }

      if (jpEnabled)
      {
        var deviationAngle = MathHelper.PiOver2 - VectorUtils.GetAngleBetween(graphUpVector, botMatrix.Left);
        var botdotUp = botMatrix.Up.Dot(graphMatrix.Up);

        if (botdotUp < 0 || Math.Abs(deviationAngle) > _twoDegToRads)
        {
          var botLeftDotUp = -botMatrix.Left.Dot(graphUpVector);

          if (botdotUp < 0)
            roll = MathHelper.Pi * Math.Sign(botLeftDotUp);
          else
            roll = (float)deviationAngle * Math.Sign(botLeftDotUp);
        }
      }

      var projUp = VectorUtils.Project(vecToWP, botMatrix.Up);
      var reject = vecToWP - projUp;
      var angle = VectorUtils.GetAngleBetween(botMatrix.Forward, reject);
      var angleTwoOrLess = relVectorBot.Z < 0 && Math.Abs(angle) < _twoDegToRads;

      if (!WaitForStuckTimer && angleTwoOrLess)
      {
        rotation = Vector2.Zero;
      }
      else
      {
        float xRot = 0;

        if (jpEnabled && Math.Abs(roll) < MathHelper.ToRadians(5))
        {
          var angleFwd = MathHelperD.PiOver2 - VectorUtils.GetAngleBetween(botMatrix.Forward, graphUpVector);
          var botDotUp = botMatrix.Up.Dot(graphMatrix.Up);

          if (botDotUp < 0 || Math.Abs(angleFwd) > _twoDegToRads)
          {
            var botFwdDotUp = botMatrix.Forward.Dot(graphMatrix.Up);

            if (botDotUp < 0)
              xRot = -MathHelper.Pi * Math.Sign(botFwdDotUp);
            else
              xRot = (float)angleFwd * Math.Sign(botFwdDotUp);
          }
        }

        rotation = new Vector2(xRot, (float)angle * Math.Sign(relVectorBot.X) * 75);
      }

      var flattenedVector = new Vector3D(relVectorBot.X, 0, relVectorBot.Z);
      var flattenedLengthSquared = flattenedVector.LengthSquared();
      var distanceSqd = relVectorBot.LengthSquared();
      var isOwnerTgt = Target.Player?.IdentityId == Owner.IdentityId;

      var gotoPosition = Target.CurrentGoToPosition;
      var actualPosition = Target.CurrentActualPosition;

      var maxProjectileDistance = AiSession.Instance.ModSaveData.MaxBotProjectileDistance * 0.9;
      double distanceSqdToTarget = (HasWeaponOrTool && Target.HasTarget) ? Vector3D.DistanceSquared(actualPosition, botPosition) : double.MaxValue;
      double distanceToCheck = maxProjectileDistance * maxProjectileDistance;

      if (distanceSqdToTarget <= distanceToCheck)
      {
        if (HasLineOfSight)
        {
          rifleAttack = !tgtFriendly;

          if (_moveFromLadder)
          {
            _moveFromLadder = false;
            _sideNode = null;
          }
        }
      }
      else if (!_moveFromLadder || (_sideNode.HasValue && Vector3D.DistanceSquared(botPosition, _sideNode.Value) > 400))
        _sideNode = null;

      if (_currentGraph != null)
      {
        var localPos = _currentGraph.WorldToLocal(botPosition);
        var worldPosAligned = _currentGraph.LocalToWorld(localPos);
        if (Vector3D.DistanceSquared(worldPosAligned, waypoint) >= _currentGraph.CellSize * _currentGraph.CellSize)
        {
          var relVectorWP = Vector3D.Rotate(waypoint - worldPosAligned, MatrixD.Transpose(botMatrix));
          var flattenedVecWP = new Vector3D(relVectorWP.X, 0, relVectorWP.Z);

          if (Vector3D.IsZero(flattenedVecWP, 0.1))
          {
            var absY = Math.Abs(relVectorBot.Y);
            if (!JetpackEnabled || absY <= 0.1)
            {
              if (!_currentGraph.IsGridGraph && absY > 0.5 && relVectorBot.Y < 0)
              {
                _pathCollection.ClearNode();
                rotation = Vector2.Zero;
                movement = Vector3.Forward;
              }
              else
              {
                movement = Vector3.Zero;
              }
            }
            else
            {
              rotation = Vector2.Zero;
              movement = Math.Sign(relVectorBot.Y) * Vector3.Up * 2;
            }

            fistAttack = !isOwnerTgt && !HasWeaponOrTool && isTarget && angleTwoOrLess && distanceSqd <= distanceCheck;
            return;
          }
        }
      }

      if (rifleAttack || _sideNode.HasValue)
      {
        if (UsePathFinder)
        {
          var vecToTgt = actualPosition - botPosition;
          var relToTarget = Vector3D.TransformNormal(vecToTgt, MatrixD.Transpose(botMatrix));
          projUp = VectorUtils.Project(vecToTgt, botMatrix.Up);
          reject = vecToTgt - projUp;
          angle = VectorUtils.GetAngleBetween(botMatrix.Forward, reject);

          if (relToTarget.Z < 0 && Math.Abs(angle) < _twoDegToRads)
          {
            rotation = Vector2.Zero;
          }
          else
          {
            if (rifleAttack && Target.Entity is IMyLargeTurretBase && Math.Abs(angle) > VectorUtils.PiOver3)
              rifleAttack = false;

            rotation = new Vector2(0, (float)angle * Math.Sign(relToTarget.X) * 75);
          }

          Vector3I node;
          if (_sideNode.HasValue)
          {
            _sideNodeTimer = 0;
            //if (_isShooting)
            //  movement = Vector3.Zero;
            //else
            {
              var dirVec = _sideNode.Value - botPosition;
              var relativeDir = Vector3D.TransformNormal(dirVec, MatrixD.Transpose(botMatrix));
              var absX = Math.Abs(relativeDir.X);
              var absZ = Math.Abs(relativeDir.Z);

              var xMove = absX <= 0.5 ? 0 : Math.Sign(relativeDir.X);
              var zMove = absZ <= 0.5 ? 0 : Math.Sign(relativeDir.Z);
              if (xMove == 0 && zMove == 0)
                _sideNode = null;

              movement = new Vector3(xMove, 0, zMove);
            }
          }
          else if (_currentGraph != null && _sideNodeTimer > _sideNodeWaitTime && _currentGraph.GetRandomNodeNearby(this, waypoint, out node))
          {
            var worldNode = _currentGraph.LocalToWorld(node);
            _sideNode = worldNode;
            _sideNodeTimer = 0;

            if (Vector3D.DistanceSquared(worldNode, actualPosition) < 10)
            {
              Node testNode;
              var dir = Vector3D.Normalize(worldNode - actualPosition);
              _currentGraph.GetRandomOpenNode(this, botPosition + dir * 15, out testNode);

              if (testNode != null)
              {
                Vector3D? addVec = (_currentGraph.LocalToWorld(testNode.Position) + testNode.Offset) - botPosition;
                _sideNode += addVec;
              }
              else
                _sideNode += new Vector3D?(dir * 5);
            }

            var dirVec = _sideNode.Value - botPosition;
            var relativeDir = Vector3D.TransformNormal(dirVec, MatrixD.Transpose(botMatrix));

            var xMove = Math.Abs(relativeDir.X) < 0.5 ? 0 : Math.Sign(relativeDir.X);
            var zMove = Math.Abs(relativeDir.Z) < 0.5 ? 0 : Math.Sign(relativeDir.Z);
            movement = new Vector3(xMove, 0, zMove);
          }
          else
            movement = Vector3.Zero;
        }
        else
        {
          if (Math.Abs(flattenedVector.Z) < 20 && relVectorBot.Y > 5)
            movement = Vector3.Forward;
          else if (flattenedLengthSquared < 900)
            movement = Vector3.Backward;
          else
          {
            if (_xMoveTimer > 100)
            {
              _xMoveTimer = 0;
              var num = MyUtils.GetRandomInt(0, 8);
              _lastMovementX = (num < 3) ? -1 : (num < 5) ? 0 : 1;
            }

            movement = new Vector3(_lastMovementX, 0, 0);
          }
        }
      }
      else if (PathFinderActive)
      {
        if (flattenedLengthSquared > flatDistanceCheck || Math.Abs(relVectorBot.Y) > distanceCheck)
        {
          if (_currentGraph.IsGridGraph)
          {
            MyCubeBlock ladder;
            if (flattenedLengthSquared <= flatDistanceCheck && relVectorBot.Y > 0 && Target.OnLadder(out ladder))
            {
              var gridGraph = _currentGraph as CubeGridMap;
              _sideNode = gridGraph.GetLastValidNodeOnLine(ladder.PositionComp.WorldAABB.Center, ladder.WorldMatrix.Forward, 20);
              _moveFromLadder = true;
            }
            else
              _moveFromLadder = false;
          }
          else 
            _moveFromLadder = false;

          if (!JetpackEnabled && Owner?.Character != null && Target.Player?.IdentityId == Owner.IdentityId)
          {
            var ch = Character as Sandbox.Game.Entities.IMyControllableEntity;
            var distanceToTarget = Vector3D.DistanceSquared(gotoPosition, botPosition);
            if (distanceToTarget > 200)
            {
              ch.Sprint(true);
            }
            else
            {
              ch.Sprint(false);

              if (!rifleAttack)
              {
                var botRunning = _botState.IsRunning;
                if (distanceToTarget > 100)
                {
                  if (!botRunning)
                    ch.SwitchWalk();
                }
                else if (botRunning)
                  ch.SwitchWalk();
              }
            }
          }

          movement = _moveFromLadder ? Vector3.Zero : Vector3.Forward;
        }
        else
          movement = Vector3.Zero;
      }
      else if (HasWeaponOrTool && WaitForLOSTimer)
      {
        int zMove;
        if (Math.Abs(flattenedVector.Z) < 30 && relVectorBot.Y > 5)
          zMove = 1;
        else
          zMove = (flattenedLengthSquared > 100) ? -1 : 0;

        movement = new Vector3(1, 0, zMove);
      }
      else if (flattenedLengthSquared > flatDistanceCheck && _ticksSinceFoundTarget > 240)
        movement = Vector3.Forward;
      else
        movement = Vector3.Zero;

      fistAttack = isTarget && !isOwnerTgt && !HasWeaponOrTool && angleTwoOrLess && distanceSqd <= distanceCheck;

      if (!fistAttack && isTarget && !isOwnerTgt && !HasWeaponOrTool && angleTwoOrLess && Vector3.IsZero(movement) && Vector2.IsZero(ref rotation))
        movement = Vector3.Forward * 0.5f;

      if (JetpackEnabled)
      {
        var vecToTgt = actualPosition - botPosition;
        var relToTarget = Vector3D.TransformNormal(vecToTgt, MatrixD.Transpose(botMatrix));
        var flatToTarget = new Vector3D(relToTarget.X, 0, relToTarget.Z);
        if (flatToTarget.LengthSquared() <= flatDistanceCheck && Math.Abs(relToTarget.Y) > 0.5)
        {
          movement = Vector3.Zero;
          relVectorBot = relToTarget;
        }

         if (Math.Abs(relVectorBot.Y) > 0.05)
           AdjustMovementForFlight(ref relVectorBot, ref movement, ref botPosition);
      }
    }

    void CheckFire(bool shouldFire, bool shouldAttack, ref Vector3 movement)
    {
      var isCrouching = _botState.IsCrouching;
      IsShooting = false;

      if (shouldFire)
      {
        if (isCrouching)
        {
          Character.Crouch();
          Character.CurrentMovementState = MyCharacterMovementEnum.Standing;
        }

        if (_botState.IsRunning)
          Character.SwitchWalk();

        if (HasLineOfSight && ((byte)MySessionComponentSafeZones.AllowedActions & 2) != 0 && FireWeapon())
        {
          IsShooting = true;
          _stuckTimer = 0;
          _ticksSinceFoundTarget = 0;

          //if (!MyAPIGateway.Multiplayer.MultiplayerActive && !isCrouching && _crouchTimer > 10) // crouching is wonky for bots, had to remove it :(
          //{
          //  _crouchTimer = 0;
          //  Character.CurrentMovementState = MyCharacterMovementEnum.Standing;
          //  Character.Crouch();
          //}

          //movement = Vector3.Zero;
        }
        //else if (!MyAPIGateway.Multiplayer.MultiplayerActive && isCrouching && _crouchTimer > 10)
        //{
        //  _crouchTimer = 0;
        //  Character.Crouch();
        //  Character.CurrentMovementState = MyCharacterMovementEnum.Standing;
        //}
      }
      else
      {
        if (!_moveFromLadder)
          _sideNode = null;

        if (isCrouching)
        {
          Character.Crouch();
          Character.CurrentMovementState = MyCharacterMovementEnum.Standing;
        }

        if (shouldAttack)
        {
          _stuckTimer = 0;
          _ticksSinceFoundTarget = 0;
          Attack();
        }
        else
        {
          TrySwitchWalk();
        }
      }
    }
  }
}
