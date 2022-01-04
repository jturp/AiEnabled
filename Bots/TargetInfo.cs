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

namespace AiEnabled.Bots
{
  public abstract partial class BotBase
  {
    public class TargetInfo
    {
      public IMyPlayer Player { get; private set; }
      public object Entity { get; private set; }
      public IMySlimBlock Inventory { get; private set; }
      readonly BotBase _base;

      public Vector3D? Override 
      { get { return _override; }
        private set
        {
          if (value == null || value.IsValid())
            _override = value;
        }
      }

      public Action<long, bool> OverrideComplete;
      public Action<long> TargetRemoved;

      List<MyEntity> _entities = new List<MyEntity>();
      List<IMyCubeGrid> _gridList = new List<IMyCubeGrid>();
      Vector3D? _override;

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

        var cube = Entity as IMyCubeBlock;
        if (cube != null)
        {
          if (cube is IMyDoor && !cube.IsFunctional && cube.SlimBlock != null)
          {
            var blockDef = (MyCubeBlockDefinition)cube.SlimBlock.BlockDefinition;
            return cube.SlimBlock.BuildLevelRatio < blockDef.CriticalIntegrityRatio;
          }

          return cube?.SlimBlock?.IsDestroyed ?? true;
        }

        var slim = Entity as IMySlimBlock;
        if (slim != null)
          return slim.IsDestroyed;

        var ch = Entity as IMyCharacter;
        if (ch == null)
          return false;

        BotBase b;
        if (AiSession.Instance.Bots.TryGetValue(ch.EntityId, out b))
          return b.IsDead;

        return ch.IsDead || ch.Integrity <= 0;
      }

      public bool IsFriendly()
      {
        var ent = Entity as IMyEntity;
        if (ent == null || _base?.Owner == null)
          return false;

        var controllingPlayer = MyAPIGateway.Players.GetPlayerControllingEntity(ent);
        if (controllingPlayer != null)
        {
          if (controllingPlayer.IdentityId == _base.Owner.IdentityId)
            return true;

          var relation = MyIDModule.GetRelationPlayerPlayer(_base.Owner.IdentityId, controllingPlayer.IdentityId);
          return relation != MyRelationsBetweenPlayers.Enemies;
        }

        var ch = ent as IMyCharacter;
        if (ch != null)
        {
          if (ch.EntityId == _base.Owner.Character.EntityId)
            return true;

          var relation = MyIDModule.GetRelationPlayerPlayer(_base.Owner.IdentityId, ch.ControllerInfo.ControllingIdentityId);
          return relation != MyRelationsBetweenPlayers.Enemies;
        }

        var grid = ent as IMyCubeGrid;
        if (grid != null)
        {
          if (grid.BigOwners?.Count > 0 || grid.SmallOwners?.Count > 0)
          {
            var owner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners[0];
            var relation = MyIDModule.GetRelationPlayerBlock(owner, _base.Owner.IdentityId);
            return relation != MyRelationsBetweenPlayerAndBlock.Enemies;
          }
        }

        return false;
      }

      public bool OnLadder(out MyCubeBlock ladder)
      {
        ladder = null;
        if (!_base._currentGraph.IsGridGraph || !(Entity is IMyCharacter))
          return false;

        var gridGraph = _base._currentGraph as CubeGridMap;

        Vector3D gotoPos, actualPos;
        if (!GetTargetPosition(out gotoPos, out actualPos))
          return false;

        var localPos = gridGraph.WorldToLocal(actualPos);

        MyCube cube;
        if (gridGraph.Grid.TryGetCube(localPos, out cube) && cube?.CubeBlock != null)
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
        var graph = _base._currentGraph;
        if (graph.IsGridGraph)
        {
          var gridGraph = graph as CubeGridMap;
          var localPos = gridGraph.Grid.WorldToGridInteger(goTo);

          Node node;
          float _;
          if (gridGraph.OpenTileDict.TryGetValue(localPos, out node))
          {
            var cube = node.Block;
            if (cube?.FatBlock is IMyCockpit)
            {
              position = gridGraph.Grid.GridIntegerToWorld(localPos);
            }
            else if (gridGraph.GetClosestValidNode(_base, localPos, out localPos, _base.WorldMatrix.Up))
            {
              position = gridGraph.Grid.GridIntegerToWorld(localPos);
            }
            else if (gridGraph.Planet != null && MyAPIGateway.Physics.CalculateNaturalGravityAt(goTo, out _).LengthSquared() > 0)
            {
              var surfacePoint = gridGraph.GetClosestSurfacePointFast(_base, goTo, _base.WorldMatrix.Up);
              localPos = gridGraph.Grid.WorldToGridInteger(surfacePoint);
              if (gridGraph.GetClosestValidNode(_base, localPos, out localPos))
              {
                position = gridGraph.Grid.GridIntegerToWorld(localPos);
              }
              else
              {
                position = surfacePoint;
              }
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
            var surfacePoint = graph.GetClosestSurfacePointFast(_base, goTo, _base.WorldMatrix.Up);
            localPos = graph.WorldToLocal(surfacePoint);
            if (graph.GetClosestValidNode(_base, localPos, out localPos))
            {
              position = graph.LocalToWorld(localPos);
            }
            else
            {
              position = surfacePoint;
            }
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
        Vector3D gotoPos, actualPos;
        if (!GetTargetPosition(out gotoPos, out actualPos))
          return double.MaxValue;

        return Vector3D.DistanceSquared(actualPos, _base.Position);
      }

      public bool GetTargetPosition(out Vector3D gotoPosition, out Vector3D actualPosition)
      {
        gotoPosition = _base.Position;
        actualPosition = gotoPosition;

        if (Entity == null && !Override.HasValue)
        {
          return false;
        }

        if (Override.HasValue)
        {
          gotoPosition = Override.Value;
          actualPosition = gotoPosition;
          return true;
        }

        var turret = Entity as IMyLargeTurretBase;
        if (turret != null && turret.CubeGrid != null && !turret.CubeGrid.MarkedForClose && !(_base is RepairBot))
        {
          var turretGrid = turret.CubeGrid;
          actualPosition = turret.WorldAABB.Center + turret.WorldMatrix.Up * (turretGrid.GridSize > 1 ? 1 : 0.5);
          var cubePosition = actualPosition;

          CubeGridMap turretGraph;
          if (turretGrid.GridSize > 1)
          {
            _gridList.Clear();
            MyAPIGateway.GridGroups.GetGroup(turretGrid, GridLinkTypeEnum.Logical, _gridList);

            MyCubeGrid biggest = turret.CubeGrid as MyCubeGrid;
            foreach (var g in _gridList)
            {
              if (g == null || g.GridSize < 1 || g.MarkedForClose)
                continue;

              if (g.WorldAABB.Volume > biggest.PositionComp.WorldAABB.Volume)
                biggest = g as MyCubeGrid;
            }

            if (AiSession.Instance.GridGraphDict.TryGetValue(biggest.EntityId, out turretGraph))
            {
              Vector3I node;
              if (turretGraph.GetRandomNodeNearby(_base, actualPosition, out node))
              {
                gotoPosition = turretGraph.LocalToWorld(node);
                return true;
              }
            }
          }

          var upVec = _base.WorldMatrix.Up;
          var toCenter = cubePosition - turretGrid.WorldAABB.Center;
          var projectUp = VectorUtils.Project(toCenter, upVec);
          var reject = toCenter - projectUp;

          if (Vector3D.IsZero(reject))
            reject = _base.WorldMatrix.Left;
          else
            reject.Normalize();

          cubePosition += reject * Math.Max(turretGrid.WorldAABB.HalfExtents.AbsMax(), 20);

          var graph = _base._currentGraph;
          if (graph != null)
          {
            var currentPosition = graph.GetClosestSurfacePointFast(_base, cubePosition, upVec);

            var orientation = Quaternion.CreateFromRotationMatrix(turretGrid.WorldMatrix);
            var obb = new MyOrientedBoundingBoxD(turretGrid.WorldAABB.Center, turretGrid.LocalAABB.HalfExtents + 2, orientation);

            while (!obb.Contains(ref currentPosition))
            {
              var node = graph.WorldToLocal(currentPosition);

              _entities.Clear();
              if (!graph.ObstacleNodes.ContainsKey(node))
              {
                break;
              }

              currentPosition -= reject * graph.CellSize;
            }

            gotoPosition = graph.GetClosestSurfacePointFast(_base, currentPosition, upVec) + upVec;
          }
          else
            gotoPosition = actualPosition;

          return true;
        }

        if (Inventory != null)
        {
          gotoPosition = Inventory.CubeGrid.GridIntegerToWorld(Inventory.Position);
          actualPosition = gotoPosition;
          return true;
        }

        var cube = Entity as IMyCubeBlock;
        if (cube != null)
        {
          var center = cube.WorldAABB.Center;

          if (cube is IMyAirtightHangarDoor)
            center += cube.WorldMatrix.Down * cube.CubeGrid.GridSize;
          else if (cube.BlockDefinition.SubtypeName == "LargeBlockGate")
            center += cube.WorldMatrix.Down * cube.CubeGrid.GridSize * 0.5;

          gotoPosition = center;
          actualPosition = center;
          return true;
        }

        var slim = Entity as IMySlimBlock;
        if (slim != null)
        {
          gotoPosition = slim.CubeGrid.GridIntegerToWorld(slim.Position);
          actualPosition = gotoPosition;
          return true;
        }

        var ent = Entity as IMyEntity;
        if (ent != null)
        {
          var seat = Player?.Character?.Parent as IMyCockpit;
          if (seat != null && _base.Owner != null && _base._currentGraph?.Planet != null && seat.CubeGrid.GridSize < 1 && Player.IdentityId == _base.Owner.IdentityId)
          {
            var gridCenter = seat.CubeGrid.WorldAABB.Center;
            var surfacePoint = _base._currentGraph.GetClosestSurfacePointFast(_base, gridCenter, _base.WorldMatrix.Up);
            if (Vector3D.DistanceSquared(gridCenter, surfacePoint) < 100)
            {
              var halfExt = seat.CubeGrid.LocalAABB.HalfExtents.AbsMax() * 1.5;
              var followPosition = gridCenter + seat.WorldMatrix.Backward * halfExt + seat.WorldMatrix.Left * halfExt;
              gotoPosition = _base._currentGraph.GetClosestSurfacePointFast(_base, followPosition, _base.WorldMatrix.Up);
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

          return true;
        }

        if (Player?.Character != null)
        {
          gotoPosition = Player.Character.WorldAABB.Center;
          actualPosition = gotoPosition;
          return true;
        }

        return false;
      }
    }
  }
}