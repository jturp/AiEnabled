using System;

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
      public Vector3D CurrentGoToPosition { get; private set; }
      public Vector3D CurrentActualPosition { get; private set; }
      public bool PositionsValid { get; private set; }

      readonly BotBase _base;
      private Vector3D? _override;

      public Vector3D? Override 
      { 
        get { return _override; }
        private set
        {
          if (value == null || value.IsValid())
            _override = value;
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

        var cube = Entity as IMyCubeBlock;
        if (cube != null)
        {
          var door = cube as IMyDoor;
          if (door != null && !cube.IsFunctional && cube.SlimBlock != null)
          {
            return door.SlimBlock.IsBlockUnbuilt();
          }

          var stator = cube as IMyMotorStator;
          if (stator != null && (stator.MarkedForClose || !stator.Enabled || stator.Top == null))
            return true;

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
        if (ent == null || _base?.Owner?.Character == null || _base.Owner.Character.MarkedForClose)
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

          var relation = MyIDModule.GetRelationPlayerPlayer(_base.Owner.IdentityId, ownerIdentityId);
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

        return Vector3D.DistanceSquared(CurrentActualPosition, _base.GetPosition());
      }

      public void Update()
      {
        Vector3D gotoPos, actualPos;
        PositionsValid = GetTargetPosition(out gotoPos, out actualPos);

        CurrentGoToPosition = gotoPos;
        CurrentActualPosition = actualPos;
      }

      bool GetTargetPosition(out Vector3D gotoPosition, out Vector3D actualPosition)
      {
        gotoPosition = _base.GetPosition();
        actualPosition = gotoPosition;

        if (_base._pathCollection == null)
          return true;

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

        var botMatrix = _base.WorldMatrix;

        var cube = Entity as IMyCubeBlock;
        var isTurretOrRotor = !(_base is RepairBot) && cube != null && !cube.MarkedForClose
          && (AiSession.Instance.AllCoreWeaponDefinitions.Contains(cube.BlockDefinition)
          || cube is IMyMotorStator || cube is IMyLargeTurretBase || cube is IMySmallGatlingGun
          || cube is IMySmallMissileLauncher || cube is IMySmallMissileLauncherReload);

        if (isTurretOrRotor && cube.CubeGrid != null && !cube.CubeGrid.MarkedForClose)
        {
          var turretGrid = cube.CubeGrid;
          actualPosition = cube.WorldAABB.Center + cube.WorldMatrix.Up * (turretGrid.GridSize > 1 ? 1 : 0.5);
          var cubePosition = actualPosition;

          CubeGridMap turretGraph;
          if (turretGrid.GridSize > 1)
          {
            List<IMyCubeGrid> gridList;
            if (!AiSession.Instance.GridGroupListStack.TryPop(out gridList))
              gridList = new List<IMyCubeGrid>();
            else
              gridList.Clear();

            turretGrid.GetGridGroup(GridLinkTypeEnum.Mechanical).GetGrids(gridList);

            MyCubeGrid biggest = cube.CubeGrid as MyCubeGrid;
            foreach (var g in gridList)
            {
              if (g == null || g.GridSize < 1 || g.MarkedForClose)
                continue;

              if (g.WorldAABB.Volume > biggest.PositionComp.WorldAABB.Volume)
                biggest = g as MyCubeGrid;
            }

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

            gridList.Clear();
            AiSession.Instance.GridGroupListStack.Push(gridList);

            if (returnNow)
              return true;
          }

          var upVec = botMatrix.Up;
          var toCenter = cubePosition - turretGrid.WorldAABB.Center;
          var projectUp = VectorUtils.Project(toCenter, upVec);
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
          var center = Inventory.Position;
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

          Dictionary<Vector3I, HashSet<Vector3I>> blockFaces;
          if (AiSession.Instance.BlockFaceDictionary.TryGetValue(Inventory.BlockDefinition.Id, out blockFaces) && blockFaces.Count > 1 && _base?._currentGraph != null)
          {
            var graph = _base._currentGraph;

            for (int i = 1; i < distanceCheck + 1; i++)
            {
              var testPoint = center + Vector3I.Up * i;
              if (!blockFaces.ContainsKey(testPoint) && graph.IsOpenTile(testPoint) && !graph.IsObstacle(testPoint, true))
              {
                center = testPoint;
                break;
              }

              testPoint = center + Vector3I.Down * i;
              if (!blockFaces.ContainsKey(testPoint) && graph.IsOpenTile(testPoint) && !graph.IsObstacle(testPoint, true))
              {
                center = testPoint;
                break;
              }

              testPoint = center + Vector3I.Left * i;
              if (!blockFaces.ContainsKey(testPoint) && graph.IsOpenTile(testPoint) && !graph.IsObstacle(testPoint, true))
              {
                center = testPoint;
                break;
              }

              testPoint = center + Vector3I.Right * i;
              if (!blockFaces.ContainsKey(testPoint) && graph.IsOpenTile(testPoint) && !graph.IsObstacle(testPoint, true))
              {
                center = testPoint;
                break;
              }

              testPoint = center + Vector3I.Forward * i;
              if (!blockFaces.ContainsKey(testPoint) && graph.IsOpenTile(testPoint) && !graph.IsObstacle(testPoint, true))
              {
                center = testPoint;
                break;
              }

              testPoint = center + Vector3I.Backward * i;
              if (!blockFaces.ContainsKey(testPoint) && graph.IsOpenTile(testPoint) && !graph.IsObstacle(testPoint, true))
              {
                center = testPoint;
                break;
              }
            }
          }

          gotoPosition = Inventory.CubeGrid.GridIntegerToWorld(center);
          actualPosition = gotoPosition;
          return true;
        }

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
