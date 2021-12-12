using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AiEnabled.API;
using AiEnabled.Bots;
using AiEnabled.Utilities;

using Jakaria.API;

using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.WorldEnvironment;
using Sandbox.ModAPI;

using VRage.Collections;
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
    public const int DefaultHalfSize = 50;

    /// <summary>
    /// The default cell size for a voxel grid map
    /// </summary>
    public const float DefaultCellSize = 1.25f;

    public MatrixD MatrixNormalizedInv;
    float _gridSizeR;
    Vector3D _upVector;

    public override float CellSize { get; internal set; } = DefaultCellSize;

    public VoxelGridMap(Vector3D botStart)
    {
      _upVector = Vector3D.Up;
      Vector3D fwd = Vector3D.Forward;
      IsGridGraph = false;

      var vec = new Vector3I(DefaultHalfSize);
      BoundingBox = new BoundingBoxI(-vec, vec);
      MyPlanet checkPlanet = null;

      float _;
      var gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(botStart, out _);
      if (gravity.LengthSquared() > 0)
      {
        _upVector = Vector3D.Normalize(-gravity);
        fwd = Vector3D.CalculatePerpendicularVector(_upVector);

        checkPlanet = MyGamePruningStructure.GetClosestPlanet(botStart);
      }
      else
      {
        checkPlanet = null;
        CellSize *= 2;
        vec /= 2;
        BoundingBox = new BoundingBoxI(-vec, vec);
      }

      WorldMatrix = MatrixD.CreateWorld(botStart, fwd, _upVector);
      MatrixNormalizedInv = MatrixD.Normalize(MatrixD.Invert(WorldMatrix));

      if (checkPlanet != null)
      {
        var tuple = checkPlanet.GetVoxelContentInBoundingBox_Fast(BoundingBox, WorldMatrix, true);

        if (tuple.Item1 == 0 && tuple.Item2 == 0)
        {
          checkPlanet = null;
          CellSize *= 2;
          vec /= 2;
          BoundingBox = new BoundingBoxI(-vec, vec);
        }
        else
        {
          checkPlanet.RangeChanged += Planet_RangeChanged;
          checkPlanet.OnMarkForClose += Planet_OnMarkForClose;
        }
      }

      Planet = checkPlanet;
      _gridSizeR = 1f / CellSize;
      Vector3D worldCenter;

      if (Planet != null)
      {
        Vector3I voxelCoord;
        MyVoxelCoordSystems.WorldPositionToVoxelCoord(Planet.PositionComp.WorldMatrixNormalizedInv, Vector3D.Zero, Planet.SizeInMetresHalf, ref botStart, out voxelCoord);
        MyVoxelCoordSystems.VoxelCoordToWorldPosition(Planet.WorldMatrix, Vector3D.Zero, Planet.SizeInMetresHalf, ref voxelCoord, out worldCenter);
      }
      else
      {
        var localVec = WorldToLocal(botStart);
        worldCenter = LocalToWorld(localVec);
      }

      var hExtents = (Vector3D)BoundingBox.HalfExtents + Vector3D.Half;
      OBB = new MyOrientedBoundingBoxD(worldCenter, hExtents * CellSize, Quaternion.CreateFromRotationMatrix(WorldMatrix));
 
      Init();
    }

    public override bool IsPositionValid(Vector3D position)
    {
      if (OBB.Contains(ref position))
        return !PointInsideVoxel(position, Planet);

      return false;
    }

    public override bool GetClosestValidNode(BotBase bot, Vector3I testNode, out Vector3I node, Vector3D? up = null, bool isSlimBlock = false, bool currentIsDenied = false)
    {
      node = testNode;
      if (!currentIsDenied && OpenTileDict.ContainsKey(node) && !Obstacles.ContainsKey(node) && !ObstacleNodes.ContainsKey(node))
      {
        return true;
      }

      var center = node;
      double localDistance = double.MaxValue;
      var worldPosition = LocalToWorld(testNode);

      foreach (var point in Neighbors(bot, center, center, worldPosition, false, currentIsObstacle: true, up: up))
      {
        var testPosition = LocalToWorld(point);
        var dist = Vector3D.DistanceSquared(testPosition, worldPosition);

        if (dist < localDistance)
        {
          localDistance = dist;
          node = point;
        }
      }

      return localDistance < double.MaxValue;
    }

    public override IEnumerable<Vector3I> GetBlockedNodeEdges(NodeBase nodeBase)
    {
      // not used here
      yield break;
    }

    List<Vector3I> _localNodes = new List<Vector3I>();
    public override bool GetRandomNodeNearby(BotBase bot, Vector3D targetPosition, out Vector3I node)
    {
      var botPosition = bot.Position;
      var botWorldMatrix = bot.WorldMatrix;

      node = WorldToLocal(botPosition);
      var upDir = WorldMatrix.GetClosestDirection(botWorldMatrix.Up);
      var intVec = Base6Directions.GetIntVector(upDir);

      if (!OpenTileDict.ContainsKey(node))
        node -= intVec;

      _localNodes.Clear();
      foreach (var dir in AiSession.Instance.CardinalDirections)
      {
        if (dir.Dot(ref intVec) != 0)
          continue;

        Node n;
        var newNode = node + dir;

        if (!OpenTileDict.ContainsKey(newNode) || Obstacles.ContainsKey(newNode) || !OpenTileDict.TryGetValue(node, out n) || n.BlockedEdges?.Contains(dir) == true)
          continue;

        _localNodes.Add(newNode);
      }

      foreach (var dir in AiSession.Instance.DiagonalDirections)
      {
        var dir1 = new Vector3I(dir.X, 0, 0);
        var dir2 = new Vector3I(0, 0, dir.Z);

        var node1 = node + dir1;
        var node2 = node + dir2;
        var newNode = node + dir;

        if (!OpenTileDict.ContainsKey(newNode) || Obstacles.ContainsKey(newNode)
          || !OpenTileDict.ContainsKey(node1) || Obstacles.ContainsKey(node1)
          || !OpenTileDict.ContainsKey(node2) || Obstacles.ContainsKey(node2))
          continue;

        Node n;
        if (!OpenTileDict.TryGetValue(node, out n) || (n.BlockedEdges != null && (n.BlockedEdges.Contains(dir1) || n.BlockedEdges.Contains(dir2))))
          continue;

        _localNodes.Add(newNode);
      }

      if (_localNodes.Count > 0)
      {
        var rnd = MyUtils.GetRandomInt(0, _localNodes.Count);
        node = _localNodes[rnd];
        return true;
      }

      return false;
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
      if (!OpenTileDict.TryGetValue(nextNode, out nNext) || nNext == null)
      {
        return false;
      }

      if ((nNext.IsAirNode && !bot._canUseAirNodes) || (nNext.IsSpaceNode && !bot._canUseSpaceNodes) || (nNext.IsWaterNode && !bot._canUseWaterNodes))
      {
        return false;
      }

      if (Planet != null && Environment.CurrentManagedThreadId == AiSession.MainThreadId)
      {
        using (Planet.Pin())
        {
          Node nCur;
          OpenTileDict.TryGetValue(currentNode, out nCur);
          Vector3D worldCurrent = nCur?.SurfacePosition ?? LocalToWorld(currentNode);
          Vector3D worldNext = nNext.SurfacePosition ?? LocalToWorld(nextNode);

          Vector3D? _;
          var line = new LineD(worldCurrent, worldNext);
          if (Planet.RootVoxel.GetIntersectionWithLine(ref line, out _))
            return false;
        }
      }

      if (nNext.BlockedEdges == null)
      {
        return true;
      }

      var dirVec = (currentIsObstacle) ? currentNode - nextNode : nextNode - currentNode;
      return !nNext.BlockedEdges.Contains(dirVec);
    }

    public override void Refresh() => Init();

    BoundingBoxI? _pendingChanges;
    private void Planet_RangeChanged(MyVoxelBase storage, Vector3I minVoxelChanged, Vector3I maxVoxelChanged, MyStorageDataTypeFlags changedData)
    {
      var min = minVoxelChanged;
      var max = maxVoxelChanged;

      if (_pendingChanges.HasValue)
      {
        min = Vector3I.Min(min, _pendingChanges.Value.Min);
        max = Vector3I.Max(max, _pendingChanges.Value.Max);
      }

      _pendingChanges = new BoundingBoxI(min, max);
      NeedsVoxelUpate = true;
    }

    public void UpdateVoxels()
    {
      if (Planet == null || Planet.MarkedForClose || !Ready)
        return;

      NeedsVoxelUpate = false;

      if (_pendingChanges.HasValue)
      {
        Ready = false;
        MyAPIGateway.Parallel.Start(ApplyVoxelChanges, SetReady);
      }
    }

    void ApplyVoxelChanges()
    {
      var minWorld = Vector3D.Transform(_pendingChanges.Value.Min - Planet.SizeInMetresHalf, Planet.WorldMatrix);
      var maxWorld = Vector3D.Transform(_pendingChanges.Value.Max - Planet.SizeInMetresHalf, Planet.WorldMatrix);
      _pendingChanges = null;

      if (!OBB.Contains(ref minWorld) && !OBB.Contains(ref maxWorld))
        return;

      var mapMin = Vector3I.Max(BoundingBox.Min, WorldToLocal(minWorld) - 3);
      var mapMax = Vector3I.Min(BoundingBox.Max, WorldToLocal(maxWorld) + 3);

      var iter = new Vector3I_RangeIterator(ref mapMin, ref mapMax);

      while (iter.IsValid())
      {
        var current = iter.Current;
        iter.MoveNext();

        Node n;
        byte b;

        OpenTileDict.TryRemove(current, out n);
        ObstacleNodes.TryRemove(current, out b);
        Obstacles.TryRemove(current, out b);
      }

      CheckForPlanetTiles(ref mapMin, ref mapMax);
    }

    private void Planet_OnMarkForClose(MyEntity obj)
    {
      if (Planet != null)
      {
        Planet.OnMarkForClose -= Planet_OnMarkForClose;
        Planet.RangeChanged -= Planet_RangeChanged;
      }
    }

    internal override void Init()
    {
      if (_locked)
        return;

      _locked = true;
      Ready = false;
      Dirty = false;
      OpenTileDict.Clear();
      ObstacleNodes.Clear();
      Obstacles.Clear();
      MyAPIGateway.Parallel.StartBackground(InitGridArea, SetReady);
    }

    void InitGridArea()
    {
      try
      {
        //AiSession.Instance.Logger.Log($"Voxel.InitGridArea starting");

        CheckForPlanetTiles(ref BoundingBox.Min, ref BoundingBox.Max);
        UpdateTempObstacles();

        //AiSession.Instance.Logger.Log($"Voxel.InitGridArea finished");
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in InitGridArea: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
        throw;
      }
    }

    HashSet<NodeBase> _groundPoints = new HashSet<NodeBase>();
    void CheckForPlanetTiles(ref Vector3I min, ref Vector3I max)
    {
      bool checkForVoxel = false;
      bool checkForWater = false;

      if (Planet != null && !Planet.MarkedForClose)
      {
        checkForWater = WaterAPI.Registered && WaterAPI.HasWater(Planet.EntityId);
        checkForVoxel = true;
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

        bool isSpaceNode;
        bool isAirNode = true;
        bool isGroundNode = false;
        bool isTunnelNode = false;

        // worldPoint = LocalToWorld(localPoint);
        Vector3D worldPoint = localPoint * cellSize;
        Vector3D.Transform(ref worldPoint, ref worldMatrix, out worldPoint);
        var groundPoint = worldPoint;

        if (checkForVoxel)
        {
          if (Planet == null || Planet.MarkedForClose)
            checkForVoxel = false;

          if (PointInsideVoxel(worldPoint, Planet))
            continue;

          isSpaceNode = false;

          var pointBelow = worldPoint + gravityNorm * cellSize;
          if (PointInsideVoxel(pointBelow, Planet))
          {
            isAirNode = false;
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
              if (PointInsideVoxel(mid, Planet))
              {
                pointInVoxel = mid;
              }
              else
              {
                pointInAir = mid;
              }
            }

            groundPoint = pointInAir - gravityNorm;

            var surfacePoint = Planet.GetClosestSurfacePointGlobal(groundPoint) - gravityNorm;

            while (PointInsideVoxel(surfacePoint, Planet))
              surfacePoint -= gravityNorm;

            var vector = groundPoint - surfacePoint;
            if (vector.LengthSquared() > 9 && vector.Dot(worldMatrix.Down) > 0)
            {
              var line = new LineD(surfacePoint, groundPoint);
              var lerpAmount = MathHelper.Clamp(1 / line.Length, 0, 1);
              var point = Vector3D.Lerp(line.From, line.To, lerpAmount);
              var testAmount = MathHelper.Clamp(lerpAmount * 2, 0, 1);

              int testCount = 0;
              int maxCount = (int)Math.Ceiling(line.Length) + 1;

              while (Vector3D.DistanceSquared(point, line.To) > 9)
              {
                if (PointInsideVoxel(point, Planet))
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
        else
        {
          float _;
          var gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(worldPoint, out _);
          isSpaceNode = gravity.LengthSquared() <= 0;
        }

        var node = new Node(localPoint, null, null, groundPoint)
        {
          IsAirNode = isAirNode,
          IsSpaceNode = isSpaceNode,
          IsGroundNode = isGroundNode,
          IsTunnelNode = isTunnelNode,
          IsWaterNode = checkForWater && WaterAPI.IsUnderwater(groundPoint),
          BlockedEdges = checkForVoxel && !isAirNode ? new List<Vector3I>(blockedVoxelEdges) : null,
        };

        OpenTileDict[localPoint] = node;

        if (isGroundNode)
          _groundPoints.Add(node);
      }

      _graphHasTunnel = tunnelCount > 25;

      var upVec = Vector3I.Up;
      foreach (var node in _groundPoints)
      {
        var localAbove = node.Position + upVec;

        Node n;
        if (OpenTileDict.TryGetValue(localAbove, out n))
        {
          n.IsAirNode = false;
          n.IsGroundNode = true;
          n.SurfacePosition = node.SurfacePosition;
        }
      }

      _groundPoints.Clear();
      //AiSession.Instance.Logger.Log($"VoxelGraph.CheckPlanetTiles: Has Tunnel = {_graphHasTunnel} ({tunnelCount} tunnel nodes)");
    }

    public override bool GetRandomOpenNode(BotBase bot, Vector3D requestedPosition, out Node node)
    {
      node = null;
      if (OpenTileDict.Count == 0)
        return false;

      var localBot = WorldToLocal(bot.Position);
      var botPosition = LocalToWorld(localBot);

      requestedPosition = GetClosestSurfacePointFast(bot, requestedPosition, WorldMatrix.Up);
      var localReq = WorldToLocal(requestedPosition);
      requestedPosition = LocalToWorld(localReq);

      botPosition = Vector3D.Transform(botPosition, MatrixNormalizedInv);
      requestedPosition = Vector3D.Transform(requestedPosition, MatrixNormalizedInv);

      NodeList.Clear();
      MyCubeGrid.RayCastStaticCells(botPosition, requestedPosition, NodeList, CellSize, BoundingBox.HalfExtents);

      for (int i = NodeList.Count - 1; i >= 0; i--)
      {
        var localPosition = NodeList[i];
        if (IsPositionUsable(bot, LocalToWorld(localPosition), out node))
        {
          break;
        }
      }

      NodeList.Clear();
      return node != null;
    }

    List<MyEntity> _tempEntities = new List<MyEntity>();
    //List<MyEntity> _tempEntitiesGrid = new List<MyEntity>();
    public override void UpdateTempObstacles()
    {
      //AiSession.Instance.Logger.Log($"UpdateTempObstacles started");

      lock (_tempEntities)
      {
        _tempEntities.Clear();
        ObstacleNodesTemp.Clear();
        var sphere = new BoundingSphereD(OBB.Center, OBB.HalfExtent.AbsMax());
        MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, _tempEntities);

        for (int i = _tempEntities.Count - 1; i >= 0; i--)
        {
          var grid = _tempEntities[i] as MyCubeGrid;
          if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose)
            continue;

          if (grid.IsStatic && grid.GridSizeEnum == VRage.Game.MyCubeSize.Large && grid.BlocksCount > 5)
            continue;

          var orientation = Quaternion.CreateFromRotationMatrix(grid.WorldMatrix);
          var obb = new MyOrientedBoundingBoxD(grid.PositionComp.WorldAABB.Center, grid.PositionComp.LocalAABB.HalfExtents, orientation);

          var containType = OBB.Contains(ref obb);
          if (containType == ContainmentType.Disjoint)
            continue;

          BoundingBoxI box = BoundingBoxI.CreateInvalid();
          obb.GetCorners(_corners1, 0);
          for (int j = 0; j < _corners1.Length; j++)
          {
            var localCorner = WorldToLocal(_corners1[j]);
            box.Include(localCorner);
          }

          if (containType == ContainmentType.Intersects)
          {
            BoundingBoxI otherBox = BoundingBoxI.CreateInvalid();
            OBB.GetCorners(_corners1, 0);
            for (int j = 0; j < _corners1.Length; j++)
            {
              var localCorner = WorldToLocal(_corners1[j]);
              otherBox.Include(localCorner);
            }

            box.IntersectWith(ref otherBox);

            var boxCenter = grid.GridIntegerToWorld(box.Center);
            obb = new MyOrientedBoundingBoxD(boxCenter, box.HalfExtents, orientation);
          }

          Vector3I_RangeIterator iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);

          while (iter.IsValid())
          {
            var graphLocal = iter.Current;
            iter.MoveNext();

            if (!OpenTileDict.ContainsKey(graphLocal) || ObstacleNodesTemp.ContainsKey(graphLocal) || Obstacles.ContainsKey(graphLocal))
            {
              continue;
            }

            var worldPoint = LocalToWorld(graphLocal);
            if (obb.Contains(ref worldPoint))
              ObstacleNodesTemp[graphLocal] = new byte();
          }
        }

        Interlocked.CompareExchange(ref ObstacleNodes, ObstacleNodesTemp, ObstacleNodes);
        _tempEntities.Clear();
      }
    }

    public override void Close()
    {
      try
      {
        _tempEntities?.Clear();
        _localNodes?.Clear();
        _groundPoints?.Clear();

        _tempEntities = null;
        _localNodes = null;
        _groundPoints = null;
      }
      catch(Exception ex)
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
      var surfacePoint = GetClosestSurfacePointFast(bot, OBB.Center, WorldMatrix.Up);
      var localSurface = Vector3D.Transform(surfacePoint, MatrixNormalizedInv);
      var localCenter = Vector3D.Transform(OBB.Center, MatrixNormalizedInv);

      _localNodes.Clear();
      MyCubeGrid.RayCastStaticCells(localSurface, localCenter, _localNodes, CellSize, BoundingBox.HalfExtents);
      Node node;

      for (int i = 0; i < _localNodes.Count; i++)
      {
        var localPosition = _localNodes[i];

        if (IsPositionUsable(bot, LocalToWorld(localPosition), out node))
          return node;

        if (GetClosestValidNode(bot, localPosition, out localPosition) && OpenTileDict.TryGetValue(localPosition, out node))
          return node;
      }

      while (PointInsideVoxel(surfacePoint, Planet))
        surfacePoint += WorldMatrix.Up * 0.1;

      if (IsPositionUsable(bot, surfacePoint, out node))
      {
        return node;
      }

      var localNode = WorldToLocal(surfacePoint);
      return new Node(localNode, null, null, surfacePoint + WorldMatrix.Up);
    }
  }
}
