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
    public const byte DefaultHalfSize = 30;

    /// <summary>
    /// The default cell size for a voxel grid map
    /// </summary>
    public const float DefaultCellSize = 1.25f;

    internal Dictionary<Vector3I, Node> OpenTileDict = new Dictionary<Vector3I, Node>(Vector3I.Comparer);

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
      IsGridGraph = false;

      _upVector = botMatrix.Up;
      var newForward = botMatrix.Forward;

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
        newForward = Vector3D.CalculatePerpendicularVector(_upVector);

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
          double maxDistance = double.MaxValue;
          MyVoxelBase vMap = null;
          for (int i = 0; i < vList.Count; i++)
          {
            vMap = vList[i];
            if (vMap?.RootVoxel != null && !(vMap.RootVoxel is MyPlanet))
            {
              var distance = Vector3D.DistanceSquared(vMap.RootVoxel.PositionComp.WorldVolume.Center, botStart);
              if (distance < maxDistance)
              {
                maxDistance = distance;
                checkVoxel = vMap.RootVoxel;
              }
            }
          }
        }

        vList.Clear();
        AiSession.Instance.VoxelMapListStack.Push(vList);
      }

      WorldMatrix = MatrixD.CreateWorld(botStart, newForward, _upVector);

      if (checkVoxel == null)
      {
        CellSize *= 2;
        vec /= 2;
        BoundingBox = new BoundingBoxI(-vec, vec);
      }
      else
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

      if (RootVoxel is MyPlanet)
      {
        Vector3D worldCenter;
        Vector3I voxelCoord;
        MyVoxelCoordSystems.WorldPositionToVoxelCoord(RootVoxel.PositionComp.WorldMatrixNormalizedInv, Vector3D.Zero, RootVoxel.SizeInMetresHalf, ref botStart, out voxelCoord);
        MyVoxelCoordSystems.VoxelCoordToWorldPosition(RootVoxel.WorldMatrix, Vector3D.Zero, RootVoxel.SizeInMetresHalf, ref voxelCoord, out worldCenter);
        WorldMatrix.Translation = worldCenter;
      }

      MatrixNormalizedInv = MatrixD.Normalize(MatrixD.Invert(WorldMatrix));
      var hExtents = (Vector3D)BoundingBox.HalfExtents + Vector3D.Half;
      OBB = new MyOrientedBoundingBoxD(WorldMatrix.Translation, hExtents * CellSize, Quaternion.CreateFromRotationMatrix(WorldMatrix));

      AiSession.Instance.MapInitQueue.Enqueue(this);
    }

    public override bool IsPositionValid(Vector3D position)
    {
      if (OBB.Contains(ref position))
        return !PointInsideVoxel(position, RootVoxel);

      return false;
    }

    public override bool GetClosestValidNode(BotBase bot, Vector3I testPosition, out Vector3I nodePosition, Vector3D? up = null, bool isSlimBlock = false, bool currentIsDenied = false, bool allowAirNodes = true, bool preferGroundNode = true)
    {
      nodePosition = testPosition;
      if (!IsValid || !Ready)
        return false;

      Node node;
      TryGetNodeForPosition(testPosition, out node);

      if (node != null && !currentIsDenied && !IsObstacle(testPosition, bot, true))
      {
        var isAir = node.IsAirNode;
        var isWater = node.IsWaterNode;
        if ((bot.RequiresJetpack || !preferGroundNode || !isAir) && (allowAirNodes || !isAir)
          && (!isWater || bot.CanUseWaterNodes) && (!isAir || bot.CanUseAirNodes)
          && (!bot.WaterNodesOnly || isWater) && (bot.CanUseSpaceNodes || !node.IsSpaceNode(this)))
        {
          return true;
        }
      }

      var localPosition = nodePosition;
      IMySlimBlock block = null;
      
      if (isSlimBlock)
      {
        block = GetBlockAtPosition(localPosition);

        if (block != null && !block.IsDestroyed)
        {
          var min = block.Min;
          var max = block.Max;

          if ((max - min) != Vector3I.Zero)
          {
            var minWorld = block.CubeGrid.GridIntegerToWorld(block.Min);
            var maxWorld = block.CubeGrid.GridIntegerToWorld(block.Max);
            min = WorldToLocal(minWorld);
            max = WorldToLocal(maxWorld);

            Vector3I.MinMax(ref min, ref max);
            Vector3I_RangeIterator iter = new Vector3I_RangeIterator(ref min, ref max);

            while (iter.IsValid())
            {
              var current = iter.Current;
              iter.MoveNext();

              if (GetClosestNodeInternal(bot, current, out nodePosition, up, isSlimBlock, currentIsDenied, allowAirNodes, preferGroundNode, block))
                return true;
            }

            return false;
          }
        }
      }

      return GetClosestNodeInternal(bot, localPosition, out nodePosition, up, isSlimBlock, currentIsDenied, allowAirNodes, preferGroundNode, block);
    }

    bool GetClosestNodeInternal(BotBase bot, Vector3I testPosition, out Vector3I nodePosition, Vector3D? up = null, bool isSlimBlock = false, bool currentIsDenied = false, bool allowAirNodes = true, bool preferGroundNode = true, IMySlimBlock block = null)
    {
      nodePosition = testPosition;

      Node node;
      TryGetNodeForPosition(testPosition, out node);

      if (node != null && !currentIsDenied && !IsObstacle(testPosition, bot, true))
      {
        var isAir = node.IsAirNode;
        var isWater = node.IsWaterNode;
        if ((bot.RequiresJetpack || !preferGroundNode || !isAir) && (allowAirNodes || !isAir)
          && (!isWater || bot.CanUseWaterNodes) && (!isAir || bot.CanUseAirNodes)
          && (!bot.WaterNodesOnly || isWater) && (bot.CanUseSpaceNodes || !node.IsSpaceNode(this)))
        {
          return true;
        }
      }

      double localDistance = double.MaxValue;
      double groundDistance = double.MaxValue;
      var worldPosition = LocalToWorld(testPosition);
      Vector3I? closestGround = null;

      var isFloater = bot?.Target.IsFloater == true;
      if (isSlimBlock || isFloater)
      {
        bool TryTwice = isFloater || block?.CubeGrid.GridSizeEnum == MyCubeSize.Large;

        foreach (var dir in AiSession.Instance.CardinalDirections)
        {
          Node n;
          if (TryGetNodeForPosition(testPosition + dir, out n) && n != null)
          {
            if (!IsObstacle(n.Position, bot, true) && IsPositionUsable(bot, n.Position))
            {
              nodePosition = n.Position;
              return true;
            }
          }

          if (TryTwice && TryGetNodeForPosition(testPosition + dir * 2, out n) && n != null)
          {
            if (!IsObstacle(n.Position, bot, true) && IsPositionUsable(bot, n.Position))
            {
              nodePosition = n.Position;
              return true;
            }
          }
        }

        foreach (var dir in AiSession.Instance.DiagonalDirections)
        {
          Node n;
          if (TryGetNodeForPosition(testPosition + dir, out n) && n != null)
          {
            if (!IsObstacle(n.Position, bot, true) && IsPositionUsable(bot, n.Position))
            {
              nodePosition = n.Position;
              return true;
            }
          }

          if (TryTwice && TryGetNodeForPosition(testPosition + dir * 2, out n) && n != null)
          {
            if (!IsObstacle(n.Position, bot, true) && IsPositionUsable(bot, n.Position))
            {
              nodePosition = n.Position;
              return true;
            }
          }
        }

        foreach (var dir in AiSession.Instance.VoxelMovementDirections)
        {
          Node n;
          if (TryGetNodeForPosition(testPosition + dir, out n) && n != null)
          {
            if (!IsObstacle(n.Position, bot, true) && IsPositionUsable(bot, n.Position))
            {
              nodePosition = n.Position;
              return true;
            }
          }

          if (TryTwice && TryGetNodeForPosition(testPosition + dir * 2, out n) && n != null)
          {
            if (!IsObstacle(n.Position, bot, true) && IsPositionUsable(bot, n.Position))
            {
              nodePosition = n.Position;
              return true;
            }
          }
        }

        return false;
      }

      foreach (var point in Neighbors(bot, testPosition, testPosition, worldPosition, false, currentIsObstacle: true, isSlimBlock: isSlimBlock, up: up))
      {
        OpenTileDict.TryGetValue(point, out node);
        var isAirNode = node.IsAirNode;
        var isWaterNode = node.IsWaterNode;

        if (!allowAirNodes && isAirNode && !bot.RequiresJetpack)
          continue;

        if (bot != null && !isWaterNode && bot.WaterNodesOnly)
          continue;

        if (bot == null || ((!isAirNode || bot.CanUseAirNodes) && (!isWaterNode || bot.CanUseWaterNodes) && (bot.CanUseSpaceNodes || !node.IsSpaceNode(this))))
        {

          var worldTest = LocalToWorld(point);
          var dist = Vector3D.DistanceSquared(worldTest, worldPosition);

          if (dist < localDistance)
          {
            localDistance = dist;
            nodePosition = point;
          }

          if (preferGroundNode && !isAirNode && dist < groundDistance)
          {
            groundDistance = dist;
            closestGround = point;
          }
        }
      }

      if (preferGroundNode && closestGround.HasValue)
        nodePosition = closestGround.Value;

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

      var botPosition = bot.BotInfo.CurrentBotPositionActual;
      var botWorldMatrix = bot.WorldMatrix;

      localPos = WorldToLocal(botPosition);
      var upDir = WorldMatrix.GetClosestDirection(botWorldMatrix.Up);
      var intVec = Base6Directions.GetIntVector(upDir);

      if (!OpenTileDict.ContainsKey(localPos))
        localPos -= intVec;

      foreach (var dir in AiSession.Instance.CardinalDirections)
      {
        if (dir.Dot(ref intVec) != 0)
          continue;

        var newNode = localPos + dir;
        Node node;

        if (!OpenTileDict.ContainsKey(newNode) || (bot?._pathCollection.Obstacles.ContainsKey(newNode) == true) || !OpenTileDict.TryGetValue(localPos, out node) || node.IsBlocked(dir))
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

        if (!OpenTileDict.ContainsKey(newNode) || !OpenTileDict.ContainsKey(node1) || !OpenTileDict.ContainsKey(node2))
          continue;

        if (bot?._pathCollection != null
          && (bot._pathCollection.Obstacles.ContainsKey(newNode)
          || bot._pathCollection.Obstacles.ContainsKey(node1)
          || bot._pathCollection.Obstacles.ContainsKey(node2)))
          continue;

        Node n;
        if (!OpenTileDict.TryGetValue(localPos, out n))
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

    public override IEnumerable<Vector3I> Neighbors(BotBase bot, Vector3I previousNode, Vector3I currentNode, Vector3D worldPosition, bool checkDoors, bool currentIsObstacle = false, bool isSlimBlock = false, Vector3D? up = null, bool checkRepairInfo = false)
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

    public override bool Passable(BotBase bot, Vector3I previousNode, Vector3I currentNode, Vector3I nextNode, Vector3D worldPosition, bool checkDoors, bool currentIsObstacle = false, bool isSlimBlock = false, bool checkRepairInfo = false)
    {
      if (ObstacleNodes.ContainsKey(nextNode))
      {
        return false;
      }

      if (bot?._pathCollection != null && bot._pathCollection.Obstacles.ContainsKey(nextNode))
      {
        return false;
      }

      Node nNext;
      if (!OpenTileDict.TryGetValue(nextNode, out nNext))
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
          if (OpenTileDict.TryGetValue(currentNode, out node))
          {
            worldCurrent = LocalToWorld(node.Position) + node.Offset;
          }
          else
          {
            worldCurrent = LocalToWorld(currentNode);
          }

          var direction = Vector3D.Normalize(worldNext - worldCurrent);
          worldCurrent += direction * CellSize * 0.5;

          if (LineIntersectsVoxel(ref worldCurrent, ref worldNext, RootVoxel))
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

    HashSet<Vector3I> _positionsRemoved = new HashSet<Vector3I>();
    void ApplyVoxelChanges()
    {
      try
      {
        //AiSession.Instance.Logger.Log($"{this}.ApplyVoxelChanges: Start");

        _positionsRemoved.Clear();
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
            if (OpenTileDict.TryGetValue(current, out node))
            {
              if (node != null)
              {
                AiSession.Instance.NodeStack?.Push(node);
              }

              OpenTileDict.Remove(current);
            }

            KeyValuePair<IMyCubeGrid, bool> kvp;
            ObstacleNodes.TryRemove(current, out kvp);
            _positionsRemoved.Add(current);
          }

          CheckForPlanetTiles(ref mapMin, ref mapMax);
          AiSession.Instance.VoxelUpdateItemStack.Push(updateItem);
        }

        InvokePositionsRemoved(_positionsRemoved);

        //AiSession.Instance.Logger.Log($"{this}.ApplyVoxelChanges: Finished");
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
      OpenTileDict.Clear();
      ObstacleNodes.Clear();
      InvokePositionsRemoved(null);
      MyAPIGateway.Parallel.StartBackground(InitGridArea, SetReady);

      // Testing only
      //InitGridArea();
      //SetReady();
    }

    void InitGridArea()
    {
      try
      {
        //AiSession.Instance.Logger.Log($"{this}.InitGridArea: Start");

        CheckForPlanetTiles(ref BoundingBox.Min, ref BoundingBox.Max);

        //AiSession.Instance.Logger.Log($"{this}.InitGridArea: Finished");
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
        checkForWater = planet != null && WaterAPI.Registered && WaterAPI.HasWater(planet);
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

        OpenTileDict[localPoint] = node;

        if (isGroundNode)
          _groundPoints.Add(node);
      }

      GraphHasTunnel = tunnelCount > 25;

      var upVec = Vector3I.Up;
      foreach (var node in _groundPoints)
      {
        var localAbove = node.Position + upVec;

        Node nAbove;
        if (OpenTileDict.TryGetValue(localAbove, out nAbove) && !nAbove.IsGridNode)
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
      if (OpenTileDict.Count == 0 || (!bot.CanUseAirNodes && RootVoxel == null))
        return false;

      var localBot = WorldToLocal(bot.BotInfo.CurrentBotPositionActual);
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
      if (OpenTileDict.TryGetValue(pos, out node) && node.IsGroundNode)
        return true;

      if (GetClosestSurfacePointFast(LocalToWorld(pos), WorldMatrix.Up, out node))
        return true;

      pos += Vector3I.Up * 10;

      for (int i = 0; i < 20; i++)
      {
        pos += Vector3I.Down;

        if (OpenTileDict.TryGetValue(pos, out node) && node.IsGroundNode)
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

    List<KeyValuePair<IMySlimBlock, Vector3I>> _tempKVPList = new List<KeyValuePair<IMySlimBlock, Vector3I>>();

    void UpdateTempObstaclesAsync(WorkData data)
    {
      //AiSession.Instance.Logger.Log($"{this}.UpdateTempObstaclesAsync: Start");

      var obstacleData = data as ObstacleWorkData;
      if (obstacleData != null && AiSession.Instance?.BlockFaceDictionary != null && AiSession.Instance.Registered)
      {
        ObstacleNodesTemp.Clear();
        var blocks = obstacleData.Blocks;
        _tempKVPList.Clear();

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
          var grid = b.CubeGrid;

          foreach (var kvp in faceDict)
          {
            var cell = kvp.Key;
            Vector3I.TransformNormal(ref cell, ref matrix, out cell);
            var position = adjustedPosition + cell;

            var worldPoint = b.CubeGrid.GridIntegerToWorld(position);
            if (!OBB.Contains(ref worldPoint))
              continue;

            var graphLocal = WorldToLocal(worldPoint);
            if (OpenTileDict.ContainsKey(graphLocal) && !ObstacleNodesTemp.ContainsKey(graphLocal))
            {
              ObstacleNodesTemp[graphLocal] = new KeyValuePair<IMyCubeGrid, bool>(grid, false);
            }

            foreach (var dir in AiSession.Instance.CardinalDirections)
            {
              var otherLocal = graphLocal + dir;

              if (OpenTileDict.ContainsKey(otherLocal) && !ObstacleNodesTemp.ContainsKey(otherLocal))
                //ObstacleNodesTemp[otherLocal] = new KeyValuePair<IMyCubeGrid, bool>(grid, true);
                _tempKVPList.Add(new KeyValuePair<IMySlimBlock, Vector3I>(b, otherLocal));
            }
          }
        }

        foreach (var kvp in _tempKVPList)
        {
          var node = kvp.Value;
          if (!ObstacleNodesTemp.ContainsKey(node))
            ObstacleNodesTemp[node] = new KeyValuePair<IMyCubeGrid, bool>(kvp.Key.CubeGrid, true);
        }
      }

      //AiSession.Instance.Logger.Log($"{this}.UpdateTempObstaclesAsync: Finihsed");
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

        OpenTileDict?.Clear();
        OpenTileDict = null;

        _positionsRemoved?.Clear();
        _positionsRemoved = null;

        _tempKVPList?.Clear();
        _tempKVPList = null;

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
      foreach (var kvp in OpenTileDict)
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
      return OpenTileDict.TryGetValue(position, out node) && node != null;
    }

    public override bool IsOpenTile(Vector3I position)
    {
      return OpenTileDict.ContainsKey(position);
    }

    public override bool IsObstacle(Vector3I position, BotBase bot, bool includeTemp)
    {
      bool result = bot?._pathCollection != null && bot._pathCollection.Obstacles.ContainsKey(position);

      if (!includeTemp)
        return result;

      return result || ObstacleNodes.ContainsKey(position) || TempBlockedNodes.ContainsKey(position);
    }

    public override Node GetValueOrDefault(Vector3I position, Node defaultValue)
    {
      Node node;
      if (OpenTileDict.TryGetValue(position, out node))
        return node;

      return defaultValue;
    }

    public override IMySlimBlock GetBlockAtPosition(Vector3I localPosition)
    {
      var worldPosition = LocalToWorld(localPosition);
      var sphere = new BoundingSphereD(worldPosition, 0.25);

      List<MyEntity> entList;
      if (!AiSession.Instance.EntListStack.TryPop(out entList) || entList == null)
        entList = new List<MyEntity>();
      else
        entList.Clear();

      MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entList);

      IMySlimBlock block = null;

      for (int i = 0; i < entList.Count; i++)
      {
        var grid = entList[i] as IMyCubeGrid;
        if (grid == null || grid.MarkedForClose)
          continue;

        var gridLocal = grid.WorldToGridInteger(worldPosition);
        block = grid.GetCubeBlock(gridLocal);

        if (block != null)
          break;
      }

      return block;
    }
  }
}
