﻿using System;

using VRage.Game.ModAPI;
using AiEnabled.Ai.Support;
using VRageMath;
using VRage.Game.Entity;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Game;
using AiEnabled.Bots.Roles.Helpers;
using AiEnabled.Utilities;
using System.Collections.Generic;
using Sandbox.Definitions;
using VRage.Game.Models;

namespace AiEnabled.Bots
{
  public abstract partial class BotBase
  {
    public class TargetInfo
    {
      public IMyPlayer Player { get; private set; }
      public object Entity { get; private set; }
      public IMySlimBlock Inventory { get; private set; }
      public Vector3D CurrentGoToPosition { get; private set; }
      public Vector3D CurrentActualPosition { get; private set; }
      public Vector3D CurrentGravityAtTarget_Nat { get; private set; }
      public Vector3D CurrentGravityAtTarget_Art { get; private set; }

      public bool PositionsValid { get; private set; }

      readonly BotBase _base;
      private Vector3D? _override, _localOverride;

      public Vector3D? Override
      {
        get { return _override; }
        private set
        {
          if (value == null)
          {
            _override = null;
            _localOverride = null;
          }
          else if (value.IsValid())
          {
            _override = value;

            var graph = _base?._currentGraph as CubeGridMap;
            if (graph?.MainGrid == null || graph.MainGrid.IsStatic)
            {
              _localOverride = value;
            }
            else
            {
              _localOverride = Vector3D.TransformNormal(value.Value - graph.MainGrid.PositionComp.WorldAABB.Center, MatrixD.Transpose(graph.MainGrid.WorldMatrix));
            }
          }
        }
      }

      public Action<long, bool> OverrideComplete;
      public Action<long> TargetRemoved;

      public TargetInfo(BotBase b)
      {
        _base = b;
      }

      public void Close()
      {
        OverrideComplete = null;
        TargetRemoved = null;
      }

      public bool HasTarget => Entity != null || Override.HasValue;
      public bool IsSlimBlock => Entity is IMySlimBlock;
      public bool IsCubeBlock => Entity is IMyCubeBlock;
      public bool IsFloater => Entity is IMyFloatingObject;
      public bool IsInventory => Inventory != null;

      public bool IsNewTarget;

      public bool IsDestroyed()
      {
        if (Entity == null)
          return false;

        var damageToDisable = _base.TargetPriorities?.DamageToDisable == true;

        var cube = Entity as IMyCubeBlock;
        if (cube != null)
        {
          if (cube.MarkedForClose || cube.SlimBlock?.IsDestroyed == true)
            return true;

          var door = cube as IMyDoor;
          if (door != null || damageToDisable)
          {
            if (damageToDisable)
            {
              var funcBlock = cube as IMyFunctionalBlock;
              if (funcBlock != null && !funcBlock.IsFunctional)
                return true;
            }

            var isUnbuilt = cube.SlimBlock?.IsBlockUnbuilt() == true;
            if (isUnbuilt)
              return true;
          }

          var cubeCheck = cube.CubeGrid.GetCubeBlock(cube.Position);
          return cubeCheck != cube.SlimBlock;
        }

        var slim = Entity as IMySlimBlock;
        if (slim != null)
        {
          if (slim.IsDestroyed)
            return true;

          if (damageToDisable && slim.FatBlock != null)
          {
            var funcBlock = slim.FatBlock as IMyFunctionalBlock;
            if ((funcBlock != null && !funcBlock.IsFunctional))
              return true;

            if (slim.IsDestroyed)
              return true;
          }

          var slimCheck = slim.CubeGrid.GetCubeBlock(slim.Position);
          return slimCheck != slim;
        }

        var ch = Entity as IMyCharacter;
        if (ch == null)
          return true;

        BotBase b;
        if (AiSession.Instance.Bots.TryGetValue(ch.EntityId, out b))
          return b.IsDead;

        return ch.IsDead || ch.Integrity <= 0;
      }

      public bool IsFriendly()
      {
        if (_base == null || Entity == null)
          return false;

        var ent = Entity as IMyEntity;
        if (ent == null)
        {
          var cube = Entity as IMyCubeBlock;
          if (cube != null)
          {
            ent = cube.SlimBlock.FatBlock as IMyEntity ?? cube.CubeGrid;
          }

          if (ent == null)
          {
            var block = Entity as IMySlimBlock;
            if (block != null)
            {
              ent = block.FatBlock as IMyEntity ?? block.CubeGrid;
            }

            if (ent == null)
            {
              return false;
            }
          }
        }

        var identityId = _base.Owner?.IdentityId ?? _base.BotIdentityId;

        var controllingPlayer = MyAPIGateway.Players.GetPlayerControllingEntity(ent);
        if (controllingPlayer != null)
        {
          if (controllingPlayer.IdentityId == identityId)
            return true;

          var relation = MyIDModule.GetRelationPlayerPlayer(identityId, controllingPlayer.IdentityId, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
          return controllingPlayer.IdentityId == 0 || relation != MyRelationsBetweenPlayers.Enemies;
        }

        var cpit = ent as IMyShipController;
        if (cpit?.Pilot != null)
        {
          var pilot = cpit.Pilot;
          long pilotIdentity = pilot.ControllerInfo?.ControllingIdentityId ?? 0L;

          BotBase bot;
          if (AiSession.Instance.Bots.TryGetValue(pilot.EntityId, out bot))
          {
            pilotIdentity = bot.Owner?.IdentityId ?? bot.BotIdentityId;
          }
          else
          {
            foreach (var kvp in AiSession.Instance.Players)
            {
              var player = kvp.Value;
              if (player?.Character?.EntityId == pilot.EntityId)
              {
                pilotIdentity = player.IdentityId;
              }
            }
          }

          if (pilotIdentity == identityId)
            return true;

          var relation = MyIDModule.GetRelationPlayerPlayer(identityId, pilotIdentity, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
          return pilotIdentity == 0 || relation != MyRelationsBetweenPlayers.Enemies;
        }

        var ch = ent as IMyCharacter;
        if (ch != null)
        {
          if (ch.EntityId == _base.Owner?.Character?.EntityId)
            return true;

          var ownerIdentityId = ch.ControllerInfo.ControllingIdentityId;

          BotBase bot;
          if (AiSession.Instance.Bots.TryGetValue(ch.EntityId, out bot) && bot != null)
          {
            ownerIdentityId = bot.Owner?.IdentityId ?? bot.BotIdentityId;
          }
          else if (ch.IsPlayer && ch.Parent is IMyShipController)
          {
            var p = MyAPIGateway.Players.GetPlayerControllingEntity(ch.Parent);
            if (p != null)
              ownerIdentityId = p.IdentityId;
          }

          var relation = MyIDModule.GetRelationPlayerPlayer(identityId, ownerIdentityId, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
          return ownerIdentityId == 0 || relation != MyRelationsBetweenPlayers.Enemies;
        }

        var grid = ent?.GetTopMostParent() as IMyCubeGrid;
        if (grid?.BigOwners?.Count > 0 || grid?.SmallOwners?.Count > 0)
        {
          var owner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners[0];
          var relation = MyIDModule.GetRelationPlayerBlock(owner, identityId, MyOwnershipShareModeEnum.None,
            MyRelationsBetweenPlayerAndBlock.Neutral, MyRelationsBetweenFactions.Neutral,
            MyRelationsBetweenPlayerAndBlock.Neutral, MyRelationsBetweenPlayerAndBlock.Neutral);
          return owner == 0 || relation != MyRelationsBetweenPlayerAndBlock.Enemies;
        }

        return false;
      }

      public bool IsOnLadder(out MyCubeBlock ladder)
      {
        ladder = null;
        var charTgt = Entity as IMyCharacter;
        if (!_base._currentGraph.IsGridGraph || charTgt == null)
          return false;

        var gridGraph = _base._currentGraph as CubeGridMap;
        var localPos = gridGraph.WorldToLocal(charTgt.WorldAABB.Center);

        MyCube cube;
        if (gridGraph.MainGrid.TryGetCube(localPos, out cube) && cube?.CubeBlock != null)
        {
          var slim = cube.CubeBlock as IMySlimBlock;
          ladder = slim.FatBlock as MyCubeBlock;
          return AiSession.Instance.LadderBlockDefinitions.Contains(slim.BlockDefinition.Id);
        }

        return false;
      }

      public void SetTarget(IMyPlayer player, IMyEntity topMostParent)
      {
        Player = player;
        Entity = topMostParent;
        IsNewTarget = Entity != null;
        Inventory = null;
        _base.ResetSideNode();
      }

      public void SetTarget(IMyEntity entity)
      {
        Player = MyAPIGateway.Players.GetPlayerControllingEntity(entity);
        Entity = entity;
        IsNewTarget = Entity != null;
        Inventory = null;
        _base.ResetSideNode();
      }

      public void SetTarget(IMyPlayer owner, object target, bool isInventory = false)
      {
        Player = owner;
        Entity = target;
        IsNewTarget = Entity != null;
        _base.ResetSideNode();

        if (Player == null && target is IMyEntity)
          Player = MyAPIGateway.Players.GetPlayerControllingEntity((IMyEntity)target);

        if (!isInventory)
          Inventory = null;
      }

      public void SetInventory(IMySlimBlock inv)
      {
        Inventory = inv;
        IsNewTarget = Inventory != null;
        _base.ResetSideNode();
      }

      public void SetOverride(Vector3D goTo)
      {
        Vector3D position;
        var botMatrix = _base.WorldMatrix;

        var graph = _base._currentGraph;
        if (graph.IsGridGraph)
        {
          var gridGraph = graph as CubeGridMap;
          var localPos = gridGraph.MainGrid.WorldToGridInteger(goTo);

          Node node;
          float _;
          if (gridGraph.OpenTileDict.TryGetValue(localPos, out node))
          {
            var cube = node.Block;
            if (cube?.FatBlock is IMyCockpit)
            {
              position = gridGraph.MainGrid.GridIntegerToWorld(localPos);
            }
            else if (gridGraph.GetClosestValidNode(_base, localPos, out localPos, botMatrix.Up))
            {
              position = gridGraph.MainGrid.GridIntegerToWorld(localPos);
            }
            else if (gridGraph.RootVoxel != null && MyAPIGateway.Physics.CalculateNaturalGravityAt(goTo, out _).LengthSquared() > 0)
            {
              var surfacePoint = gridGraph.GetClosestSurfacePointFast(_base, goTo, botMatrix.Up);
              if (surfacePoint.HasValue)
              {
                localPos = gridGraph.MainGrid.WorldToGridInteger(surfacePoint.Value);
                if (gridGraph.GetClosestValidNode(_base, localPos, out localPos))
                {
                  position = gridGraph.MainGrid.GridIntegerToWorld(localPos);
                }
                else
                {
                  position = surfacePoint.Value;
                }
              }
              else
                position = goTo;
            }
            else
            {
              position = goTo;
            }
          }
          else
          {
            position = goTo;
          }
        }
        else
        {
          var localPos = graph.WorldToLocal(goTo);

          Vector3I node;
          if (graph.GetClosestValidNode(_base, localPos, out node))
          {
            position = graph.LocalToWorld(node);
          }
          else
          {
            var surfacePoint = graph.GetClosestSurfacePointFast(_base, goTo, botMatrix.Up);
            if (surfacePoint.HasValue)
            {
              localPos = graph.WorldToLocal(surfacePoint.Value);
              if (graph.GetClosestValidNode(_base, localPos, out localPos))
              {
                position = graph.LocalToWorld(localPos);
              }
              else
              {
                position = surfacePoint.Value;
              }
            }
            else
              position = goTo;
          }
        }

        Override = position;
      }

      public void RemoveTarget()
      {
        Player = null;
        Entity = null;
        Inventory = null;
        IsNewTarget = false;
        _base.ResetSideNode();

        if (_base?.UseAPITargets == true && _base.Character?.EntityId > 0)
          TargetRemoved?.Invoke(_base.Character.EntityId);
      }

      public void RemoveInventory()
      {
        Inventory = null;
        _base.ResetSideNode();
      }

      public void RemoveOverride(bool arrived)
      {
        Override = null;

        if (_base?.Character?.EntityId > 0)
          OverrideComplete?.Invoke(_base.Character.EntityId, arrived);
      }

      public double GetDistanceSquared()
      {
        if (!PositionsValid)
          return double.MaxValue;

        return Vector3D.DistanceSquared(CurrentActualPosition, _base.BotInfo.CurrentBotPositionActual);
      }

      public void Update()
      {
        Vector3D gotoPos, actualPos;
        PositionsValid = GetTargetPosition(out gotoPos, out actualPos);

        CurrentGoToPosition = gotoPos;
        CurrentActualPosition = actualPos;
      }

      void AssignGravityAtTarget(ref Vector3D targetPosition)
      {
        float interference;
        CurrentGravityAtTarget_Nat = MyAPIGateway.Physics.CalculateNaturalGravityAt(targetPosition, out interference);
        CurrentGravityAtTarget_Art = MyAPIGateway.Physics.CalculateArtificialGravityAt(targetPosition, interference);
      }

      bool GetTargetPosition(out Vector3D gotoPosition, out Vector3D actualPosition)
      {
        gotoPosition = _base.BotInfo.CurrentBotPositionActual;
        actualPosition = gotoPosition;

        if (_base._pathCollection == null)
          return true;

        if (Entity == null && !Override.HasValue)
        {
          return false;
        }

        if (Override.HasValue)
        {
          var graph = _base._currentGraph as CubeGridMap;
          if (graph?.MainGrid != null && !graph.MainGrid.IsStatic && _localOverride.HasValue)
          {
            Override = graph.MainGrid.PositionComp.WorldAABB.Center + Vector3D.TransformNormal(_localOverride.Value, graph.MainGrid.WorldMatrix);
          }

          gotoPosition = Override.Value;
          actualPosition = gotoPosition;
          AssignGravityAtTarget(ref actualPosition);
          return true;
        }

        var floater = Entity as IMyFloatingObject;
        if (floater != null)
        {
          actualPosition = floater.GetPosition();
          AssignGravityAtTarget(ref actualPosition);

          var grav = CurrentGravityAtTarget_Nat.LengthSquared() > 0 ? CurrentGravityAtTarget_Nat : CurrentGravityAtTarget_Art;

          if (grav.LengthSquared() > 0)
          {
            gotoPosition = actualPosition - Vector3D.Normalize(grav);
          }
          else if (_base._currentGraph?.Ready == true)
          {
            var graphLocal = _base._currentGraph.WorldToLocal(actualPosition);
            if (!_base._currentGraph.IsOpenTile(graphLocal))
            {
              graphLocal += Vector3I.Up;
            }

            gotoPosition = _base._currentGraph.LocalToWorld(graphLocal);
          }

          return true;
        }

        var botMatrix = _base.WorldMatrix;

        var cube = IsSlimBlock ? ((IMySlimBlock)Entity).FatBlock : Entity as IMyCubeBlock;
        bool isTurret = false;
        bool checkWeapon = false;
        if (!(_base is RepairBot) && cube != null && !cube.MarkedForClose)
        {
          isTurret = cube is IMyLargeTurretBase;
          var isRotor = cube is IMyMotorStator;
          var isWeapon = AiSession.Instance.AllCoreWeaponDefinitions.Contains(cube.BlockDefinition)
            || cube is IMySmallGatlingGun || cube is IMySmallMissileLauncher || cube is IMySmallMissileLauncherReload;

          checkWeapon = isTurret || isRotor || isWeapon;
        }

        if (checkWeapon && cube.CubeGrid != null && !cube.CubeGrid.MarkedForClose)
        {
          var turretGrid = cube.CubeGrid;
          bool validWeapon = _base.HasWeaponOrTool && _base.ToolDefinition != null && _base.ToolDefinition.WeaponType != MyItemWeaponType.None;

          if (validWeapon)
          {
            actualPosition = cube.WorldAABB.Center + cube.WorldMatrix.Up * (turretGrid.GridSize > 1 ? 1 : 0.5);
            AssignGravityAtTarget(ref actualPosition);
          }
          else
          {
            var pos = cube.Position;

            if (isTurret && cube.CubeGrid.GridSizeEnum == MyCubeSize.Large)
            {
              var downVec = cube.WorldMatrix.Down;
              var downDir = cube.CubeGrid.WorldMatrix.GetClosestDirection(downVec);
              var intVec = Base6Directions.GetIntVector(downDir);
              pos += intVec;
            }

            actualPosition = turretGrid.GridIntegerToWorld(pos);
            gotoPosition = actualPosition;
            AssignGravityAtTarget(ref actualPosition);
            return true;
          }

          var cubePosition = actualPosition;

          CubeGridMap turretGraph;
          if (turretGrid.GridSize > 1)
          {
            MyCubeGrid biggest = GridBase.GetLargestGridForMap(turretGrid) as MyCubeGrid;

            bool returnNow = false;
            if (AiSession.Instance.GridGraphDict.TryGetValue(biggest.EntityId, out turretGraph))
            {
              Vector3I node;
              if (turretGraph.GetRandomNodeNearby(_base, actualPosition, out node))
              {
                gotoPosition = turretGraph.LocalToWorld(node);
                returnNow = true;
              }
            }

            if (returnNow)
              return true;
          }

          var upVec = botMatrix.Up;
          var toCenter = cubePosition - turretGrid.WorldAABB.Center;
          var projectUp = AiUtils.Project(toCenter, upVec);
          var reject = toCenter - projectUp;

          if (Vector3D.IsZero(reject))
            reject = botMatrix.Left;
          else
            reject.Normalize();

          cubePosition += reject * Math.Max(turretGrid.WorldAABB.HalfExtents.AbsMax(), 20);

          var graph = _base._currentGraph;
          var currentPosition = graph?.GetClosestSurfacePointFast(_base, cubePosition, upVec);
          if (currentPosition.HasValue)
          {
            var currentValue = currentPosition.Value;
            var orientation = Quaternion.CreateFromRotationMatrix(turretGrid.WorldMatrix);
            var obb = new MyOrientedBoundingBoxD(turretGrid.WorldAABB.Center, turretGrid.LocalAABB.HalfExtents + 2, orientation);

            while (!obb.Contains(ref currentValue))
            {
              var node = graph.WorldToLocal(currentValue);

              if (!graph.ObstacleNodes.ContainsKey(node))
              {
                break;
              }

              currentValue -= reject * graph.CellSize;
            }

            currentPosition = graph.GetClosestSurfacePointFast(_base, currentValue, upVec);
            if (currentPosition.HasValue)
              gotoPosition = currentPosition.Value + upVec;
            else
              gotoPosition = currentValue;
          }
          else
            gotoPosition = actualPosition;

          return true;
        }

        if (Inventory != null)
        {
          Vector3I center;

          if (_base?._currentGraph != null)
          {
            Vector3D centerWorld;
            if (Inventory.FatBlock != null)
              centerWorld = Inventory.FatBlock.WorldAABB.Center;
            else
              Inventory.ComputeWorldCenter(out centerWorld);

            center = _base._currentGraph.WorldToLocal(centerWorld);
          }
          else
          {
            center = Inventory.Position;
          }

          if (_base?._currentGraph != null)
          {
            int distanceCheck;
            if (Inventory.FatBlock != null)
            {
              distanceCheck = (int)Math.Ceiling((Inventory.FatBlock.PositionComp.LocalAABB.HalfExtents.AbsMax() + 1f) / 2.5);
            }
            else
            {
              BoundingBoxD box;
              Inventory.GetWorldBoundingBox(out box, true);
              distanceCheck = (int)Math.Ceiling((box.HalfExtents.AbsMax() + 1f) / 2.5);
            }

            var graph = _base._currentGraph;
            var invGrid = Inventory.CubeGrid;
            var connector = Inventory.FatBlock as IMyShipConnector;
            var m = new MatrixI(Inventory.Orientation);

            for (int i = 1; i < distanceCheck + 1; i++)
            {
              if (connector == null || !connector.IsConnected)
              {
                var testPointUp = center + m.UpVector * i;
                var testCubeUP = invGrid.GetCubeBlock(testPointUp);

                if (testCubeUP != Inventory && (testCubeUP == null || !AiSession.Instance.SlopeBlockDefinitions.Contains(testCubeUP.BlockDefinition.Id))
                  && graph.IsOpenTile(testPointUp) && !graph.IsObstacle(testPointUp, _base, true))
                {
                  center = testPointUp;
                  break;
                }
              }

              if (connector == null)
              {
                var testPointDwn = center + m.DownVector * i;
                var testCubeDwn = invGrid.GetCubeBlock(testPointDwn);
                if (testCubeDwn != Inventory && (testCubeDwn == null || !AiSession.Instance.SlopeBlockDefinitions.Contains(testCubeDwn.BlockDefinition.Id))
                  && graph.IsOpenTile(testPointDwn) && !graph.IsObstacle(testPointDwn, _base, true))
                {
                  center = testPointDwn;
                  break;
                }
              }

              var testPoint = center + m.LeftVector * i;
              var testCube = invGrid.GetCubeBlock(testPoint);
              if (testCube != Inventory && (testCube == null || !AiSession.Instance.SlopeBlockDefinitions.Contains(testCube.BlockDefinition.Id))
                && graph.IsOpenTile(testPoint) && !graph.IsObstacle(testPoint, _base, true))
              {
                center = testPoint;
                break;
              }

              testPoint = center + m.RightVector * i;
              testCube = invGrid.GetCubeBlock(testPoint);
              if (testCube != Inventory && (testCube == null || !AiSession.Instance.SlopeBlockDefinitions.Contains(testCube.BlockDefinition.Id))
                && graph.IsOpenTile(testPoint) && !graph.IsObstacle(testPoint, _base, true))
              {
                center = testPoint;
                break;
              }

              testPoint = center + m.ForwardVector * i;
              testCube = invGrid.GetCubeBlock(testPoint);
              if (testCube != Inventory && (testCube == null || !AiSession.Instance.SlopeBlockDefinitions.Contains(testCube.BlockDefinition.Id))
                && graph.IsOpenTile(testPoint) && !graph.IsObstacle(testPoint, _base, true))
              {
                center = testPoint;
                break;
              }

              testPoint = center + m.BackwardVector * i;
              testCube = invGrid.GetCubeBlock(testPoint);
              if (testCube != Inventory && (testCube == null || !AiSession.Instance.SlopeBlockDefinitions.Contains(testCube.BlockDefinition.Id))
                && graph.IsOpenTile(testPoint) && !graph.IsObstacle(testPoint, _base, true))
              {
                center = testPoint;
                break;
              }
            }
          }

          gotoPosition = Inventory.CubeGrid.GridIntegerToWorld(center);
          actualPosition = gotoPosition;
          AssignGravityAtTarget(ref actualPosition);

          return true;
        }

        if (cube != null)
        {
          var center = cube.WorldAABB.Center;

          if (cube is IMyDoor)
          {
            if (cube is IMyAirtightHangarDoor)
              center += cube.WorldMatrix.Down * cube.CubeGrid.GridSize;
            else if (cube.BlockDefinition.SubtypeName == "LargeBlockGate")
              center += cube.WorldMatrix.Down * cube.CubeGrid.GridSize * 0.5;
            else if (cube.BlockDefinition.SubtypeName == "LargeBlockSlideDoor")
              center += cube.WorldMatrix.Forward * cube.CubeGrid.GridSize * 0.5;
          }

          gotoPosition = center;
          actualPosition = center;
          AssignGravityAtTarget(ref actualPosition);

          if (_base is RepairBot && cube.CubeGrid.GridSizeEnum == MyCubeSize.Small)
          {
            var grav = CurrentGravityAtTarget_Nat.LengthSquared() > 0 ? CurrentGravityAtTarget_Nat : CurrentGravityAtTarget_Art;

            if (grav.LengthSquared() > 0)
              gotoPosition -= Vector3D.Normalize(grav);
            else
              gotoPosition += _base._currentGraph?.WorldMatrix.Up ?? _base.WorldMatrix.Up;
          }

          return true;
        }

        var slim = Entity as IMySlimBlock;
        if (slim != null)
        {
          gotoPosition = slim.CubeGrid.GridIntegerToWorld(slim.Position);
          actualPosition = gotoPosition;
          AssignGravityAtTarget(ref actualPosition);

          if (_base is RepairBot && slim.CubeGrid.GridSizeEnum == MyCubeSize.Small)
          {
            var grav = CurrentGravityAtTarget_Nat.LengthSquared() > 0 ? CurrentGravityAtTarget_Nat : CurrentGravityAtTarget_Art;

            if (grav.LengthSquared() > 0)
              gotoPosition -= Vector3D.Normalize(grav);
            else
              gotoPosition += _base._currentGraph?.WorldMatrix.Up ?? _base.WorldMatrix.Up;
          }

          return true;
        }

        var charTgt = Entity as IMyCharacter;
        if (charTgt != null && charTgt.EntityId == _base.Owner?.Character?.EntityId)
        {
          var ownerPos = charTgt.WorldAABB.Center;
          var ownerMatrix = charTgt.WorldMatrix;
          actualPosition = ownerPos;

          bool foundPos = false;
          var cellSize = _base._currentGraph.CellSize;

          List<MyLineSegmentOverlapResult<MyEntity>> overlapList = AiSession.Instance.OverlapResultListPool.Get();
          List<Vector3I> cellList = AiSession.Instance.LineListPool.Get();

          var followDistance = AiSession.Instance.PlayerFollowDistanceDict.GetValueOrDefault(_base.Owner.IdentityId, 7.5f);
          int numSpaces = (int)Math.Round(followDistance / _base._currentGraph.CellSize);

          for (int i = numSpaces; i > 0; i--)
          {
            var testPoint = ownerPos + (ownerMatrix.Backward * i + ownerMatrix.Left * i) * cellSize;
            if (_base._currentGraph.IsPositionUsable(_base, testPoint) && HasLineOfSight(ownerPos, testPoint, overlapList, cellList))
            {
              foundPos = true;
              gotoPosition = testPoint;
              break;
            }

            testPoint = ownerPos + (ownerMatrix.Backward * i + ownerMatrix.Right * i) * cellSize;
            if (_base._currentGraph.IsPositionUsable(_base, testPoint) && HasLineOfSight(ownerPos, testPoint, overlapList, cellList))
            {
              foundPos = true;
              gotoPosition = testPoint;
              break;
            }

            testPoint = ownerPos + ownerMatrix.Left * i * cellSize;
            if (_base._currentGraph.IsPositionUsable(_base, testPoint) && HasLineOfSight(ownerPos, testPoint, overlapList, cellList))
            {
              foundPos = true;
              gotoPosition = testPoint;
              break;
            }

            testPoint = ownerPos + ownerMatrix.Right * i * cellSize;
            if (_base._currentGraph.IsPositionUsable(_base, testPoint) && HasLineOfSight(ownerPos, testPoint, overlapList, cellList))
            {
              foundPos = true;
              gotoPosition = testPoint;
              break;
            }

            testPoint = ownerPos + ownerMatrix.Backward * i * cellSize;
            if (_base._currentGraph.IsPositionUsable(_base, testPoint) && HasLineOfSight(ownerPos, testPoint, overlapList, cellList))
            {
              foundPos = true;
              gotoPosition = testPoint;
              break;
            }
          }

          if (!foundPos)
          {
            for (int i = numSpaces; i > 0; i--)
            {
              var testPoint = ownerPos + (ownerMatrix.Forward * i + ownerMatrix.Left * i) * cellSize;
              if (_base._currentGraph.IsPositionUsable(_base, testPoint) && HasLineOfSight(ownerPos, testPoint, overlapList, cellList))
              {
                foundPos = true;
                gotoPosition = testPoint;
                break;
              }

              testPoint = ownerPos + (ownerMatrix.Forward * i + ownerMatrix.Right * i) * cellSize;
              if (_base._currentGraph.IsPositionUsable(_base, testPoint) && HasLineOfSight(ownerPos, testPoint, overlapList, cellList))
              {
                foundPos = true;
                gotoPosition = testPoint;
                break;
              }
            }

            if (!foundPos)
              gotoPosition = actualPosition;
          }

          AiSession.Instance.OverlapResultListPool?.Return(ref overlapList);
          AiSession.Instance.LineListPool?.Return(ref cellList);
          return true;
        }

        var ent = Entity as IMyEntity;
        if (ent != null)
        {
          var seat = Player?.Character?.Parent as IMyCockpit;
          if (seat != null && _base.Owner != null && _base._currentGraph?.RootVoxel != null && seat.CubeGrid.GridSize < 1 && Player.IdentityId == _base.Owner.IdentityId)
          {
            var gridCenter = seat.CubeGrid.WorldAABB.Center;
            var surfacePoint = _base._currentGraph.GetClosestSurfacePointFast(_base, gridCenter, botMatrix.Up);
            if (surfacePoint.HasValue && Vector3D.DistanceSquared(gridCenter, surfacePoint.Value) < 100)
            {
              var halfExt = seat.CubeGrid.LocalAABB.HalfExtents.AbsMax() * 1.5;
              var followPosition = gridCenter + seat.WorldMatrix.Backward * halfExt + seat.WorldMatrix.Left * halfExt;

              var followSurface = _base._currentGraph.GetClosestSurfacePointFast(_base, followPosition, botMatrix.Up);
              if (followSurface.HasValue)
                gotoPosition = followSurface.Value;
              else
                gotoPosition = followPosition;

              actualPosition = gotoPosition;
            }
            else
            {
              gotoPosition = ent.WorldAABB.Center;
              actualPosition = gotoPosition;
            }
          }
          else
          {
            gotoPosition = ent.WorldAABB.Center;
            actualPosition = gotoPosition;
          }

          AssignGravityAtTarget(ref actualPosition);
          return true;
        }

        if (Player?.Character != null)
        {
          gotoPosition = Player.Character.WorldAABB.Center;
          actualPosition = gotoPosition;
          AssignGravityAtTarget(ref actualPosition);
          return true;
        }

        return false;
      }

      bool HasLineOfSight(Vector3D aimPoint, Vector3D testPoint, List<MyLineSegmentOverlapResult<MyEntity>> overlapList, List<Vector3I> cellList)
      {
        var voxel = _base._currentGraph?.RootVoxel;
        if (GridBase.PointInsideVoxel(testPoint, voxel))
          return false;

        var line = new LineD(testPoint, aimPoint);

        if (voxel != null)
        {
          MyIntersectionResultLineTriangleEx? hit;
          voxel.GetIntersectionWithLine(ref line, out hit);
          if (hit.HasValue)
            return false;
        }

        overlapList.Clear();
        MyGamePruningStructure.GetAllEntitiesInRay(ref line, overlapList);

        for (int i = 0; i < overlapList.Count; i++)
        {
          var result = overlapList[i];
          var grid = result.Element as MyCubeGrid;
          if (grid?.Physics != null && !grid.IsPreview)
          {
            cellList.Clear();
            grid.RayCastCells(testPoint, aimPoint, cellList);

            foreach (var cell in cellList)
            {
              var cube = grid.GetCubeBlock(cell) as IMySlimBlock;
              var cubeDef = cube?.BlockDefinition as MyCubeBlockDefinition;
              if (cubeDef?.IsAirTight == true)
                return false;
            }
          }
        }

        return true;
      }
    }
  }
}