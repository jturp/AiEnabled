using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AiEnabled.API;
using AiEnabled.Bots;
using AiEnabled.Parallel;
using AiEnabled.Utilities;

using ParallelTasks;

using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.WorldEnvironment;
using Sandbox.ModAPI;

using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRage.Voxels;

using VRageMath;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace AiEnabled.Ai.Support
{
  public class VoxelGridMap : GridBase
  {
    /// <summary>
    /// The default half size for a voxel grid map
    /// </summary>
    public const byte DefaultHalfSize = 25;

    /// <summary>
    /// The default cell size for a voxel grid map
    /// </summary>
    public const float DefaultCellSize = 1.25f;

    Dictionary<Vector3I, Node> _openTileDict = new Dictionary<Vector3I, Node>(Vector3I.Comparer);

    public MatrixD MatrixNormalizedInv;
    Vector3D _upVector;
    readonly float _gridSizeR;
    ObstacleWorkData _tempObstaclesWorkData;
    List<VoxelUpdateItem> _voxelUpdatesNeeded;
    MyQueue<VoxelUpdateItem> _voxelUpdatesQueue;
    ParallelTasks.Task _obstacleTask, _updateTask;
    FastResourceLock _pendingLockObject = new FastResourceLock();

    public override float CellSize { get; internal set; } = DefaultCellSize;

    public VoxelGridMap(Vector3D botStart, MatrixD botMatrix)
    {
      _upVector = Vector3D.Up;
      Vector3D fwd = Vector3D.Forward;
      IsGridGraph = false;

      var vec = new Vector3I(DefaultHalfSize);
      BoundingBox = new BoundingBoxI(-vec, vec);
      MyVoxelBase checkVoxel = null;

      if (!AiSession.Instance.ObstacleWorkDataStack.TryPop(out _tempObstaclesWorkData) || _tempObstaclesWorkData == null)
        _tempObstaclesWorkData = new ObstacleWorkData();

      if (!AiSession.Instance.VoxelUpdateListStack.TryPop(out _voxelUpdatesNeeded) || _voxelUpdatesNeeded == null)
        _voxelUpdatesNeeded = new List<VoxelUpdateItem>(10);
      else
        _voxelUpdatesNeeded.Clear();

      if (!AiSession.Instance.VoxelUpdateQueueStack.TryPop(out _voxelUpdatesQueue) || _voxelUpdatesQueue == null)
        _voxelUpdatesQueue = new MyQueue<VoxelUpdateItem>(10);
      else
        _voxelUpdatesQueue.Clear();

      float _;
      var gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(botStart, out _);
      if (gravity.LengthSquared() > 0)
      {
        _upVector = Vector3D.Normalize(-gravity);
        fwd = Vector3D.CalculatePerpendicularVector(_upVector);

        checkVoxel = MyGamePruningStructure.GetClosestPlanet(botStart);
      }
      else
      {
        List<MyVoxelBase> vList;
        if (!AiSession.Instance.VoxelMapListStack.TryPop(out vList) || vList == null)
          vList = new List<MyVoxelBase>();
        else
          vList.Clear();

        var halfSize = vec * CellSize;
        var box = new BoundingBoxD(botStart - halfSize, botStart + halfSize);
        MyGamePruningStructure.GetAllVoxelMapsInBox(ref box, vList);

        if (vList.Count > 0)
        {
          MyVoxelBase vMap = null;
          for (int i = 0; i < vList.Count; i++)
          {
            vMap = vList[i];
            if (vMap?.RootVoxel != null)
            {
              checkVoxel = vMap.RootVoxel;
              break;
            }
          }

          if (checkVoxel != null)
          {
            Vector3I minVoxel, maxVoxel;
            vMap.GetContainedVoxelCoords(ref box, out minVoxel, out maxVoxel);

            Vector3I_RangeIterator iter = new Vector3I_RangeIterator(ref minVoxel, ref maxVoxel);
            double distanceSqd = double.MaxValue;
            Vector3D? closestVoxelPostion = null;

            var checkPoint = botStart;
            var directionToBot = Vector3D.Normalize(botMatrix.Translation - checkPoint);
            while (PointInsideVoxel(checkPoint, checkVoxel) && OBB.Contains(ref checkPoint))
            {
              checkPoint += directionToBot * 2;
            }

            while (iter.IsValid())
            {
              var current = iter.Current;
              iter.MoveNext();

              Vector3D worldPosition;
              MyVoxelCoordSystems.VoxelCoordToWorldPosition(vMap.PositionLeftBottomCorner, ref current, out worldPosition);

              if (PointInsideVoxel(worldPosition, vMap.RootVoxel))
              {
                var dSqd = Vector3D.DistanceSquared(worldPosition, checkPoint);
                if (dSqd < distanceSqd)
                {
                  distanceSqd = dSqd;
                  closestVoxelPostion = worldPosition;
                }
              }
            }

            if (closestVoxelPostion.HasValue)
            {
              _upVector = Vector3D.Normalize(checkPoint - closestVoxelPostion.Value);
              fwd = Vector3D.CalculatePerpendicularVector(_upVector);
            }
            else
            {
              List<IHitInfo> hitList;
              if (!AiSession.Instance.HitListStack.TryPop(out hitList) || hitList == null)
                hitList = new List<IHitInfo>();
              else
                hitList.Clear();

              bool OK = false;
              MyAPIGateway.Physics.CastRay(botStart, vMap.PositionComp.WorldVolume.Center, hitList, CollisionLayers.CharacterCollisionLayer);
              if (hitList.Count > 0)
              {
                for (int i = 0; i < hitList.Count; i++)
                {
                  var hit = hitList[i];
                  var voxel = hit?.HitEntity as MyVoxelBase;
                  if (voxel != null)
                  {
                    OK = true;
                    _upVector = hit.Normal;
                    fwd = Vector3D.CalculatePerpendicularVector(_upVector);
                    break;
                  }
                }
              }

              hitList.Clear();
              AiSession.Instance.HitListStack.Push(hitList);

              if (!OK)
              {
                var matrix = MatrixD.CreateWorld(botStart, _upVector, fwd);
                var upDir = matrix.GetClosestDirection(botMatrix.Up);
                var fwDir = matrix.GetClosestDirection(botMatrix.Forward);

                _upVector = matrix.GetDirectionVector(upDir);
                fwd = matrix.GetDirectionVector(fwDir);
              }
            }
          }
        }

        vList.Clear();
        AiSession.Instance.VoxelMapListStack.Push(vList);

        if (checkVoxel == null)
        {
          CellSize *= 2;
          vec /= 2;
          BoundingBox = new BoundingBoxI(-vec, vec);
        }
      }

      WorldMatrix = MatrixD.CreateWorld(botStart, fwd, _upVector);
      MatrixNormalizedInv = MatrixD.Normalize(MatrixD.Invert(WorldMatrix));

      if (checkVoxel != null)
      {
        var tuple = checkVoxel.GetVoxelContentInBoundingBox_Fast(BoundingBox, WorldMatrix, true);

        if (tuple.Item1 == 0 && tuple.Item2 == 0)
        {
          checkVoxel = null;
          CellSize *= 2;
          vec /= 2;
          BoundingBox = new BoundingBoxI(-vec, vec);
        }
        else
        {
          checkVoxel.RangeChanged += Planet_RangeChanged;
          checkVoxel.OnMarkForClose += Planet_OnMarkForClose;
        }
      }

      RootVoxel = checkVoxel;
      _gridSizeR = 1f / CellSize;
      Vector3D worldCenter;

      if (RootVoxel != null)
      {
        Vector3I voxelCoord;
        MyVoxelCoordSystems.WorldPositionToVoxelCoord(RootVoxel.PositionComp.WorldMatrixNormalizedInv, Vector3D.Zero, RootVoxel.SizeInMetresHalf, ref botStart, out voxelCoord);
        MyVoxelCoordSystems.VoxelCoordToWorldPosition(RootVoxel.WorldMatrix, Vector3D.Zero, RootVoxel.SizeInMetresHalf, ref voxelCoord, out worldCenter);
      }
      else
      {
        var localVec = WorldToLocal(botStart);
        worldCenter = LocalToWorld(localVec);
      }

      var hExtents = (Vector3D)BoundingBox.HalfExtents + Vector3D.Half;
      OBB = new MyOrientedBoundingBoxD(worldCenter, hExtents * CellSize, Quaternion.CreateFromRotationMatrix(WorldMatrix));

      AiSession.Instance.MapInitQueue.Enqueue(this);
    }

    public override bool IsPositionValid(Vector3D position)
    {
      if (OBB.Contains(ref position))
        return !PointInsideVoxel(position, RootVoxel);

      return false;
    }

    public override bool GetClosestValidNode(BotBase bot, Vector3I testNode, out Vector3I node, Vector3D? up = null, bool isSlimBlock = false, bool currentIsDenied = false, bool allowAirNodes = true, bool preferGroundNode = true)
    {
      node = testNode;
      Node n;
      if (!currentIsDenied && _openTileDict.TryGetValue(node, out n) && !Obstacles.ContainsKey(node) && !ObstacleNodes.ContainsKey(node))
      {
        if (preferGroundNode)
        {
          if (!n.IsAirNode)
            return true;
        }
        else if (allowAirNodes || !n.IsAirNode)
          return true;
      }

      var center = node;
      double localDistance = double.MaxValue;
      double groundDistance = double.MaxValue;
      var worldPosition = LocalToWorld(testNode);
      Vector3I? closestGround = null;

      foreach (var point in Neighbors(bot, center, center, worldPosition, false, currentIsObstacle: true, up: up))
      {
        _openTileDict.TryGetValue(point, out n);
        var isAirNode = n.IsAirNode;
        var isWaterNode = n.IsWaterNode;

        if (!allowAirNodes && isAirNode)
          continue;

        if (bot != null && !isWaterNode && bot.WaterNodesOnly)
          continue;

        if (bot == null || ((!isAirNode || bot.CanUseAirNodes) && (!isWaterNode || bot.CanUseWaterNodes) && (!n.IsSpaceNode(this) || bot.CanUseSpaceNodes)))
        {

          var testPosition = LocalToWorld(point);
          var dist = Vector3D.DistanceSquared(testPosition, worldPosition);

          if (dist < localDistance)
          {
            localDistance = dist;
            node = point;
          }

          if (preferGroundNode && !isAirNode && dist < groundDistance)
          {
            groundDistance = dist;
            closestGround = point;
          }
        }
      }

      if (preferGroundNode && closestGround.HasValue)
        node = closestGround.Value;

      return localDistance < double.MaxValue;
    }

    public override void GetBlockedNodeEdges(Node nodeBase)
    {
      // not used here
    }

    public override bool GetRandomNodeNearby(BotBase bot, Vector3D targetPosition, out Vector3I localPos)
    {
      List<Vector3I> localNodes;
      if (!AiSession.Instance.LineListStack.TryPop(out localNodes))
        localNodes = new List<Vector3I>();
      else
        localNodes.Clear();

      var botPosition = bot.Target.CurrentBotPosition;
      var botWorldMatrix = bot.WorldMatrix;

      localPos = WorldToLocal(botPosition);
      var upDir = WorldMatrix.GetClosestDirection(botWorldMatrix.Up);
      var intVec = Base6Directions.GetIntVector(upDir);

      if (!_openTileDict.ContainsKey(localPos))
        localPos -= intVec;

      foreach (var dir in AiSession.Instance.CardinalDirections)
      {
        if (dir.Dot(ref intVec) != 0)
          continue;

        var newNode = localPos + dir;
        Node node;

        if (!_openTileDict.ContainsKey(newNode) || Obstacles.ContainsKey(newNode) || !_openTileDict.TryGetValue(localPos, out node) || node.IsBlocked(dir))
          continue;

        localNodes.Add(newNode);
      }

      foreach (var dir in AiSession.Instance.DiagonalDirections)
      {
        var dir1 = new Vector3I(dir.X, 0, 0);
        var dir2 = new Vector3I(0, 0, dir.Z);

        var node1 = localPos + dir1;
        var node2 = localPos + dir2;
        var newNode = localPos + dir;

        if (!_openTileDict.ContainsKey(newNode) || Obstacles.ContainsKey(newNode)
          || !_openTileDict.ContainsKey(node1) || Obstacles.ContainsKey(node1)
          || !_openTileDict.ContainsKey(node2) || Obstacles.ContainsKey(node2))
          continue;

        Node n;
        if (!_openTileDict.TryGetValue(localPos, out n))
          continue;

        if (n.IsBlocked(dir1) || n.IsBlocked(dir2))
          continue;

        localNodes.Add(newNode);
      }

      var result = false;
      if (localNodes.Count > 0)
      {
        var rnd = MyUtils.GetRandomInt(0, localNodes.Count);
        localPos = localNodes[rnd];
        result = true;
      }

      localNodes.Clear();
      AiSession.Instance.LineListStack.Push(localNodes);
      return result;
    }

    public override Vector3D? GetBufferZoneTargetPosition(Vector3D fromPosition, Vector3D toPosition, bool getEdgePoint = false)
    {
      if (OBB.Contains(ref fromPosition))
        return fromPosition;

      var line = new LineD(fromPosition, toPosition);
      var num = OBB.Intersects(ref line);
      if (!num.HasValue)
        return null;

      var point = fromPosition + line.Direction * num.Value;

      if (!getEdgePoint)
        point += line.Direction * 10;

      return point;
    }

    public override Vector3D? GetBufferZoneTargetPositionCentered(Vector3D fromPosition, Vector3D toPosition, Vector3D sideNormal, bool getEdgePoint = false)
    {
      if (OBB.Contains(ref fromPosition))
        return fromPosition;

      var dSqd = Vector3D.DistanceSquared(fromPosition, toPosition);
      fromPosition = toPosition + sideNormal * dSqd;

      var line = new LineD(fromPosition, toPosition);
      var num = OBB.Intersects(ref line);
      if (!num.HasValue)
        return null;

      var point = fromPosition + line.Direction * num.Value;

      if (!getEdgePoint)
        point += line.Direction * 10;

      return point;
    }

    public override bool InBounds(Vector3I node)
    {
      return BoundingBox.Contains(node) != ContainmentType.Disjoint;
    }

    public override Vector3D LocalToWorld(Vector3I localVector)
    {
      return MyCubeGrid.GridIntegerToWorld(CellSize, localVector, WorldMatrix);
    }

    public override Vector3I WorldToLocal(Vector3D worldVector)
    {
      return Vector3I.Round(Vector3D.Transform(worldVector, MatrixNormalizedInv) * _gridSizeR);
    }

    public override IEnumerable<Vector3I> Neighbors(BotBase bot, Vector3I previousNode, Vector3I currentNode, Vector3D worldPosition, bool checkDoors, bool currentIsObstacle = false, bool isSlimBlock = false, Vector3D? up = null)
    {
      Vector3I upVec = Vector3I.Zero;

      if (up.HasValue)
      {
        var upDir = WorldMatrix.GetClosestDirection(up.Value);
        upVec = Base6Directions.GetIntVector(upDir);
      }

      foreach (var dir in AiSession.Instance.CardinalDirections)
      {
        if (dir.Dot(ref upVec) != 0)
        {
          continue;
        }

        var next = currentNode + dir;
        if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, false, currentIsObstacle))
        {
          yield return next;
        }
      }

      foreach (var dir in AiSession.Instance.DiagonalDirections)
      {
        if (dir.Dot(ref upVec) != 0)
        {
          continue;
        }

        var next = currentNode + dir;
        if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, false, currentIsObstacle))
        {
          yield return next;
        }
      }

      if (currentIsObstacle)
        yield break;

      foreach (var dir in AiSession.Instance.VoxelMovementDirections)
      {
        if (dir.Dot(ref upVec) != 0)
        {
          continue;
        }

        var next = currentNode + dir;
        if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, false, currentIsObstacle))
        {
          yield return next;
        }
      }
    }

    public override bool Passable(BotBase bot, Vector3I previousNode, Vector3I currentNode, Vector3I nextNode, Vector3D worldPosition, bool checkDoors, bool currentIsObstacle = false, bool isSlimBlock = false)
    {
      if (Obstacles.ContainsKey(nextNode) || ObstacleNodes.ContainsKey(nextNode))
      {
        return false;
      }

      Node nNext;
      if (!_openTileDict.TryGetValue(nextNode, out nNext))
      {
        return false;
      }

      if ((nNext.IsAirNode && !bot.CanUseAirNodes) || (nNext.IsSpaceNode(this) && !bot.CanUseSpaceNodes) || (nNext.IsWaterNode && !bot.CanUseWaterNodes))
      {
        return false;
      }

      if (RootVoxel != null)
      {
        using (RootVoxel.Pin())
        {
          Vector3D worldCurrent;
          Vector3D worldNext = LocalToWorld(nNext.Position) + nNext.Offset;

          Node node;
          if (_openTileDict.TryGetValue(currentNode, out node))
          {
            worldCurrent = LocalToWorld(node.Position) + node.Offset;
          }
          else
          {
            worldCurrent = LocalToWorld(currentNode);
          }

          var direction = Vector3D.Normalize(worldNext - worldCurrent);
          worldCurrent += direction * CellSize * 0.5;

          if (LineIntersectsVoxel(worldCurrent, worldNext, RootVoxel))
            return false;

          //var line = new LineD(worldCurrent, worldNext);

          //Vector3D? _;
          //if (RootVoxel.RootVoxel.GetIntersectionWithLine(ref line, out _))
          //  return false;
        }
      }

      if (nNext.BlockedMask == 0)
      {
        return true;
      }

      var dirVec = (currentIsObstacle) ? currentNode - nextNode : nextNode - currentNode;
      return !nNext.IsBlocked(dirVec);
    }

    public override void Refresh() => Init();

    private void Planet_RangeChanged(MyVoxelBase storage, Vector3I minVoxelChanged, Vector3I maxVoxelChanged, MyStorageDataTypeFlags changedData)
    {
      try
      {
        if (AiSession.Instance == null || !AiSession.Instance.Registered || !IsValid)
          return;

        using (_pendingLockObject.AcquireExclusiveUsing())
        {
          var min = minVoxelChanged;
          var max = maxVoxelChanged;

          bool found = false;
          for (int i = _voxelUpdatesNeeded.Count - 1; i >= 0; i--)
          {
            var updateItem = _voxelUpdatesNeeded[i];
            if (updateItem.Check(ref min, ref max))
            {
              found = true;
              break;
            }
          }

          if (!found)
          {
            VoxelUpdateItem updateItem;
            if (!AiSession.Instance.VoxelUpdateItemStack.TryPop(out updateItem) || updateItem == null)
              updateItem = new VoxelUpdateItem();

            updateItem.Init(ref min, ref max);
            _voxelUpdatesNeeded.Add(updateItem);
          }

          NeedsVoxelUpdate = true;
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in VoxelGridMap.Planet_RangeChanged: {ex.Message}\n{ex.StackTrace}");
      }
    }

    public void UpdateVoxels()
    {
      try
      {
        if (RootVoxel == null || RootVoxel.MarkedForClose)
        {
          NeedsVoxelUpdate = false;
          return;
        }

        if (!Ready)
          return;

        using (_pendingLockObject.AcquireExclusiveUsing())
        {
          for (int i = _voxelUpdatesNeeded.Count - 1; i >= 0; i--)
          {
            var updateItem = _voxelUpdatesNeeded[i];
            if (updateItem.Update())
            {
              _voxelUpdatesNeeded.RemoveAtFast(i);
              _voxelUpdatesQueue.Enqueue(updateItem);
            }
          }

          if (_updateTask.IsComplete)
          {
            if (_updateTask.Exceptions != null)
            {
              AiSession.Instance.Logger.ClearCached();
              AiSession.Instance.Logger.AddLine($"Exceptions found during voxel update task!\n");
              foreach (var ex in _updateTask.Exceptions)
                AiSession.Instance.Logger.AddLine($" -> {ex.Message}\n{ex.StackTrace}\n");

              AiSession.Instance.Logger.LogAll();
              MyAPIGateway.Utilities.ShowNotification($"Exception during task!");
            }

            if (_voxelUpdatesQueue.Count > 0)
            {
              Ready = false;
              _updateTask = MyAPIGateway.Parallel.Start(ApplyVoxelChanges, SetReady);
            }

            NeedsVoxelUpdate = _voxelUpdatesNeeded.Count > 0;
          }
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in VoxelGridMap.UpdateVoxels: {ex.Message}\n{ex.StackTrace}");
      }
    }

    void ApplyVoxelChanges()
    {
      try
      {
        while (_voxelUpdatesQueue.Count > 0)
        {
          var updateItem = _voxelUpdatesQueue.Dequeue();
          var updateBox = updateItem.BoundingBox;

          var minWorld = Vector3D.Transform((Vector3)updateBox.Min - RootVoxel.SizeInMetresHalf, RootVoxel.WorldMatrix);
          var maxWorld = Vector3D.Transform((Vector3)updateBox.Max - RootVoxel.SizeInMetresHalf, RootVoxel.WorldMatrix);

          if (!OBB.Contains(ref minWorld) && !OBB.Contains(ref maxWorld))
          {
            AiSession.Instance.VoxelUpdateItemStack.Push(updateItem);
            return;
          }

          var min = WorldToLocal(minWorld);
          var max = WorldToLocal(maxWorld);
          Vector3I.MinMax(ref min, ref max);

          var mapMin = Vector3I.Max(BoundingBox.Min, min - 3);
          var mapMax = Vector3I.Min(BoundingBox.Max, max + 3);

          var iter = new Vector3I_RangeIterator(ref mapMin, ref mapMax);

          while (iter.IsValid())
          {
            if (Dirty || RootVoxel == null || RootVoxel.MarkedForClose)
            {
              AiSession.Instance.VoxelUpdateItemStack.Push(updateItem);
              return;
            }

            var current = iter.Current;
            iter.MoveNext();

            Node node;
            if (_openTileDict.TryGetValue(current, out node))
            {
              if (node != null)
              {
                AiSession.Instance.NodeStack?.Push(node);
              }

              _openTileDict.Remove(current);
            }

            byte b;
            ObstacleNodes.TryRemove(current, out b);
            Obstacles.TryRemove(current, out b);
            OptimizedCache.TryRemove(current, out node);
          }

          CheckForPlanetTiles(ref mapMin, ref mapMax);
          AiSession.Instance.VoxelUpdateItemStack.Push(updateItem);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in ApplyVoxelChanges: {ex.Message}\n{ex.StackTrace}");
      }
    }

    private void Planet_OnMarkForClose(MyEntity obj)
    {
      if (RootVoxel != null)
      {
        RootVoxel.OnMarkForClose -= Planet_OnMarkForClose;
        RootVoxel.RangeChanged -= Planet_RangeChanged;
      }
    }

    internal override void Init()
    {
      if (GraphLocked || !IsValid)
        return;

      GraphLocked = true;
      Ready = false;
      Dirty = false;
      _openTileDict.Clear();
      ObstacleNodes.Clear();
      Obstacles.Clear();
      MyAPIGateway.Parallel.StartBackground(InitGridArea, SetReady);

      // Testing only
      //InitGridArea();
      //SetReady();
    }

    void InitGridArea()
    {
      try
      {
        //AiSession.Instance.Logger.Log($"Voxel.InitGridArea starting ({Key})");

        CheckForPlanetTiles(ref BoundingBox.Min, ref BoundingBox.Max);
        UpdateTempObstacles();

        //AiSession.Instance.Logger.Log($"Voxel.InitGridArea finished ({Key})");
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in InitGridArea ({Key}): {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
        throw;
      }
    }

    HashSet<Node> _groundPoints = new HashSet<Node>();
    void CheckForPlanetTiles(ref Vector3I min, ref Vector3I max)
    {
      bool createGroundNodes = false;
      bool checkForVoxel = false;
      bool checkForWater = false;
      var planet = RootVoxel as MyPlanet;

      if (RootVoxel != null && !RootVoxel.MarkedForClose)
      {
        checkForWater = planet != null && WaterAPI.Registered && WaterAPI.HasWater(RootVoxel.EntityId);
        checkForVoxel = true;

        if (planet != null)
        {
          createGroundNodes = true;
        }
        else
        {
          var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(OBB.Center, 0);
          createGroundNodes = aGrav.LengthSquared() > 0;
        }
      }

      var worldMatrix = WorldMatrix;
      var gravityNorm = -_upVector;
      var blockedVoxelEdges = AiSession.Instance.BlockedVoxelEdges;
      var cellSize = CellSize;

      var iter = new Vector3I_RangeIterator(ref min, ref max);
      _groundPoints.Clear();
      int tunnelCount = 0;

      while (iter.IsValid())
      {
        var localPoint = iter.Current;
        iter.MoveNext();

        bool isGroundNode = false;
        bool isTunnelNode = false;

        // worldPoint = LocalToWorld(localPoint);
        Vector3D worldPoint = localPoint * cellSize;
        Vector3D.Transform(ref worldPoint, ref worldMatrix, out worldPoint);
        var groundPoint = worldPoint;

        if (checkForVoxel)
        {
          if (RootVoxel == null || RootVoxel.MarkedForClose)
            checkForVoxel = false;

          if (PointInsideVoxel(worldPoint, RootVoxel))
            continue;

          var pointBelow = worldPoint + gravityNorm * cellSize;
          if (createGroundNodes && PointInsideVoxel(pointBelow, RootVoxel))
          {
            isGroundNode = true;

            var pointInAir = worldPoint;
            var pointInVoxel = pointBelow;
            int count = 0;

            while (Vector3D.DistanceSquared(pointInAir, pointInVoxel) > 0.1)
            {
              count++;
              if (count > 500)
              {
                AiSession.Instance.Logger.Log($"Point {localPoint} took too many attempts to finish", MessageType.WARNING);
                break;
              }

              var mid = (pointInAir + pointInVoxel) * 0.5;
              if (PointInsideVoxel(mid, RootVoxel))
              {
                pointInVoxel = mid;
              }
              else
              {
                pointInAir = mid;
              }
            }

            groundPoint = pointInAir - gravityNorm;

            Vector3D? surfacePoint = null;
            if (planet != null)
              surfacePoint = planet.GetClosestSurfacePointGlobal(worldPoint) - gravityNorm;
            else
            {
              var closestPoint = GetClosestSurfacePointFast(null, worldPoint - gravityNorm * 20, -gravityNorm);
              if (closestPoint.HasValue)
                surfacePoint = closestPoint.Value - gravityNorm;
            }

            if (surfacePoint.HasValue)
            {
              var surfaceValue = surfacePoint.Value;

              while (PointInsideVoxel(surfaceValue, RootVoxel) && OBB.Contains(ref surfaceValue))
                surfaceValue -= gravityNorm;

              var vector = groundPoint - surfaceValue;
              if (vector.LengthSquared() > 9 && vector.Dot(worldMatrix.Down) > 0)
              {
                var line = new LineD(surfaceValue, groundPoint);
                var lerpAmount = MathHelper.Clamp(1 / line.Length, 0, 1);
                var point = Vector3D.Lerp(line.From, line.To, lerpAmount);
                var testAmount = MathHelper.Clamp(lerpAmount * 2, 0, 1);

                int testCount = 0;
                int maxCount = (int)Math.Ceiling(line.Length) + 1;

                while (Vector3D.DistanceSquared(point, line.To) > 9)
                {
                  if (PointInsideVoxel(point, RootVoxel))
                  {
                    isTunnelNode = true;
                    tunnelCount++;
                    break;
                  }
                  else if (++testCount > maxCount)
                  {
                    break;
                  }

                  point = Vector3D.Lerp(line.From, line.To, testAmount);
                  testAmount = MathHelper.Clamp(testAmount + lerpAmount, 0, 1);
                }
              }
            }
          }
        }

        var isInWater = checkForWater && WaterAPI.IsUnderwater(groundPoint);

        NodeType nType = NodeType.GridPlanet;
        if (isGroundNode)
          nType |= NodeType.Ground;
        if (isInWater)
          nType |= NodeType.Water;
        if (isTunnelNode)
          nType |= NodeType.Tunnel;

        var offset = (Vector3)(groundPoint - LocalToWorld(localPoint));

        Node node;
        if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
          node = new Node();

        node.Update(localPoint, offset, nType, 0);

        if (checkForVoxel)
        {
          foreach (var dir in blockedVoxelEdges)
          {
            node.SetBlocked(dir);
          }
        }

        _openTileDict[localPoint] = node;

        if (isGroundNode)
          _groundPoints.Add(node);
      }

      GraphHasTunnel = tunnelCount > 25;

      var upVec = Vector3I.Up;
      foreach (var node in _groundPoints)
      {
        var localAbove = node.Position + upVec;

        Node nAbove;
        if (_openTileDict.TryGetValue(localAbove, out nAbove) && !nAbove.IsGridNode)
        {
          nAbove.SetNodeType(NodeType.Ground);

          var worldPoint = LocalToWorld(node.Position) + node.Offset;
          nAbove.Offset = (Vector3)(worldPoint - LocalToWorld(nAbove.Position));
        }
      }

      _groundPoints.Clear();
    }

    public override bool GetRandomOpenNode(BotBase bot, Vector3D requestedPosition, out Node node)
    {
      node = null;
      if (_openTileDict.Count == 0 || (!bot.CanUseAirNodes && RootVoxel == null))
        return false;

      var localBot = WorldToLocal(bot.Target.CurrentBotPosition);
      var botPosition = LocalToWorld(localBot);

      var localReq = WorldToLocal(requestedPosition);
      requestedPosition = LocalToWorld(localReq);

      var vector = requestedPosition - botPosition;
      var length = vector.Normalize();
      var cellSize = (double)CellSize;

      var count = (int)Math.Floor(length / cellSize);

      for (int i = count; i >= 0; i--)
      {
        var worldPos = botPosition + (vector * count * cellSize);
        if (!OBB.Contains(ref worldPos))
          continue;

        Node tempNode;
        if (!bot.CanUseAirNodes && GetClosestGroundNode(WorldToLocal(worldPos), out tempNode))
          worldPos = LocalToWorld(tempNode.Position);

        if (IsPositionUsable(bot, worldPos, out node))
          return true;
      }

      node = null;
      return false;
    }

    bool GetClosestGroundNode(Vector3I pos, out Node node)
    {
      if (_openTileDict.TryGetValue(pos, out node) && node.IsGroundNode)
        return true;

      if (GetClosestSurfacePointFast(LocalToWorld(pos), WorldMatrix.Up, out node))
        return true;

      pos += Vector3I.Up * 10;

      for (int i = 0; i < 20; i++)
      {
        pos += Vector3I.Down;

        if (_openTileDict.TryGetValue(pos, out node) && node.IsGroundNode)
          return true;
      }

      node = null;
      return false;
    }

    public override void UpdateTempObstacles()
    {
      if (!_obstacleTask.IsComplete)
        return;

      if (_obstacleTask.Exceptions != null)
      {
        AiSession.Instance.Logger.ClearCached();
        AiSession.Instance.Logger.AddLine($"Exceptions found during pathfinder task!\n");
        foreach (var ex in _obstacleTask.Exceptions)
          AiSession.Instance.Logger.AddLine($" -> {ex.Message}\n{ex.StackTrace}\n");

        AiSession.Instance.Logger.LogAll();
        MyAPIGateway.Utilities.ShowNotification($"Exception during ObstacleTask!");
      }

      List<MyEntity> tempEntities;
      if (!AiSession.Instance.EntListStack.TryPop(out tempEntities))
        tempEntities = new List<MyEntity>();
      else
        tempEntities.Clear();

      List<IMySlimBlock> blocks;
      if (!AiSession.Instance.SlimListStack.TryPop(out blocks) || blocks == null)
        blocks = new List<IMySlimBlock>();
      else
        blocks.Clear();

      var sphere = new BoundingSphereD(OBB.Center, OBB.HalfExtent.AbsMax());
      MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, tempEntities);

      for (int i = tempEntities.Count - 1; i >= 0; i--)
      {
        var grid = tempEntities[i] as MyCubeGrid;
        if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose)
          continue;

        if (grid.IsStatic && grid.GridSizeEnum == VRage.Game.MyCubeSize.Large && grid.BlocksCount > 5)
          continue;

        ((IMyCubeGrid)grid).GetBlocks(blocks);
      }

      tempEntities.Clear();
      AiSession.Instance.EntListStack.Push(tempEntities);

      _tempObstaclesWorkData.Blocks = blocks;
      _obstacleTask = MyAPIGateway.Parallel.Start(UpdateTempObstaclesAsync, UpdateTempObstaclesCallback, _tempObstaclesWorkData);
    }

    void UpdateTempObstaclesAsync(WorkData data)
    {
      var obstacleData = data as ObstacleWorkData;
      if (obstacleData == null || AiSession.Instance?.BlockFaceDictionary == null || !AiSession.Instance.Registered)
        return;

      ObstacleNodesTemp.Clear();
      var blocks = obstacleData.Blocks;

      for (int i = 0; i < blocks.Count; i++)
      {
        var b = blocks[i];
        if (b?.CubeGrid == null || b.IsDestroyed || b.CubeGrid.MarkedForClose)
          continue;

        Dictionary<Vector3I, HashSet<Vector3I>> faceDict;
        if (!AiSession.Instance.BlockFaceDictionary.TryGetValue(b.BlockDefinition.Id, out faceDict))
          continue;

        Matrix matrix = new Matrix
        {
          Forward = Base6Directions.GetVector(b.Orientation.Forward),
          Left = Base6Directions.GetVector(b.Orientation.Left),
          Up = Base6Directions.GetVector(b.Orientation.Up)
        };

        if (faceDict.Count < 2)
          matrix.TransposeRotationInPlace();

        var cubeDef = b.BlockDefinition as MyCubeBlockDefinition;
        Vector3I center = cubeDef.Center;
        Vector3I.TransformNormal(ref center, ref matrix, out center);
        var adjustedPosition = b.Position - center;

        foreach (var kvp in faceDict)
        {
          var cell = kvp.Key;
          Vector3I.TransformNormal(ref cell, ref matrix, out cell);
          var position = adjustedPosition + cell;

          var worldPoint = b.CubeGrid.GridIntegerToWorld(position);
          if (!OBB.Contains(ref worldPoint))
            continue;

          var graphLocal = WorldToLocal(worldPoint);
          if (_openTileDict.ContainsKey(graphLocal) && !ObstacleNodesTemp.ContainsKey(graphLocal) && !Obstacles.ContainsKey(graphLocal))
          {
            ObstacleNodesTemp[graphLocal] = new byte();
          }

          foreach (var dir in AiSession.Instance.CardinalDirections)
          {
            var otherLocal = graphLocal + dir;

            if (_openTileDict.ContainsKey(otherLocal) && !ObstacleNodesTemp.ContainsKey(otherLocal) && !Obstacles.ContainsKey(otherLocal))
              ObstacleNodesTemp[otherLocal] = new byte();
          }
        }
      }
    }

    void UpdateTempObstaclesCallback(WorkData data)
    {
      Interlocked.CompareExchange(ref ObstacleNodes, ObstacleNodesTemp, ObstacleNodes);

      var obstacleData = data as ObstacleWorkData;
      if (obstacleData?.Blocks != null && AiSession.Instance?.ObstacleWorkDataStack != null && AiSession.Instance.Registered)
      {
        obstacleData.Blocks.Clear();
        AiSession.Instance.SlimListStack.Push(obstacleData.Blocks);
      }
    }

    public override void Close()
    {
      try
      {
        Ready = false;
        Dirty = true;

        _groundPoints?.Clear();
        _groundPoints = null;

        _openTileDict?.Clear();
        _openTileDict = null;

        _pendingLockObject = null;

        if (AiSession.Instance != null && AiSession.Instance.Registered)
        {
          if (_tempObstaclesWorkData != null)
          {
            AiSession.Instance.ObstacleWorkDataStack?.Push(_tempObstaclesWorkData);
          }

          if (_voxelUpdatesNeeded != null)
          {
            AiSession.Instance.VoxelUpdateListStack?.Push(_voxelUpdatesNeeded);
          }

          if (_voxelUpdatesQueue != null)
          {
            AiSession.Instance.VoxelUpdateQueueStack?.Push(_voxelUpdatesQueue);
          }
        }
        else
        {
          _voxelUpdatesNeeded?.Clear();
          _voxelUpdatesQueue?.Clear();

          _tempObstaclesWorkData = null;
          _voxelUpdatesNeeded = null;
          _voxelUpdatesQueue = null;
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in {this.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
      }
      finally
      {
        base.Close();
      }
    }

    public override Node GetReturnHomePoint(BotBase bot)
    {
      if (bot == null || bot.IsDead)
        return null;

      Node node = null;
      Node backup = null;
      foreach (var kvp in _openTileDict)
      {
        var localPosition = kvp.Key;
        if (IsPositionUsable(bot, kvp.Key))
        {
          node = kvp.Value;

          if (node.IsGroundNode)
            return node;
          else if (backup == null)
            backup = node;
        }
      }

      return node ?? backup;
    }

    public override bool TryGetNodeForPosition(Vector3I position, out Node node)
    {
      return _openTileDict.TryGetValue(position, out node) && node != null;
    }

    public override bool IsOpenTile(Vector3I position)
    {
      return _openTileDict.ContainsKey(position);
    }

    public override bool IsObstacle(Vector3I position, bool includeTemp)
    {
      bool result = Obstacles.ContainsKey(position);

      if (!includeTemp)
        return result;

      return result || ObstacleNodes.ContainsKey(position) || TempBlockedNodes.ContainsKey(position);
    }

    public override Node GetValueOrDefault(Vector3I position, Node defaultValue)
    {
      Node node;
      if (_openTileDict.TryGetValue(position, out node))
        return node;

      return defaultValue;
    }

    public override IMySlimBlock GetBlockAtPosition(Vector3I localPosition) => null;
  }
}
