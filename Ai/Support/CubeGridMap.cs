using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.WorldEnvironment;
using Sandbox.ModAPI;

using SpaceEngineers.Game.ModAPI;

using VRage;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.Utils;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

using VRageMath;
using AiEnabled.Support;
using AiEnabled.Utilities;
using VRage.Collections;
using AiEnabled.Bots;
using VRage.ModAPI;
using System.Threading;
using VRage.Voxels;
using AiEnabled.API;
using Jakaria.API;

namespace AiEnabled.Ai.Support
{
  public class CubeGridMap : GridBase
  {
    /// <summary>
    /// The cube grid this map refers to
    /// </summary>
    public MyCubeGrid Grid { get; protected set; }

    /// <summary>
    /// The cell size (in meters) for the map
    /// </summary>
    public override float CellSize => Grid?.GridSize ?? 2.5f;

    /// <summary>
    /// The smallest oriented bounding box that fits the grid
    /// </summary>
    public MyOrientedBoundingBoxD UnbufferedOBB { get; private set; }

    /// <summary>
    /// This will be true when the grid has moved more than 1m from its previous position
    /// </summary>
    public bool HasMoved { get; private set; }

    /// <summary>
    /// If true, planet tiles have already been cleared from the dictionary
    /// </summary>
    public bool PlanetTilesRemoved { get; private set; }

    /// <summary>
    /// Contains information about the components stored in the grid's inventories
    /// </summary>
    public InventoryCache InventoryCache { get; private set; }

    /// <summary>
    /// If repair bots are present on the grid, this will hold any tiles they are actively reparing or building.
    /// Key is bot entity id, Value is block position on grid.
    /// </summary>
    public ConcurrentDictionary<long, Vector3I> SelectedRepairTiles = new ConcurrentDictionary<long, Vector3I>();

    /// <summary>
    /// Doors that are currently closed and non-functional will be in this collection
    /// </summary>
    public ConcurrentDictionary<Vector3I, IMyDoor> BlockedDoors = new ConcurrentDictionary<Vector3I, IMyDoor>(Vector3I.Comparer);

    /// <summary>
    /// These are nodes that correspond to blocks above slopes and ramps that are pre-checked for pathfinding validity.
    /// No need to check them again for blocked edges.
    /// </summary>
    public HashSet<Vector3I> ExemptNodesUpper = new HashSet<Vector3I>(Vector3I.Comparer);

    /// <summary>
    /// These are nodes that correspond to blocks to the side of slopes and ramps that are pre-checked for pathfinding validity.
    /// No need to check them again for blocked edges.
    /// </summary>
    public HashSet<Vector3I> ExemptNodesSide = new HashSet<Vector3I>(Vector3I.Comparer);

    /// <summary>
    /// Used for determining when a boss encounter spawns
    /// </summary>
    public int BossSpawnChance = 100;

    /// <summary>
    /// The number of bots spawned on this grid
    /// </summary>
    public int TotalSpawnCount = 0;

    Vector3D _worldPosition;
    List<IMySlimBlock> _blocks = new List<IMySlimBlock>();
    List<MyEntity> _closeList = new List<MyEntity>();
    int _boxExpansion;

    public CubeGridMap(MyCubeGrid grid, MatrixD spawnBlockMatrix)
    {
      _boxExpansion = grid.Physics.IsStatic ? 10 : 5;
      _worldPosition = grid.WorldMatrix.Translation;
      Grid = grid;
      InventoryCache = new InventoryCache(grid);
      InventoryCache.Update(false);

      var hExtents = grid.PositionComp.LocalAABB.HalfExtents + (new Vector3I(11) * grid.GridSize);
      OBB = new MyOrientedBoundingBoxD(grid.PositionComp.WorldAABB.Center, hExtents, Quaternion.CreateFromRotationMatrix(grid.WorldMatrix));

      float _;
      var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out _);
      var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(OBB.Center, 0);
      if (aGrav.LengthSquared() > 0)
      {
        var up = -Vector3D.Normalize(aGrav);
        var fwd = Vector3D.CalculatePerpendicularVector(up);

        var dir = grid.WorldMatrix.GetClosestDirection(up);
        up = grid.WorldMatrix.GetDirectionVector(dir);

        dir = grid.WorldMatrix.GetClosestDirection(fwd);
        fwd = grid.WorldMatrix.GetDirectionVector(dir);

        WorldMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
      }
      else if (nGrav.LengthSquared() > 0)
      {
        var up = -Vector3D.Normalize(nGrav);
        var fwd = Vector3D.CalculatePerpendicularVector(up);

        var dir = grid.WorldMatrix.GetClosestDirection(up);
        up = grid.WorldMatrix.GetDirectionVector(dir);

        dir = grid.WorldMatrix.GetClosestDirection(fwd);
        fwd = grid.WorldMatrix.GetDirectionVector(dir);

        WorldMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
      }
      else
        WorldMatrix = spawnBlockMatrix;

      HookEvents();
      Init();
    }

    void HookEvents()
    {
      Grid.OnBlockAdded += MarkDirty;
      Grid.OnBlockRemoved += MarkDirty;
      Grid.OnGridSplit += OnGridSplit;
      Grid.OnMarkForClose += CloseGrid;
      Grid.PositionComp.OnPositionChanged += OnGridPositionChanged;
    }

    private void OnGridPositionChanged(VRage.Game.Components.MyPositionComponentBase obj)
    {
      if (!HasMoved)
      {
        var pos = Grid.WorldMatrix.Translation;
        if (Vector3D.DistanceSquared(_worldPosition, pos) < 1)
          return;

        HasMoved = true;
      }
    }

    /// <summary>
    /// Checks if a given position is being repaired by a different repair bot
    /// </summary>
    /// <param name="position">Block position on the grid</param>
    /// <param name="botId">Entity Id of the asking repair bot</param>
    /// <returns>true if a DIFFERENT bot is currently planning to repair the given block, otherwise false</returns>
    public bool IsTileBeingRepaired(Vector3I position, long botId)
    {
      foreach (var kvp in SelectedRepairTiles)
      {
        if (kvp.Value == position)
          return kvp.Key != botId;
      }

      return false;
    }

    public void ResetMovement()
    {
      _worldPosition = Grid.WorldMatrix.Translation;
      HasMoved = false;

      var aabb = OBB.GetAABB();
      if (MyGamePruningStructure.AnyVoxelMapInBox(ref aabb))
        Dirty = true;
    }

    private void CloseGrid(MyEntity obj)
    {
      Ready = false;
      Dirty = true;

      if (Grid == null)
      {
        IsGridGraph = false;
        InventoryCache?.Close();
        _closeList?.Clear();
        _tempEntities?.Clear();
        _blocks?.Clear();
        return;
      }

      Grid.OnBlockAdded -= MarkDirty;
      Grid.OnBlockRemoved -= MarkDirty;
      Grid.OnGridSplit -= OnGridSplit;
      Grid.OnMarkForClose -= CloseGrid;
      Grid.PositionComp.OnPositionChanged -= OnGridPositionChanged;

      var position = Grid.PositionComp.WorldAABB.Center;
      var sphere = new BoundingSphereD(position, 1);

      _closeList.Clear();
      MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, _closeList);

      for (int i = _closeList.Count - 1; i >= 0; i--)
      {
        var tempGrid = _closeList[i] as MyCubeGrid;
        if (tempGrid == null || tempGrid == Grid)
          continue;

        if (!tempGrid.MarkedForClose)
        {
          SetGrid(tempGrid, WorldMatrix);
          return;
        }
      }

      IsGridGraph = false;
      InventoryCache?.Close();
      _closeList?.Clear();
      _tempEntities?.Clear();
      _blocks?.Clear();
      _additionalMaps?.Clear();
      _additionalMaps2?.Clear();
    }

    public void RemovePlanetTiles()
    {
      PlanetTilesRemoved = true;
      Ready = false;
      _locked = true;
      MyAPIGateway.Parallel.Start(RemovePlanetTilesAsync, SetReady);
    }

    void RemovePlanetTilesAsync()
    {
      NodeList.Clear();
      foreach (var kvp in OpenTileDict)
      {
        if (kvp.Value.IsGridNodePlanetTile)
          NodeList.Add(kvp.Key);
      }

      foreach (var node in NodeList)
        OpenTileDict.Remove(node);

      NodeList.Clear();

      float _;
      var gravityNorm = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out _);
      if (gravityNorm.LengthSquared() > 0)
        gravityNorm.Normalize();
      else
        gravityNorm = WorldMatrix.Down;

      var upDir = Grid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
      var upVec = Base6Directions.GetIntVector(upDir);

      CheckForPlanetTiles(ref BoundingBox, ref gravityNorm, ref upVec);
    }

    public void SetGrid(MyCubeGrid grid, MatrixD worldMatrix)
    {
      Dirty = true;
      WorldMatrix = worldMatrix;
      Grid = grid;
      HookEvents();
      Init();
    }

    private void MarkDirty(IMySlimBlock obj)
    {
      Dirty = true;
    }

    private void OnGridSplit(MyCubeGrid originalGrid, MyCubeGrid newGrid)
    {
      Dirty = true;
    }

    public bool IsInBufferZone(Vector3D botPosition)
    {
      return OBB.Contains(ref botPosition) && !UnbufferedOBB.Contains(ref botPosition);
    }

    public override void Refresh() => Init();

    internal override void Init()
    {
      if (_locked)
        return;

      _locked = true;
      Ready = false;
      Dirty = false;
      ObstacleNodes.Clear();
      Obstacles.Clear();
      BlockedDoors.Clear();
      OptimizedCache.Clear();
      OpenTileDict.Clear();
      ExemptNodesUpper.Clear();
      ExemptNodesSide.Clear();
      IsGridGraph = Grid != null;

      if (Grid != null && !Grid.MarkedForClose)
        MyAPIGateway.Parallel.StartBackground(InitGridArea, SetReady);
    }

    public override Vector3D? GetBufferZoneTargetPosition(Vector3D fromPosition, Vector3D toPosition, bool getEdgePoint = false)
    {
      if (IsInBufferZone(fromPosition))
        return fromPosition;

      var line = new LineD(fromPosition, toPosition);
      var num = OBB.Intersects(ref line);
      if (!num.HasValue)
      {
        return null;
      }

      var point = fromPosition + line.Direction * num.Value;

      if (!getEdgePoint)
        point += line.Direction * CellSize * 5;

      return point;
    }

    public override Vector3D? GetBufferZoneTargetPositionCentered(Vector3D fromPosition, Vector3D toPosition, Vector3D sideNormal, bool getEdgePoint = false)
    {
      if (IsInBufferZone(fromPosition))
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


    public override Vector3D LocalToWorld(Vector3I localVector) => Grid?.GridIntegerToWorld(localVector) ?? Vector3D.PositiveInfinity;

    public override Vector3I WorldToLocal(Vector3D worldVector) => Grid?.WorldToGridInteger(worldVector) ?? Vector3I.MaxValue;

    public override bool InBounds(Vector3I node) => BoundingBox.Contains(node) != ContainmentType.Disjoint;

    public override bool GetClosestValidNode(BotBase bot, Vector3I testNode, out Vector3I node, Vector3D? up = null, bool isSlimBlock = false, bool currentIsDenied = false)
    {
      node = testNode;
      Node n;
      if (!currentIsDenied && !BlockedDoors.ContainsKey(node) && !ObstacleNodes.ContainsKey(node) && !TempBlockedNodes.ContainsKey(node) && OpenTileDict.TryGetValue(node, out n) && n != null)
      {
        if ((!n.IsAirNode || bot._canUseAirNodes) && (!n.IsWaterNode || bot._canUseWaterNodes) && (!n.IsSpaceNode || bot._canUseSpaceNodes))
          return true;
      }

      var center = node;
      double localDistance = double.MaxValue;
      var worldPosition = Grid.GridIntegerToWorld(node);

      foreach (var point in Neighbors(bot, center, center, worldPosition, true, true, isSlimBlock, up))
      {
        var testPosition = Grid.GridIntegerToWorld(point);
        var dist = Vector3D.DistanceSquared(testPosition, worldPosition);

        if (dist < localDistance)
        {
          localDistance = dist;
          node = point;
        }
      }

      bool valid = OpenTileDict.TryGetValue(node, out n) && !BlockedDoors.ContainsKey(node) && !ObstacleNodes.ContainsKey(node) && !TempBlockedNodes.ContainsKey(node);
      return valid && n != null && (!n.IsAirNode || bot._canUseAirNodes) && (!n.IsWaterNode || bot._canUseWaterNodes) && (!n.IsSpaceNode || bot._canUseSpaceNodes);
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
        if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, checkDoors, currentIsObstacle, isSlimBlock))
        {
          yield return next;
        }
      }

      if (isSlimBlock)
        yield break;

      Node n;
      bool isVoxelNode = OpenTileDict.TryGetValue(currentNode, out n) && (!n.IsGridNode || n.IsGridNodePlanetTile);
      if (isVoxelNode || currentIsObstacle)
      {
        foreach (var dir in AiSession.Instance.DiagonalDirections)
        {
          if (dir.Dot(ref upVec) != 0)
          {
            continue;
          }

          var next = currentNode + dir;
          if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, checkDoors, currentIsObstacle))
          {
            yield return next;
          }
        }
      }

      if (!isVoxelNode || currentIsObstacle)
        yield break;

      foreach (var dir in AiSession.Instance.VoxelMovementDirections)
      {
        if (dir.Dot(ref upVec) != 0)
        {
          continue;
        }

        var next = currentNode + dir;
        if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, checkDoors))
        {
          yield return next;
        }
      }
    }

    public override bool Passable(BotBase bot, Vector3I previousNode, Vector3I currentNode, Vector3I nextNode, Vector3D worldPosition, bool checkDoors, bool currentIsObstacle = false, bool isSlimBlock = false)
    {
      if (checkDoors && BlockedDoors.ContainsKey(nextNode))
      {
        return false;
      }

      if (ObstacleNodes.ContainsKey(nextNode) || TempBlockedNodes.ContainsKey(nextNode))
      {
        return false;
      }

      Node nNext;
      if (!OpenTileDict.TryGetValue(nextNode, out nNext))
      {
        return false;
      }

      if ((nNext.IsAirNode && !bot._canUseAirNodes) || (nNext.IsSpaceNode && !bot._canUseSpaceNodes) || (nNext.IsWaterNode && !bot._canUseWaterNodes))
      {
        return false;
      }

      if (isSlimBlock)
      {
        if (TempBlockedNodes.ContainsKey(currentNode) || Grid.CubeExists(nextNode))
        {
          return false;
        }

        if (Planet != null && Environment.CurrentManagedThreadId == AiSession.MainThreadId)
        {
          using (Planet.Pin())
          {
            Node n;
            OpenTileDict.TryGetValue(currentNode, out n);
            Vector3D worldCurrent = n?.SurfacePosition ?? Grid.GridIntegerToWorld(currentNode);
            Vector3D worldNext = nNext?.SurfacePosition ?? Grid.GridIntegerToWorld(nextNode);
            var direction = Vector3D.Normalize(worldNext - worldCurrent);
            worldCurrent += direction * CellSize * 0.5;
            var line = new LineD(worldCurrent, worldNext);

            Vector3D? hit;
            if (Planet.RootVoxel.GetIntersectionWithLine(ref line, out hit))
              return false;
          }
        }

        return true;
      }

      if (currentIsObstacle)
      {
        var castTo = LocalToWorld(nextNode);

        IHitInfo hitInfo;
        MyAPIGateway.Physics.CastRay(worldPosition, castTo, out hitInfo, CollisionLayers.DefaultCollisionLayer);
        if (hitInfo?.HitEntity?.GetTopMostParent() is MyCubeGrid)
        {
          return false;
        }

        return true;
      }

      if (nNext.BlockedEdges?.Contains(currentNode - nextNode) == true)
      {
        return false;
      }

      Node nCur;
      if (!OpenTileDict.TryGetValue(currentNode, out nCur) || nCur.BlockedEdges?.Contains(nextNode - currentNode) == true)
      {
        return false;
      }

      if (Planet != null && Environment.CurrentManagedThreadId == AiSession.MainThreadId)
      {
        using (Planet.Pin())
        {
          Vector3D worldCurrent = nCur.SurfacePosition ?? Grid.GridIntegerToWorld(currentNode);
          Vector3D worldNext = nNext.SurfacePosition ?? Grid.GridIntegerToWorld(nextNode);
          var line = new LineD(worldCurrent, worldNext);

          Vector3D? hit;
          if (Planet.RootVoxel.GetIntersectionWithLine(ref line, out hit))
            return false;
        }
      }

      Vector3 totalMovement = nextNode - previousNode;
      Vector3 botUpVec = Base6Directions.GetIntVector(Grid.WorldMatrix.GetClosestDirection(WorldMatrix.Up));

      var checkVec = Vector3I.Round(Vector3.ProjectOnVector(ref totalMovement, ref botUpVec));
      var checkTotal = checkVec.RectangularLength();

      if (checkTotal > 1)
      {
        if (!nCur.IsGridNode && nNext.IsGridNode)
        {
          return false;
        }

        IMySlimBlock prevBlock = Grid.GetCubeBlock(previousNode);
        IMySlimBlock curBlock = Grid.GetCubeBlock(currentNode);
        bool usePrevCur = prevBlock != null && curBlock != null && prevBlock != curBlock;

        IMySlimBlock nextBlock = null;
        bool useCurNext = false;
        if (!usePrevCur || checkTotal < 0)
        {
          nextBlock = Grid.GetCubeBlock(nextNode);
          useCurNext = curBlock != null && nextBlock != null && curBlock != nextBlock;
          usePrevCur = usePrevCur && !useCurNext;
        }

        if (usePrevCur || useCurNext)
        {
          IMySlimBlock from = usePrevCur ? prevBlock : curBlock;
          IMySlimBlock to = usePrevCur ? curBlock : nextBlock;

          if (AiSession.Instance.HalfStairBlockDefinitions.Contains(from.BlockDefinition.Id)
            && AiSession.Instance.HalfStairBlockDefinitions.Contains(to.BlockDefinition.Id))
          {
            Vector3I insertPosition;
            if (!GetValidPositionForStackedStairs(currentNode, out insertPosition))
            {
              return false;
            }

            StackedStairsFound.Enqueue(MyTuple.Create(previousNode, currentNode, insertPosition));
          }
        }
      }
      else if (checkTotal > 0 && !nCur.IsGridNode && nNext.IsGridNode)
      {
        return false;
      }

      return true;
    }

    public bool GetValidPositionForStackedStairs(Vector3I stairPosition, out Vector3I adjusted)
    {
      var botUp = Grid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
      var upVec = Base6Directions.GetIntVector(botUp);
      Vector3I center = stairPosition;

      foreach (var dir in AiSession.Instance.CardinalDirections)
      {
        if (dir.Dot(ref upVec) != 0)
          continue;

        adjusted = center + dir;
        if (OpenTileDict.ContainsKey(adjusted) && !BlockedDoors.ContainsKey(adjusted) 
          && !ObstacleNodes.ContainsKey(adjusted) && !TempBlockedNodes.ContainsKey(adjusted))
        {
          Node n;
          if (OpenTileDict.TryGetValue(center, out n) && n.BlockedEdges?.Contains(dir) != true)
            return true;
        }
      }

      adjusted = Vector3I.MaxValue;
      return false;
    }

    void InitGridArea()
    {
      try
      {
        //AiSession.Instance.Logger.Log($"Grid.InitGridArea starting for {Grid.DisplayName}");

        GridGroups.Clear();
        MyAPIGateway.GridGroups.GetGroup(Grid, GridLinkTypeEnum.Logical, GridGroups);
        Base6Directions.Direction upDir;
        Vector3I upVec;

        BoundingBox = new BoundingBoxI(Grid.Min, Grid.Max);
        for (int i = GridGroups.Count - 1; i >= 0; i--)
        {
          if (Dirty)
            break;

          var connectedGrid = GridGroups[i] as MyCubeGrid;
          if (connectedGrid?.Physics == null || connectedGrid.MarkedForClose)
          {
            GridGroups.RemoveAtFast(i);
            continue;
          }

          upDir = connectedGrid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
          upVec = Base6Directions.GetIntVector(upDir);
          bool connectedIsMain = connectedGrid.EntityId == Grid.EntityId;

          Vector3I min, max;
          if (connectedIsMain)
          {
            min = connectedGrid.Min;
            max = connectedGrid.Max;
          }
          else
          {
            min = Grid.WorldToGridInteger(connectedGrid.GridIntegerToWorld(connectedGrid.Min));
            max = Grid.WorldToGridInteger(connectedGrid.GridIntegerToWorld(connectedGrid.Max));
          }

          BoundingBox.Include(ref min);
          BoundingBox.Include(ref max);

          if (connectedGrid.GridSizeEnum == VRage.Game.MyCubeSize.Small)
          {
            continue;
          }

          _blocks.Clear();
          ((IMyCubeGrid)connectedGrid).GetBlocks(_blocks);

          foreach (var block in _blocks)
          {
            if (Dirty)
              break;

            if (block == null || block.IsDestroyed)
              continue;

            var cubeDef = block.BlockDefinition as MyCubeBlockDefinition;
            if (cubeDef == null || !cubeDef.HasPhysics || block.FatBlock is IMyButtonPanel)
              continue;

            if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(cubeDef.Id))
              continue;

            CheckFaces(block, upVec, cubeDef);

            var position = block.Position;
            var mainGridPosition = connectedIsMain ? position : Grid.WorldToGridInteger(connectedGrid.GridIntegerToWorld(position));

            var door = block.FatBlock as IMyDoor;
            if (door != null)
            {
              if (door.MarkedForClose)
                continue;

              if (!door.Enabled && door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Open)
                Door_EnabledChanged(door);

              door.EnabledChanged -= Door_EnabledChanged;
              door.EnabledChanged += Door_EnabledChanged;
              door.IsWorkingChanged -= Door_EnabledChanged;
              door.IsWorkingChanged += Door_EnabledChanged;
              door.OnDoorStateChanged -= Door_OnDoorStateChanged;
              door.OnDoorStateChanged += Door_OnDoorStateChanged;
              door.OnMarkForClose -= Door_OnMarkForClose;
              door.OnMarkForClose += Door_OnMarkForClose;

              if (door is IMyAirtightHangarDoor) // TODO: Possibly - check block groups for this door and open others with it? Maybe check at open time?
              {
                if (block.Orientation.Up == upDir)
                {
                  for (int j = 1; j < cubeDef.Size.Y; j++)
                  {
                    var newPos = mainGridPosition - upVec * j;
                    OpenTileDict[newPos] = new Node(newPos, connectedGrid, block);
                  }
                }
                else if (block.Orientation.Up == Base6Directions.GetOppositeDirection(upDir))
                {
                  var newPos = mainGridPosition + upVec;
                  OpenTileDict[newPos] = new Node(newPos, connectedGrid, block);
                }
              }
              else if (block.Orientation.Up == upDir)
                OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);
            }
            else if (AiSession.Instance.ArmorPanelAllDefinitions.Contains(cubeDef.Id))
            {
              if (AiSession.Instance.ArmorPanelFullDefinitions.ContainsItem(cubeDef.Id))
              {
                if (upDir == block.Orientation.Left)
                {
                  OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);
                }
              }
              else if (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(cubeDef.Id))
              {
                if (Base6Directions.GetIntVector(block.Orientation.Left).Dot(ref upVec) == 0)
                {
                  OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);
                }
              }
              else if (AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(cubeDef.Id))
              {
                if (Base6Directions.GetIntVector(block.Orientation.Left).Dot(ref upVec) == 0)
                {
                  OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);

                  var newPos = mainGridPosition + upVec;
                  var cubeAbove = connectedGrid.GetCubeBlock(newPos) as IMySlimBlock;
                  if (cubeAbove == null || cubeAbove.BlockDefinition == block.BlockDefinition)
                    OpenTileDict[newPos] = new Node(newPos, connectedGrid, cubeAbove);
                }
              }
            }
            else if (AiSession.Instance.PassageBlockDefinitions.Contains(cubeDef.Id))
            {
              var downDir = Base6Directions.GetOppositeDirection(upDir);
              if (cubeDef.Id.TypeId == typeof(MyObjectBuilder_Passage))
              {
                if (block.Orientation.Forward == upDir || block.Orientation.Forward == downDir)
                {
                  OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);
                }
              }
              else if (block.Orientation.Up == upDir || block.Orientation.Up == downDir)
              {
                OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);
              }
            }
            else if (AiSession.Instance.BeamBlockDefinitions.ContainsItem(cubeDef.Id))
            {
              var cubeAbove = block.CubeGrid.GetCubeBlock(block.Position + upVec);
              if (cubeAbove == null)
              {
                var blockSubtype = cubeDef.Id.SubtypeName;
                if (blockSubtype.EndsWith("Slope"))
                {
                  if (block.Orientation.Forward == upDir || block.Orientation.Left == Base6Directions.GetOppositeDirection(upDir))
                    OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);
                }
                else if (blockSubtype.EndsWith("TJunction") || blockSubtype.EndsWith("Tip") || blockSubtype.EndsWith("Base"))
                {
                  if (block.Orientation.Left == upDir)
                    OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);
                }
                else if (block.Orientation.Left == upDir || block.Orientation.Left == Base6Directions.GetOppositeDirection(upDir))
                {
                  OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);
                }
              }
            }
            else if (AiSession.Instance.CatwalkBlockDefinitions.Contains(cubeDef.Id))
            {
              if (block.Orientation.Up == upDir)
              {
                OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);
              }
            }
            else if (AiSession.Instance.SlopeBlockDefinitions.Contains(cubeDef.Id))
            {
              var positionAbove = position + upVec;
              var cubeAbove = connectedGrid.GetCubeBlock(positionAbove) as IMySlimBlock;
              var entrance = Base6Directions.GetOppositeDirection(block.Orientation.Forward);
              bool cubeAboveEmpty = cubeAbove == null || !((MyCubeBlockDefinition)cubeAbove.BlockDefinition).HasPhysics;
              bool validHalfStair = false;
              bool includeAbove = false;
              bool entranceIsUp = entrance == upDir;
              bool upIsUp = !entranceIsUp && block.Orientation.Up == upDir;

              bool addThis = (upIsUp && cubeAboveEmpty)
                || (entranceIsUp && (cubeAboveEmpty || cubeAbove == block));

              if (!addThis)
              {
                if (cubeAboveEmpty && block.BlockDefinition.Id.SubtypeName.EndsWith("HalfSlopeArmorBlock"))
                {
                  addThis = upIsUp || Base6Directions.GetOppositeDirection(upDir) == block.Orientation.Forward;
                }

                if (!addThis)
                {
                  if (!cubeAboveEmpty && (entranceIsUp || upIsUp))
                  {
                    var cubeAboveDef = cubeAbove.BlockDefinition.Id;

                    if (AiSession.Instance.HalfStairBlockDefinitions.Contains(cubeDef.Id))
                    {
                      if (cubeAboveDef == cubeDef.Id && block.Orientation.Up == cubeAbove.Orientation.Up)
                        addThis = validHalfStair = (entrance == cubeAbove.Orientation.Forward);

                      if (AiSession.Instance.RailingBlockDefinitions.ContainsItem(cubeAboveDef))
                      {
                        if (!addThis)
                          addThis = !CheckCatwalkForRails(cubeAbove, -upVec);
  
                        includeAbove = addThis;
                      }
                    }
                    else if (AiSession.Instance.SlopeBlockDefinitions.Contains(cubeAboveDef))
                    {
                      var aboveOr = cubeAbove.Orientation;
                      var downDir = Base6Directions.GetOppositeDirection(block.Orientation.Up);
                      if (AiSession.Instance.SlopedHalfBlockDefinitions.Contains(cubeAboveDef))
                      {
                        if (!cubeAboveDef.SubtypeName.EndsWith("Slope2Base"))
                        {
                          if (entranceIsUp)
                          {
                            addThis = (aboveOr.Up == entrance && aboveOr.Forward == downDir) 
                              || (aboveOr.Forward == Base6Directions.GetOppositeDirection(entrance) && aboveOr.Up == upDir);
                            if (addThis)
                            {
                              includeAbove = true;
                              ExemptNodesUpper.Add(positionAbove);
                            }
                          }
                          else // Up is up
                          {
                            addThis = (aboveOr.Up == downDir && aboveOr.Forward == entrance)
                              || (aboveOr.Forward == upDir && aboveOr.Up == Base6Directions.GetOppositeDirection(entrance));
                            if (addThis)
                            {
                              includeAbove = true;
                              ExemptNodesUpper.Add(positionAbove);
                            }
                          }
                        }
                      }
                      else
                      {
                        if (entranceIsUp)
                        {
                          addThis = (aboveOr.Up == entrance && aboveOr.Forward == downDir) || (aboveOr.Forward == entrance && aboveOr.Up == downDir);
                          if (addThis)
                          {
                            includeAbove = true;
                            ExemptNodesUpper.Add(positionAbove);
                          }
                        }
                        else // Up is up
                        {
                          addThis = (aboveOr.Up == downDir && aboveOr.Forward == entrance) || (aboveOr.Forward == downDir && aboveOr.Up == entrance);
                          if (addThis)
                          {
                            includeAbove = true;
                            ExemptNodesUpper.Add(positionAbove);
                          }
                        }
                      }
                    }
                    else if (AiSession.Instance.RampBlockDefinitions.Contains(cubeAboveDef))
                    {
                      // TODO: Is this needed?
                    }
                    else if (AiSession.Instance.RailingBlockDefinitions.ContainsItem(cubeAboveDef))
                    {
                      addThis = !CheckCatwalkForRails(cubeAbove, -upVec);
                      includeAbove = addThis;
                    }
                  }

                  if (!addThis)
                  {
                    if (cubeDef.Id.SubtypeName.IndexOf("BlockArmorSlope2") >= 0)
                    {
                      var blockOr = block.Orientation;
                      var blockDown = Base6Directions.GetOppositeDirection(blockOr.Up);
                      var blockBack = Base6Directions.GetOppositeDirection(blockOr.Forward);
                      bool isBlockTip = cubeDef.Id.SubtypeName.EndsWith("Tip");

                      if (entranceIsUp)
                      {
                        addThis = true;
                        includeAbove = cubeAboveEmpty;

                        if (!includeAbove)
                        {
                          var aboveOr = cubeAbove.Orientation;
                          var aboveDef = cubeAbove.BlockDefinition.Id;
                          var aboveSubtype = aboveDef.SubtypeName;

                          if (AiSession.Instance.RampBlockDefinitions.Contains(cubeAbove.BlockDefinition.Id))
                          {
                            includeAbove = !isBlockTip && aboveOr.Up == blockDown && aboveOr.Forward == blockOr.Forward;
                          }
                          else if (isBlockTip)
                          {
                            if (AiSession.Instance.SlopeBlockDefinitions.Contains(aboveDef))
                            {
                              if (aboveSubtype.IndexOf("ArmorSlope2") >= 0)
                              {
                                if (aboveSubtype.EndsWith("Tip"))
                                {
                                  includeAbove = aboveOr.Forward == blockBack && aboveOr.Up == blockDown;
                                }
                              }
                              else if (aboveSubtype.EndsWith("ArmorHalfSlopeCorner")) // SlopeCorner vs SlopedCorner, gotta love it :/
                              {
                                includeAbove = aboveOr.Up == blockOr.Forward || aboveOr.Forward == blockBack;
                              }
                              else if (aboveSubtype.EndsWith("BlockArmorSlope"))
                              {
                                includeAbove = (aboveOr.Up == blockDown && aboveOr.Forward == blockBack)
                                  || (aboveOr.Forward == blockOr.Up && aboveOr.Up == blockOr.Forward);
                              }
                              else if (aboveSubtype.EndsWith("HalfSlopeArmorBlock"))
                              {
                                includeAbove = (aboveOr.Up == blockDown && aboveOr.Forward == blockBack)
                                  || (aboveOr.Up == blockOr.Forward && aboveOr.Forward == blockOr.Up);
                              }
                            }
                          }
                          else // block base -> add adjacent
                          {
                            var adjacentPos = mainGridPosition + Base6Directions.GetIntVector(blockOr.Up);
                            var adjacentBlock = connectedGrid.GetCubeBlock(adjacentPos) as IMySlimBlock;

                            if (adjacentBlock != null)
                            {
                              var adjacentDef = adjacentBlock.BlockDefinition.Id;
                              var adjacentOr = adjacentBlock.Orientation;
                              bool addAdjacent = false;

                              if (AiSession.Instance.SlopedHalfBlockDefinitions.Contains(adjacentDef))
                              {
                                addAdjacent = adjacentOr.Up == blockDown && (adjacentOr.Forward == blockBack || adjacentOr.Forward == blockOr.Forward);

                                if (!addAdjacent && !adjacentDef.SubtypeName.EndsWith("Tip"))
                                {
                                  addAdjacent = adjacentOr.Forward == blockOr.Up && (adjacentOr.Up == blockBack || adjacentOr.Up == blockOr.Forward);
                                }
                              }
                              else if (AiSession.Instance.RampBlockDefinitions.Contains(adjacentDef))
                              {
                                addAdjacent = adjacentOr.Forward == blockOr.Forward && adjacentOr.Up == blockDown;
                              }
                              else if (AiSession.Instance.SlopeBlockDefinitions.Contains(adjacentDef))
                              {
                                if (adjacentDef.SubtypeName.EndsWith("ArmorHalfSlopedCorner"))
                                {
                                  addAdjacent = adjacentOr.Left == blockDown || adjacentOr.Up == blockDown || adjacentOr.Forward == blockDown;
                                }
                              }

                              if (addAdjacent)
                              {
                                OpenTileDict[adjacentPos] = new Node(adjacentPos, connectedGrid, adjacentBlock);
                                ExemptNodesSide.Add(adjacentPos);
                              }
                            }
                          }
                        }
                      }
                      else if (upIsUp && !cubeAboveEmpty)
                      {
                        var aboveOr = cubeAbove.Orientation;
                        var aboveDef = cubeAbove.BlockDefinition.Id;
                        var aboveSubtype = aboveDef.SubtypeName;

                        if (!isBlockTip)
                        {
                          // block base is the tall side - less head room
                          if (AiSession.Instance.SlopedHalfBlockDefinitions.Contains(aboveDef))
                          {
                            if (aboveSubtype.EndsWith("ArmorSlope2Tip"))
                            {
                              addThis = aboveOr.Forward == blockBack && aboveOr.Up == blockDown;
                            }
                            else if (aboveSubtype.EndsWith("ArmorHalfSlopeCorner")) // SlopeCorner vs SlopedCorner, gotta love it :/
                            {
                              addThis = aboveOr.Up == blockDown || Base6Directions.GetOppositeDirection(aboveOr.Forward) == blockDown;
                            }
                            else if (aboveSubtype.EndsWith("HalfSlopeArmorBlock"))
                            {
                              addThis = (aboveOr.Up == blockDown && aboveOr.Forward == blockBack)
                                || (aboveOr.Forward == blockOr.Up && aboveOr.Left == blockOr.Left);
                            }
                          }
                          else if (AiSession.Instance.HalfBlockDefinitions.ContainsItem(aboveDef))
                          {
                            if (aboveSubtype.EndsWith("HalfArmorBlock"))
                            {
                              addThis = blockDown == Base6Directions.GetOppositeDirection(aboveOr.Forward);
                            }
                            else if (aboveSubtype.EndsWith("ArmorHalfSlopedCorner"))
                            {
                              addThis = aboveOr.Forward == blockDown || aboveOr.Left == blockDown || aboveOr.Up == blockDown;
                            }
                          }
                        }
                        else // slope tip
                        {
                          if (AiSession.Instance.SlopeBlockDefinitions.Contains(aboveDef))
                          {
                            if (aboveSubtype.IndexOf("ArmorSlope2") >= 0)
                            {
                              addThis = aboveOr.Up == blockDown;

                              if (addThis && aboveSubtype.EndsWith("Base"))
                              {
                                addThis &= aboveOr.Forward == blockBack;
                              }
                            }
                            else if (aboveSubtype.EndsWith("ArmorHalfSlopeCorner")) // SlopeCorner vs SlopedCorner, gotta love it :/
                            {
                              addThis = aboveOr.Up == blockDown || Base6Directions.GetOppositeDirection(aboveOr.Forward) == blockDown;
                            }
                            else if (aboveSubtype.EndsWith("BlockArmorSlope"))
                            {
                              addThis = (aboveOr.Up == blockDown && aboveOr.Forward == blockBack)
                                || (aboveOr.Forward == blockOr.Up && blockBack == Base6Directions.GetOppositeDirection(aboveOr.Up));
                            }
                            else if (aboveSubtype.EndsWith("HalfSlopeArmorBlock"))
                            {
                              addThis = aboveOr.Up == blockDown || aboveOr.Forward == blockOr.Up;
                            }
                          }
                          else if (AiSession.Instance.HalfBlockDefinitions.ContainsItem(aboveDef))
                          {
                            bool isHalfSlopedCorner = aboveSubtype.EndsWith("ArmorHalfSlopedCorner");
                            if (isHalfSlopedCorner || aboveSubtype.EndsWith("ArmorHalfCorner") || aboveSubtype.EndsWith("HalfArmorBlock"))
                            {
                              addThis = aboveOr.Up == blockDown;

                              if (addThis && isHalfSlopedCorner)
                              {
                                addThis &= (aboveOr.Forward == blockBack || aboveOr.Left == blockBack);
                              }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }

              if (addThis)
              {
                OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);

                if (includeAbove || validHalfStair
                  || !AiSession.Instance.SlopedHalfBlockDefinitions.Contains(cubeDef.Id)
                  || (entranceIsUp && cubeAboveEmpty && cubeDef.Id.SubtypeName.EndsWith("Tip")))
                {
                  var newPos = mainGridPosition + upVec;
                  OpenTileDict[newPos] = new Node(newPos, connectedGrid, cubeAbove);

                  if (!cubeAboveEmpty && !validHalfStair)
                  {
                    ExemptNodesUpper.Add(newPos);
                  }
                }
              }
            }
            else if (AiSession.Instance.RampBlockDefinitions.Contains(cubeDef.Id))
            {
              Matrix matrix = new Matrix
              {
                Forward = Base6Directions.GetVector(block.Orientation.Forward),
                Left = Base6Directions.GetVector(block.Orientation.Left),
                Up = Base6Directions.GetVector(block.Orientation.Up)
              };

              var faceDict = AiSession.Instance.BlockFaceDictionary[cubeDef.Id];
              if (faceDict.Count < 2)
                matrix.TransposeRotationInPlace();

              var dotUp = Base6Directions.GetIntVector(block.Orientation.Up).Dot(ref upVec);
              var dotFwd = Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref upVec);

              Vector3I center = cubeDef.Center;
              Vector3I.TransformNormal(ref center, ref matrix, out center);
              var adjustedPosition = block.Position - center;
              var blockOr = block.Orientation;

              foreach (var kvp in faceDict)
              {
                Vector3I offset = kvp.Key;
                Vector3I.TransformNormal(ref offset, ref matrix, out offset);
                var cell = adjustedPosition + offset;
                var positionAbove = cell + upVec;
                mainGridPosition = connectedIsMain ? cell : Grid.WorldToGridInteger(connectedGrid.GridIntegerToWorld(cell));

                var cubeAbove = connectedGrid.GetCubeBlock(positionAbove) as IMySlimBlock;
                bool cubeAboveEmpty = cubeAbove == null || !((MyCubeBlockDefinition)cubeAbove.BlockDefinition).HasPhysics;
                bool isBlockTip = block.Position == cell;
                bool addAbove = false, addThis = false;

                addThis = (blockOr.Up == upDir && cubeAboveEmpty) || (blockOr.Forward == upDir && (cubeAboveEmpty || cubeAbove == block));

                if (!addThis && !cubeAboveEmpty)
                {
                  var aboveOr = cubeAbove.Orientation;
                  var aboveDef = cubeAbove.BlockDefinition.Id;
                  var aboveSubtype = aboveDef.SubtypeName;
                  var blockDown = Base6Directions.GetOppositeDirection(blockOr.Up);

                  if (dotUp > 0) // normal placement
                  {
                    addAbove = !isBlockTip;

                    if (AiSession.Instance.RampBlockDefinitions.Contains(aboveDef))
                    {
                      addThis = aboveOr.Up == blockDown && aboveOr.Forward == Base6Directions.GetOppositeDirection(blockOr.Forward);
                    }
                    else if (isBlockTip)
                    {
                      if (AiSession.Instance.SlopeBlockDefinitions.Contains(aboveDef))
                      {
                        if (aboveSubtype.IndexOf("ArmorSlope2") >= 0)
                        {
                          addThis = aboveOr.Up == blockDown;

                          if (addThis && aboveSubtype.EndsWith("Base"))
                          {
                            addThis &= aboveOr.Forward == blockOr.Forward;
                          }
                        }
                        else if (aboveSubtype.EndsWith("ArmorHalfSlopeCorner")) // SlopeCorner vs SlopedCorner, gotta love it :/
                        {
                          addThis = aboveOr.Up == blockDown || Base6Directions.GetOppositeDirection(aboveOr.Forward) == blockDown;
                        }
                        else if (aboveSubtype.EndsWith("BlockArmorSlope"))
                        {
                          addThis = (aboveOr.Up == blockDown && aboveOr.Forward == blockOr.Forward)
                            || (aboveOr.Forward == blockOr.Up && blockOr.Forward == Base6Directions.GetOppositeDirection(aboveOr.Up));
                        }
                        else if (aboveSubtype.EndsWith("HalfSlopeArmorBlock"))
                        {
                          addThis = aboveOr.Up == blockDown || aboveOr.Forward == blockOr.Up;
                        }
                      }
                      else if (AiSession.Instance.HalfBlockDefinitions.ContainsItem(aboveDef))
                      {
                        bool isHalfSlopedCorner = aboveSubtype.EndsWith("ArmorHalfSlopedCorner");
                        if (isHalfSlopedCorner || aboveSubtype.EndsWith("ArmorHalfCorner") || aboveSubtype.EndsWith("HalfArmorBlock"))
                        {
                          addThis = aboveOr.Up == blockDown;

                          if (addThis && isHalfSlopedCorner)
                          {
                            addThis &= (aboveOr.Forward == blockOr.Forward || aboveOr.Left == blockOr.Forward);
                          }
                        }
                      }
                    }
                    else // block base is the tall side - less head room
                    {
                      if (AiSession.Instance.SlopedHalfBlockDefinitions.Contains(aboveDef))
                      {
                        if (aboveSubtype.EndsWith("ArmorSlope2Tip"))
                        {
                          addThis = aboveOr.Forward == blockOr.Forward && aboveOr.Up == blockDown;
                        }
                        else if (aboveSubtype.EndsWith("ArmorHalfSlopeCorner")) // SlopeCorner vs SlopedCorner, gotta love it :/
                        {
                          addThis = aboveOr.Up == blockDown || Base6Directions.GetOppositeDirection(aboveOr.Forward) == blockDown;
                        }
                        else if (aboveSubtype.EndsWith("HalfSlopeArmorBlock"))
                        {
                          addThis = (aboveOr.Up == blockDown && aboveOr.Forward == blockOr.Forward)
                            || (aboveOr.Forward == blockOr.Up && aboveOr.Left == blockOr.Left);
                        }
                      }
                      else if (AiSession.Instance.HalfBlockDefinitions.ContainsItem(aboveDef))
                      {
                        if (aboveSubtype.EndsWith("HalfArmorBlock"))
                        {
                          addThis = blockDown == Base6Directions.GetOppositeDirection(aboveOr.Forward);
                        }
                        else if (aboveSubtype.EndsWith("ArmorHalfSlopedCorner"))
                        {
                          addThis = aboveOr.Forward == blockDown || aboveOr.Left == blockDown || aboveOr.Up == blockDown;
                        }
                      }
                    }
                  }
                  else if (dotFwd > 0) // steep placement
                  {
                    addThis = cubeAbove == block;
                    addAbove = isBlockTip;

                    if (!addThis)
                    {
                      if (AiSession.Instance.RampBlockDefinitions.Contains(cubeAbove.BlockDefinition.Id))
                      {
                        addThis = aboveOr.Up == Base6Directions.GetOppositeDirection(blockOr.Up)
                          && aboveOr.Forward == Base6Directions.GetOppositeDirection(blockOr.Forward);
                      }
                      else if (isBlockTip)
                      {
                        var blockBack = Base6Directions.GetOppositeDirection(blockOr.Forward);

                        if (AiSession.Instance.SlopeBlockDefinitions.Contains(aboveDef))
                        {
                          if (aboveSubtype.IndexOf("ArmorSlope2") >= 0)
                          {
                            if (aboveSubtype.EndsWith("Tip"))
                            {
                              addThis = aboveOr.Forward == blockOr.Forward && aboveOr.Up == blockDown;
                            }
                          }
                          else if (aboveSubtype.EndsWith("ArmorHalfSlopeCorner")) // SlopeCorner vs SlopedCorner, gotta love it :/
                          {
                            addThis = aboveOr.Up == blockBack || Base6Directions.GetOppositeDirection(aboveOr.Forward) == blockBack;
                          }
                          else if (aboveSubtype.EndsWith("BlockArmorSlope"))
                          {
                            addThis = (aboveOr.Up == blockDown && aboveOr.Forward == blockOr.Forward)
                              || (aboveOr.Forward == blockOr.Up && aboveOr.Up == blockBack);
                          }
                          else if (aboveSubtype.EndsWith("HalfSlopeArmorBlock"))
                          {
                            addThis = (aboveOr.Up == blockDown && aboveOr.Forward == blockOr.Forward)
                              || (aboveOr.Up == blockBack && aboveOr.Forward == blockOr.Up);
                          }
                        }
                      }
                    }
                  }
                }
                else if (cubeAboveEmpty)
                {
                  addAbove = (dotUp > 0 && !isBlockTip) || (dotFwd > 0 && isBlockTip);
                }
                else if (cubeAbove == block && !isBlockTip) // add adjacent
                {
                  var blockDown = Base6Directions.GetOppositeDirection(blockOr.Up);
                  var adjacentPos = mainGridPosition + Base6Directions.GetIntVector(blockOr.Up);
                  var adjacentBlock = connectedGrid.GetCubeBlock(adjacentPos) as IMySlimBlock;

                  if (adjacentBlock != null)
                  {
                    var blockBack = Base6Directions.GetOppositeDirection(blockOr.Forward);
                    var adjacentDef = adjacentBlock.BlockDefinition.Id;
                    var adjacentOr = adjacentBlock.Orientation;
                    bool addAdjacent = false;

                    if (AiSession.Instance.SlopedHalfBlockDefinitions.Contains(adjacentDef))
                    {
                      addAdjacent = adjacentOr.Up == blockDown && (adjacentOr.Forward == blockBack || adjacentOr.Forward == blockOr.Forward);

                      if (!addAdjacent && !adjacentDef.SubtypeName.EndsWith("Tip"))
                      {
                        addAdjacent = adjacentOr.Forward == blockOr.Up && (adjacentOr.Up == blockBack || adjacentOr.Up == blockOr.Forward);
                      }
                    }
                    else if (AiSession.Instance.RampBlockDefinitions.Contains(adjacentDef))
                    {
                      addAdjacent = adjacentOr.Forward == blockBack && adjacentOr.Up == blockDown;
                    }
                    else if (AiSession.Instance.SlopeBlockDefinitions.Contains(adjacentDef))
                    {
                      if (adjacentDef.SubtypeName.EndsWith("ArmorHalfSlopedCorner"))
                      {
                        addAdjacent = adjacentOr.Left == blockDown || adjacentOr.Up == blockDown || adjacentOr.Forward == blockDown;
                      }
                    }

                    if (addAdjacent)
                    {
                      OpenTileDict[adjacentPos] = new Node(adjacentPos, connectedGrid, adjacentBlock);
                      ExemptNodesSide.Add(adjacentPos);
                    }
                  }
                }

                if (addThis)
                {
                  OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);

                  if (addAbove)
                  {
                    var newPos = mainGridPosition + upVec;
                    OpenTileDict[newPos] = new Node(newPos, connectedGrid, cubeAbove);

                    if (!cubeAboveEmpty)
                    {
                      ExemptNodesUpper.Add(newPos);
                    }
                  }
                }
              }
            }
            else if (AiSession.Instance.FlatWindowDefinitions.ContainsItem(cubeDef.Id))
            {
              Base6Directions.Direction sideWithPane;
              if (cubeDef.Id.SubtypeName == "LargeWindowSquare")
                sideWithPane = block.Orientation.Forward;
              else
                sideWithPane = Base6Directions.GetOppositeDirection(block.Orientation.Left);

              Matrix matrix = new Matrix
              {
                Forward = Base6Directions.GetVector(block.Orientation.Forward),
                Left = Base6Directions.GetVector(block.Orientation.Left),
                Up = Base6Directions.GetVector(block.Orientation.Up)
              };

              var faceDict = AiSession.Instance.BlockFaceDictionary[cubeDef.Id];
              if (faceDict.Count < 2)
                matrix.TransposeRotationInPlace();

              Vector3I center = cubeDef.Center;
              Vector3I.TransformNormal(ref center, ref matrix, out center);
              var adjustedPosition = block.Position - center;
              var downDir = Base6Directions.GetOppositeDirection(upDir);

              foreach (var kvp in faceDict)
              {
                Vector3I offset = kvp.Key;
                Vector3I.TransformNormal(ref offset, ref matrix, out offset);
                var cell = adjustedPosition + offset;
                mainGridPosition = connectedIsMain ? cell : Grid.WorldToGridInteger(connectedGrid.GridIntegerToWorld(cell));

                if (sideWithPane == downDir || (cubeDef.Id.SubtypeName.StartsWith("HalfWindowCorner") && block.Orientation.Forward == downDir))
                  OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);
              }
            }
            else if (AiSession.Instance.AngledWindowDefinitions.ContainsItem(cubeDef.Id))
            {
              Matrix matrix = new Matrix
              {
                Forward = Base6Directions.GetVector(block.Orientation.Forward),
                Left = Base6Directions.GetVector(block.Orientation.Left),
                Up = Base6Directions.GetVector(block.Orientation.Up)
              };

              var faceDict = AiSession.Instance.BlockFaceDictionary[cubeDef.Id];
              if (faceDict.Count < 2)
                matrix.TransposeRotationInPlace();

              Vector3I center = cubeDef.Center;
              Vector3I.TransformNormal(ref center, ref matrix, out center);
              var adjustedPosition = block.Position - center;

              bool isEdgeWindow = cubeDef.Id.SubtypeName == "LargeWindowEdge";

              foreach (var kvp in faceDict)
              {
                Vector3I offset = kvp.Key;
                Vector3I.TransformNormal(ref offset, ref matrix, out offset);
                var cell = adjustedPosition + offset;
                var positionAbove = cell + upVec;
                mainGridPosition = connectedIsMain ? cell : Grid.WorldToGridInteger(connectedGrid.GridIntegerToWorld(cell));

                var cubeAbove = connectedGrid.GetCubeBlock(positionAbove) as IMySlimBlock;
                bool cubeAboveEmpty = cubeAbove == null || !((MyCubeBlockDefinition)cubeAbove.BlockDefinition).HasPhysics;

                if (isEdgeWindow)
                {
                  if (cubeAboveEmpty && (block.Orientation.Up == upDir || block.Orientation.Forward == upDir))
                  {
                    OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);

                    var newPos = mainGridPosition + upVec;
                    OpenTileDict[newPos] = new Node(newPos, connectedGrid, cubeAbove);
                  }
                }
                else
                {
                  if (cubeAboveEmpty || cubeAbove == block)
                  {
                    var vecLeft = Base6Directions.GetIntVector(block.Orientation.Left);
                    if (vecLeft.Dot(ref upVec) == 0)
                    {
                      OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);

                      var newPos = mainGridPosition + upVec;
                      OpenTileDict[newPos] = new Node(newPos, connectedGrid, cubeAbove);
                    }
                  }
                }
              }
            }
            else if (cubeDef.Id.TypeId == typeof(MyObjectBuilder_SolarPanel))
            {
              if (block.Orientation.Forward == Base6Directions.GetOppositeDirection(upDir))
                OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);
            }
            else if (cubeDef.Id.TypeId == typeof(MyObjectBuilder_Ladder2))
            {
              OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);

              var blockUp = Base6Directions.GetIntVector(block.Orientation.Up);
              if (blockUp.Dot(ref upVec) != 0)
              {
                var positionAbove = position + upVec;
                var cubeAbove = connectedGrid.GetCubeBlock(positionAbove) as IMySlimBlock;
                if (cubeAbove == null)
                {
                  var newPos = mainGridPosition + upVec;
                  OpenTileDict[newPos] = new Node(newPos, connectedGrid, cubeAbove);
                }
              }
            }
            else if (cubeDef.Id.SubtypeName.StartsWith("Large") && cubeDef.Id.SubtypeName.EndsWith("HalfArmorBlock"))
            {
              var blockFwd = Base6Directions.GetIntVector(block.Orientation.Forward);
              if (blockFwd.Dot(ref upVec) < 0)
              {
                var positionAbove = position + upVec;
                var cubeAbove = connectedGrid.GetCubeBlock(positionAbove) as IMySlimBlock;
                if (cubeAbove == null)
                  OpenTileDict[mainGridPosition] = new Node(mainGridPosition, connectedGrid, block);
              }
            }
          }
        }

        if (Dirty)
          return;

        var worldCenter = LocalToWorld(BoundingBox.Center);
        var quat = Quaternion.CreateFromRotationMatrix(Grid.GetBiggestGridInGroup().WorldMatrix);
        UnbufferedOBB = new MyOrientedBoundingBoxD(worldCenter, BoundingBox.HalfExtents * CellSize + Vector3D.Half * CellSize, quat);

        BoundingBox.Inflate(_boxExpansion);
        OBB = new MyOrientedBoundingBoxD(worldCenter, BoundingBox.HalfExtents * CellSize + Vector3D.Half * CellSize, quat);

        if (!Grid.Physics.IsStatic)
          AddAdditionalGridTiles();

        float _;
        Vector3 gravityNorm;
        var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out _);
        if (nGrav.LengthSquared() > 0)
        {
          Planet = MyGamePruningStructure.GetClosestPlanet(WorldMatrix.Translation);
          gravityNorm = Vector3.Normalize(nGrav);
        }
        else
        {
          Planet = null;
          gravityNorm = (Vector3)WorldMatrix.Down;
        }

        upDir = Grid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
        upVec = Base6Directions.GetIntVector(upDir);
        CheckForPlanetTiles(ref BoundingBox, ref gravityNorm, ref upVec);
        PlanetTilesRemoved = false;

        foreach (var tile in OpenTileDict.Values)
        {
          if (Dirty)
            return;

          if (tile.IsGridNodeAdditional || tile.IsAirNode)
            continue;

          foreach (var edge in GetBlockedNodeEdges(tile))
            tile.AddBlockage(edge);

          if (tile.SurfacePosition == null)
          {
            var worldNode = tile.Grid.GridIntegerToWorld(tile.Position);
            if (PointInsideVoxel(worldNode, Planet))
            {
              tile.IsGridNodeUnderGround = true;
            }
          }
        }

        _additionalMaps2.Clear();
        UpdateTempObstacles();
        //AiSession.Instance.Logger.Log($"Grid.InitGridArea finished");
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception during InitGridArea: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    private void Door_OnDoorStateChanged(IMyDoor door, bool open)
    {
      Door_EnabledChanged(door);
    }

    private void Door_OnMarkForClose(VRage.ModAPI.IMyEntity ent)
    {
      ent.OnMarkForClose -= Door_OnMarkForClose;

      var door = ent as IMyDoor;
      if (door != null)
      {
        Door_EnabledChanged(door);
        door.EnabledChanged -= Door_EnabledChanged;
        door.IsWorkingChanged -= Door_EnabledChanged;
        door.OnDoorStateChanged -= Door_OnDoorStateChanged;
      }
    }

    internal void Door_EnabledChanged(IMyCubeBlock block)
    {
      var door = block as IMyDoor;
      if (door != null)
      {
        Vector3I pos = block.Position;
        var grid = block.CubeGrid as MyCubeGrid;
        var needsPositionAdjusted = Grid.EntityId != grid.EntityId;

        if (needsPositionAdjusted)
          pos = Grid.WorldToGridInteger(grid.GridIntegerToWorld(pos));

        bool isOpen = false;
        var blockDef = (MyCubeBlockDefinition)door.SlimBlock.BlockDefinition;
        if (door.SlimBlock.BuildLevelRatio < blockDef.CriticalIntegrityRatio)
          isOpen = true;

        if (isOpen || door.MarkedForClose || door.Enabled || door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open)
        {
          IMyDoor _;
          if (BlockedDoors.TryRemove(pos, out _))
          {
            var cubeDef = block.SlimBlock.BlockDefinition as MyCubeBlockDefinition;
            var faceDict = AiSession.Instance.BlockFaceDictionary[cubeDef.Id];

            Matrix matrix = new Matrix
            {
              Forward = Base6Directions.GetVector(block.Orientation.Forward),
              Left = Base6Directions.GetVector(block.Orientation.Left),
              Up = Base6Directions.GetVector(block.Orientation.Up)
            };

            matrix.TransposeRotationInPlace();

            Vector3I center = cubeDef.Center;
            Vector3I.TransformNormal(ref center, ref matrix, out center);
            var adjustedPosition = block.Position - center;

            foreach (var kvp in faceDict)
            {
              var cell = kvp.Key;
              Vector3I.TransformNormal(ref cell, ref matrix, out cell);
              var position = adjustedPosition + cell;

              if (needsPositionAdjusted)
                position = Grid.WorldToGridInteger(grid.GridIntegerToWorld(position));

              BlockedDoors.TryRemove(position, out _);
            }
          }
        }
        else if (!door.Enabled && !BlockedDoors.ContainsKey(pos))
        {
          BlockedDoors[pos] = door;

          var cubeDef = block.SlimBlock.BlockDefinition as MyCubeBlockDefinition;
          var faceDict = AiSession.Instance.BlockFaceDictionary[cubeDef.Id];

          Matrix matrix = new Matrix
          {
            Forward = Base6Directions.GetVector(block.Orientation.Forward),
            Left = Base6Directions.GetVector(block.Orientation.Left),
            Up = Base6Directions.GetVector(block.Orientation.Up)
          };

          matrix.TransposeRotationInPlace();

          Vector3I center = cubeDef.Center;
          Vector3I.TransformNormal(ref center, ref matrix, out center);
          var adjustedPosition = block.Position - center;

          foreach (var kvp in faceDict)
          {
            var cell = kvp.Key;
            Vector3I.TransformNormal(ref cell, ref matrix, out cell);
            var position = adjustedPosition + cell;

            if (needsPositionAdjusted)
              position = Grid.WorldToGridInteger(grid.GridIntegerToWorld(position));

            BlockedDoors[position] = door;
          }
        }
      }
    }

    List<CubeGridMap> _additionalMaps = new List<CubeGridMap>();
    List<CubeGridMap> _additionalMaps2 = new List<CubeGridMap>();
    void AddAdditionalGridTiles()
    {
      _additionalMaps.Clear();
      var sphere = new BoundingSphereD(Grid.PositionComp.WorldAABB.Center, BoundingBox.HalfExtents.AbsMax() * CellSize);

      lock (_tempEntities)
      {
        _tempEntities.Clear();
        MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, _tempEntities);

        foreach (var ent in _tempEntities)
        {
          var grid = ent as MyCubeGrid;
          if (grid?.Physics == null || grid.IsSameConstructAs(Grid) || grid.MarkedForClose || grid.MarkedAsTrash || grid.GridSize < 1)
            continue;

          var gridMap = AiSession.Instance.GetGridGraph(grid, WorldMatrix);
          _additionalMaps.Add(gridMap);
        }

        _tempEntities.Clear();
      }

      int countMS = 0;
      int maxMS = MyUtils.GetRandomInt(1000, 2000);
      while (_additionalMaps.Count > 0)
      {
        for (int i = _additionalMaps.Count - 1; i >= 0; i--)
        {
          var map = _additionalMaps[i];
          if (!map.Ready)
            continue;

          foreach (var kvp in map.OpenTileDict)
          {
            if (map.Dirty || map.Grid?.MarkedForClose != false)
              break;

            var node = kvp.Value;
            if (map.ObstacleNodes.ContainsKey(node.Position))
              continue;

            var localTile = Grid.WorldToGridInteger(node.Grid.GridIntegerToWorld(kvp.Key));
            if (!OpenTileDict.ContainsKey(localTile) && !ObstacleNodes.ContainsKey(localTile) && BoundingBox.Contains(localTile) != ContainmentType.Disjoint)
            {
              var newNode = new Node(localTile, node.Grid, node.Block, node.SurfacePosition ?? node.Grid.GridIntegerToWorld(node.Position))
              {
                IsGridNode = node.IsGridNode,
                IsGridNodeUnderGround = node.IsGridNodeUnderGround,
                IsGridNodePlanetTile = true,
                IsGridNodeAdditional = true
              };

              if (node.BlockedEdges != null)
              {
                foreach (var blocked in node.BlockedEdges)
                  newNode.AddBlockage(blocked);
              }

              OpenTileDict[localTile] = newNode;
            }
          }

          _additionalMaps2.Add(map);
          _additionalMaps.RemoveAtFast(i);
        }

        MyAPIGateway.Parallel.Sleep(25);
        countMS += 25;

        if (countMS > maxMS) // TODO: possibly check if this grid has less blocks than the other and break on smaller one ??
          break;
      }
    }

    void CheckForPlanetTiles(ref BoundingBoxI box, ref Vector3 gravityNorm, ref Vector3I upVec)
    {
      var blockedVoxelEdges = AiSession.Instance.BlockedVoxelEdges;
      var cellSize = CellSize;
      var cellSizeCutoff = cellSize * 0.65f;
      var cellSizeCutoffSqd = cellSizeCutoff * cellSizeCutoff;
      var worldMatrix = WorldMatrix;

      bool checkForWater = false; 
      bool checkForVoxel = false;
      if (Planet != null && !Planet.MarkedForClose)
      {
        checkForWater = WaterAPI.Registered && WaterAPI.HasWater(Planet.EntityId);
        checkForVoxel = true;

        Planet.RangeChanged -= Planet_RangeChanged;
        Planet.OnMarkForClose -= Planet_OnMarkForClose;

        Planet.RangeChanged += Planet_RangeChanged;
        Planet.OnMarkForClose += Planet_OnMarkForClose;
      }

      double edgeDistance;
      if (!GetEdgeDistanceInDirection(WorldMatrix.Up, out edgeDistance))
        edgeDistance = VoxelGridMap.DefaultHalfSize * cellSize;

      var topOfGraph = OBB.Center + WorldMatrix.Up * edgeDistance;
      var iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);
      int tunnelCount = 0;

      while (iter.IsValid())
      {
        var localPoint = iter.Current;
        iter.MoveNext();

        bool isAirNode = true;
        bool isGroundNode = false;
        bool isTunnelNode = false;
        bool addNodeBelow = false;

        // worldPoint = LocalToWorld(localPoint);
        Vector3D worldPoint = Grid.GridIntegerToWorld(localPoint);
        var groundPoint = worldPoint;
        var groundPointBelow = worldPoint;

        var worldBelow = worldPoint + gravityNorm;
        var localBelow = localPoint - upVec;

        if (checkForVoxel)
        {
          if (Planet == null || Planet.MarkedForClose)
            checkForVoxel = false;

          if (PointInsideVoxel(worldPoint, Planet))
            continue;

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

            var offsetPosition = pointInAir - gravityNorm;

            if (Vector3D.DistanceSquared(pointInAir, worldPoint) > cellSizeCutoffSqd)
            {
              isAirNode = true;
              isGroundNode = false;
              addNodeBelow = true;
              groundPointBelow = offsetPosition;
            }
            else
            {
              groundPoint = offsetPosition;
            }

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

        Node node;        
        if (addNodeBelow)
        {
          if (OpenTileDict.TryGetValue(localBelow, out node))
          {
            if (!node.IsGroundNode)
              node.IsGroundNode = true;

            if (node.IsAirNode)
              node.IsAirNode = false;

            if (node.IsSpaceNode)
              node.IsSpaceNode = false;

            if (!node.IsTunnelNode)
              node.IsTunnelNode = isTunnelNode;

            addNodeBelow = false;
          }
        }
        
        if (OpenTileDict.TryGetValue(localPoint, out node))
        {
          if (!isAirNode && node.IsAirNode)
            node.IsAirNode = false;

          if (isGroundNode && !node.IsGroundNode)
            node.IsGroundNode = true;

          if (isTunnelNode && !node.IsTunnelNode)
            node.IsTunnelNode = true;

          continue;
        }

        bool add = true;
        foreach (var gridMap in _additionalMaps2)
        {
          var grid = gridMap.Grid;

          if (addNodeBelow)
          {
            var localBelowOther = grid.WorldToGridInteger(worldBelow);
            if (grid.CubeExists(localBelowOther))
              addNodeBelow = false;
          }

          var localPointOther = grid.WorldToGridInteger(worldPoint);
          if (grid.CubeExists(localPointOther))
          {
            add = false;
            break;
          }
        }

        if (add)
        {
          foreach (var grid in GridGroups)
          {
            if (addNodeBelow)
            {
              var localBelowOther = grid.WorldToGridInteger(worldBelow);
              if (grid.CubeExists(localBelowOther))
                addNodeBelow = false;
            }

            var localPointOther = grid.WorldToGridInteger(worldPoint);
            if (grid.CubeExists(localPointOther))
            {
              add = false;
              break;
            }

            if (isGroundNode)
            {
              var worldAbove = worldPoint - gravityNorm;
              var localAbove = grid.WorldToGridInteger(worldAbove);
              var cubeAbove = grid.GetCubeBlock(localAbove);
              if (cubeAbove != null)
              {
                if (!AiSession.Instance.CatwalkBlockDefinitions.Contains(cubeAbove.BlockDefinition.Id) || Base6Directions.GetIntVector(cubeAbove.Orientation.Up) != -upVec)
                {
                  var downVec = grid.WorldMatrix.GetDirectionVector(Base6Directions.GetDirection(-upVec));
                  var actualSurface = groundPoint + gravityNorm;
                  var bottomEdge = grid.GridIntegerToWorld(localAbove) + downVec * grid.GridSize * 0.5;
                  if (Vector3D.DistanceSquared(actualSurface, bottomEdge) < 4)
                  {
                    add = false;
                    break;
                  }
                }
              }
            }
          }

          if (add)
          {
            var isInWater = checkForWater && WaterAPI.IsUnderwater(groundPoint);

            node = new Node(localPoint, Grid, null, groundPoint)
            {
              IsGridNodePlanetTile = true,
              IsAirNode = isAirNode,
              IsGroundNode = isGroundNode,
              IsWaterNode = isInWater,
              IsTunnelNode = isTunnelNode && !addNodeBelow,
              BlockedEdges = checkForVoxel ? new List<Vector3I>(blockedVoxelEdges) : null,
            };

            OpenTileDict[localPoint] = node;

            if (addNodeBelow)
            {
              var nodeBelow = new Node(localBelow, Grid, null, groundPointBelow)
              {
                IsGridNodePlanetTile = true,
                IsAirNode = false,
                IsGroundNode = true,
                IsWaterNode = isInWater,
                IsTunnelNode = isTunnelNode,
                BlockedEdges = checkForVoxel ? new List<Vector3I>(blockedVoxelEdges) : null,
              };

              OpenTileDict[localBelow] = nodeBelow;
            }
          }
        }
      }

      _graphHasTunnel = tunnelCount > 25;
    }

    private void Planet_OnMarkForClose(MyEntity obj)
    {
      if (Planet != null)
      {
        Planet.OnMarkForClose -= Planet_OnMarkForClose;
        Planet.RangeChanged -= Planet_RangeChanged;
      }
    }

    BoundingBoxI? _pendingChanges;
    object _pendingLockObject = new object();
    private void Planet_RangeChanged(MyVoxelBase storage, Vector3I minVoxelChanged, Vector3I maxVoxelChanged, MyStorageDataTypeFlags changedData)
    {
      try
      {
        var min = minVoxelChanged;
        var max = maxVoxelChanged;

        if (_pendingChanges.HasValue)
        {
          min = Vector3I.Min(min, _pendingChanges.Value.Min);
          max = Vector3I.Max(max, _pendingChanges.Value.Max);
        }

        lock(_pendingLockObject)
          _pendingChanges = new BoundingBoxI(min, max);
  
        NeedsVoxelUpate = true;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in Planet_RangeChanged: {ex.Message}\n{ex.StackTrace}");
      }
    }

    public void UpdateVoxels()
    {
      try
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
      catch(Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in UpdateVoxels: {ex.Message}\n{ex.StackTrace}");
      }
    }

    void ApplyVoxelChanges() 
    {
      try
      {
        Vector3D minWorld, maxWorld;
        lock (_pendingLockObject)
        {
          minWorld = Vector3D.Transform(_pendingChanges.Value.Min - Planet.SizeInMetresHalf, Planet.WorldMatrix);
          maxWorld = Vector3D.Transform(_pendingChanges.Value.Max - Planet.SizeInMetresHalf, Planet.WorldMatrix);
          _pendingChanges = null;
        }

        if (!OBB.Contains(ref minWorld) && !OBB.Contains(ref maxWorld))
          return;

        var mapMin = Vector3I.Max(BoundingBox.Min, WorldToLocal(minWorld) - 3);
        var mapMax = Vector3I.Min(BoundingBox.Max, WorldToLocal(maxWorld) + 3);

        var iter = new Vector3I_RangeIterator(ref mapMin, ref mapMax);
        var current = iter.Current;

        while (iter.IsValid())
        {
          if (Planet == null || Planet.MarkedForClose)
            return;

          Node n;
          OpenTileDict.TryGetValue(current, out n);
          if (n != null && n.IsGridNodePlanetTile)
          {
            byte b;
            OpenTileDict.TryRemove(current, out n);
            ObstacleNodes.TryRemove(current, out b);
            Obstacles.TryRemove(current, out b);
          }

          iter.GetNext(out current);
        }

        float _;
        var gravityNorm = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out _);
        gravityNorm.Normalize();

        var upDir = Grid.WorldMatrix.GetClosestDirection(-gravityNorm);
        var upVec = Base6Directions.GetIntVector(upDir);
        var box = new BoundingBoxI(mapMin, mapMax);

        CheckForPlanetTiles(ref box, ref gravityNorm, ref upVec);
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in ApplyVoxelChanges: {ex.Message}\n{ex.StackTrace}");
      }
    }

    void CheckFaces(IMySlimBlock block, Vector3I normal, MyCubeBlockDefinition cubeDef = null)
    {
      if (cubeDef == null)
        cubeDef = block.BlockDefinition as MyCubeBlockDefinition;

      Dictionary<Vector3I, HashSet<Vector3I>> faceDict;
      if (!AiSession.Instance.BlockFaceDictionary.TryGetValue(cubeDef.Id, out faceDict))
      {
        AiSession.Instance.Logger.Log($"There was no cube face dictionary found for {cubeDef.Id} (Size = {cubeDef.CubeSize}, Grid = {block.CubeGrid.DisplayName}, Position = {block.Position})", MessageType.WARNING);
        return;
      }

      bool airTight = cubeDef.IsAirTight ?? false;
      bool allowSolar = !airTight && block.FatBlock is IMySolarPanel && Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) > 0;
      bool allowConn = !allowSolar && block.FatBlock is IMyShipConnector && cubeDef.Id.SubtypeName == "Connector";
      bool isFlatWindow = !allowConn && AiSession.Instance.FlatWindowDefinitions.ContainsItem(cubeDef.Id);
      bool isCylinder = !isFlatWindow && AiSession.Instance.PipeBlockDefinitions.ContainsItem(cubeDef.Id);
      bool isSlopeBlock = !isCylinder && AiSession.Instance.SlopeBlockDefinitions.Contains(cubeDef.Id)
        && !AiSession.Instance.SlopedHalfBlockDefinitions.Contains(cubeDef.Id)
        && !AiSession.Instance.HalfStairBlockDefinitions.Contains(cubeDef.Id);

      var grid = block.CubeGrid as MyCubeGrid;
      bool needsPositionAdjusted = grid.EntityId != Grid.EntityId;

      Matrix matrix = new Matrix
      {
        Forward = Base6Directions.GetVector(block.Orientation.Forward),
        Left = Base6Directions.GetVector(block.Orientation.Left),
        Up = Base6Directions.GetVector(block.Orientation.Up)
      };

      if (faceDict.Count < 2)
        matrix.TransposeRotationInPlace();

      Vector3I side, center = cubeDef.Center;
      Vector3I.TransformNormal(ref normal, ref matrix, out side);
      Vector3I.TransformNormal(ref center, ref matrix, out center);
      var adjustedPosition = block.Position - center;

      foreach (var kvp in faceDict)
      {
        var cell = kvp.Key;
        Vector3I.TransformNormal(ref cell, ref matrix, out cell);
        var positionAbove = adjustedPosition + cell + normal;
        var mainGridPosition = needsPositionAdjusted ? Grid.WorldToGridInteger(grid.GridIntegerToWorld(positionAbove)) : positionAbove;
        var cubeAbove = grid.GetCubeBlock(positionAbove) as IMySlimBlock;
        var cubeAboveDef = cubeAbove?.BlockDefinition as MyCubeBlockDefinition;
        bool cubeAboveEmpty = cubeAbove == null || !cubeAboveDef.HasPhysics;
        bool checkAbove = airTight || allowConn || allowSolar || isCylinder || (kvp.Value?.Contains(side) ?? false);

        if (cubeAboveEmpty)
        {
          if (checkAbove)
          {
            OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, null);
          }
          else if (isFlatWindow)
          {
            if (block.BlockDefinition.Id.SubtypeName == "LargeWindowSquare")
            {
              if (Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) > 0)
                OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, null);
            }
            else if (Base6Directions.GetIntVector(block.Orientation.Left).Dot(ref normal) < 0)
            {
              OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, null);
            }
            else if (block.BlockDefinition.Id.SubtypeName.StartsWith("HalfWindowCorner")
              && Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) > 0)
            {
              OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, null);
            }
          }
          else if (isSlopeBlock)
          {
            var leftVec = Base6Directions.GetIntVector(block.Orientation.Left);
            if (leftVec.Dot(ref normal) == 0)
              return;

            var node = new Node(mainGridPosition, grid, null);
            var up = Base6Directions.GetIntVector(block.Orientation.Up);
            var bwd = -Base6Directions.GetIntVector(block.Orientation.Forward);
            node.AddBlockage(up);
            node.AddBlockage(bwd);

            OpenTileDict[mainGridPosition] = node;
          }
        }
        else if (checkAbove)
        {
          if (cubeAbove.BlockDefinition.Id.SubtypeName.StartsWith("DeadBody"))
          {
            OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, cubeAbove);
          }
          else if (cubeAbove.BlockDefinition.Id.SubtypeName == "RoboFactory")
          {
            if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) > 0 && positionAbove != cubeAbove.Position)
              OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, cubeAbove);
          }
          else if (AiSession.Instance.DecorativeBlockDefinitions.ContainsItem(cubeAbove.BlockDefinition.Id))
          {
            if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) > 0)
              OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, cubeAbove);
          }
          else if (AiSession.Instance.RailingBlockDefinitions.ContainsItem(cubeAboveDef.Id))
          {
            OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, cubeAbove);
          }
          else if (cubeAboveDef.Id.SubtypeName.StartsWith("LargeCoverWall") || cubeAboveDef.Id.SubtypeName.StartsWith("FireCover"))
          {
            OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, cubeAbove);
          }
          else if (AiSession.Instance.LockerDefinitions.ContainsItem(cubeAboveDef.Id))
          {
            if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) > 0)
              OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, cubeAbove);
          }
          else if (AiSession.Instance.ArmorPanelAllDefinitions.Contains(cubeAboveDef.Id))
          {
            if (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(cubeAboveDef.Id)
              || AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Left).Dot(ref normal) == 0)
                OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, cubeAbove);
            }
            else
              OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, cubeAbove);
          }
          else if (AiSession.Instance.FreightBlockDefinitions.ContainsItem(cubeAboveDef.Id))
          {
            OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, cubeAbove);
          }
          else if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(cubeAboveDef.Id))
          {
            var turretBasePosition = cubeAbove.Position - Base6Directions.GetIntVector(cubeAbove.Orientation.Up);
            if (turretBasePosition != positionAbove || cubeAboveDef.Id.TypeId == typeof(MyObjectBuilder_InteriorTurret))
              OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, cubeAbove);
          }
          else if (AiSession.Instance.CatwalkBlockDefinitions.Contains(cubeAboveDef.Id))
          {
            OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, cubeAbove);
          }
          else if (AiSession.Instance.FlatWindowDefinitions.ContainsItem(cubeAboveDef.Id))
          {
            OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, cubeAbove);
          }
          else if (cubeAbove.FatBlock != null)
          {
            var hangar = cubeAbove.FatBlock as IMyAirtightHangarDoor;
            if (hangar != null)
            {
              if (positionAbove != hangar.Position)
                OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, cubeAbove);
            }
            else if (cubeAbove.FatBlock is IMyTextPanel || cubeAbove.FatBlock is IMySolarPanel || cubeAbove.FatBlock is IMyButtonPanel)
            {
              OpenTileDict[mainGridPosition] = new Node(mainGridPosition, grid, cubeAbove);
            }
          }
        }
      }
    }

    public override IEnumerable<Vector3I> GetBlockedNodeEdges(NodeBase node)
    {
      if (node?.Grid == null || node.Grid.MarkedForClose)
      {
        Dirty = true;
        yield break;
      }

      var upDir = Grid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
      var upVec = Base6Directions.GetIntVector(upDir);

      Vector3I nodePos;
      if (node.Grid == Grid)
        nodePos = node.Position;
      else
        nodePos = node.Grid.WorldToGridInteger(Grid.GridIntegerToWorld(node.Position));

      var thisBlock = node.Block;
      var blockBelowThis = node.Grid.GetCubeBlock(nodePos - upVec) as IMySlimBlock;
      bool thisBlockEmpty = thisBlock == null || !((MyCubeBlockDefinition)thisBlock.BlockDefinition).HasPhysics;
      bool thisIsDoor = false, thisIsSlope = false, thisIsHalfSlope = false, thisIsHalfStair = false, thisIsRamp = false, thisIsSolar = false;
      bool thisIsCatwalk = false, thisIsBtnPanel = false, thisIsLadder = false, thisIsWindowFlat = false, thisIsWindowSlope = false;
      bool thisIsHalfBlock = false, thisIsCoverWall = false, thisIsFreight = false, thisIsRailing = false, thisIsPassage = false;
      bool thisIsPanel = false, thisIsFullPanel = false, thisIsHalfPanel = false, thisIsSlopePanel = false, thisIsHalfSlopePanel = false;
      bool thisIsLocker = false, thisIsBeam = false, thisIsDeadBody = false, thisIsDeco = false, belowThisIsPanelSlope = false;
      bool belowThisIsHalfSlope = false, belowThisIsSlope = false, belowThisIsRamp = false;

      if (!thisBlockEmpty)
      {
        var def = thisBlock.BlockDefinition.Id;
        thisIsDoor = thisBlock.FatBlock is IMyDoor;
        thisIsSolar = !thisIsDoor && thisBlock.FatBlock is IMySolarPanel;
        thisIsDeadBody = !thisIsSolar && def.SubtypeName.StartsWith("DeadBody");
        thisIsFreight = !thisIsDeadBody && AiSession.Instance.FreightBlockDefinitions.ContainsItem(def);
        thisIsSlope = !thisIsFreight && AiSession.Instance.SlopeBlockDefinitions.Contains(def);
        thisIsHalfStair = thisIsSlope && AiSession.Instance.HalfStairBlockDefinitions.Contains(def);
        thisIsHalfSlope = thisIsSlope && !thisIsHalfStair && AiSession.Instance.SlopedHalfBlockDefinitions.Contains(def);
        thisIsRamp = !thisIsSlope && AiSession.Instance.RampBlockDefinitions.Contains(def);
        thisIsCatwalk = !thisIsRamp && AiSession.Instance.CatwalkBlockDefinitions.Contains(def);
        thisIsBtnPanel = !thisIsCatwalk && AiSession.Instance.BtnPanelDefinitions.ContainsItem(def);
        thisIsLadder = !thisIsBtnPanel && AiSession.Instance.LadderBlockDefinitions.Contains(def);
        thisIsWindowFlat = !thisIsLadder && AiSession.Instance.FlatWindowDefinitions.ContainsItem(def);
        thisIsWindowSlope = !thisIsWindowFlat && AiSession.Instance.AngledWindowDefinitions.ContainsItem(def);
        thisIsCoverWall = !thisIsWindowSlope && (def.SubtypeName.StartsWith("LargeCoverWall") || def.SubtypeName.StartsWith("FireCover"));
        thisIsHalfBlock = !thisIsCoverWall && def.SubtypeName.EndsWith("HalfArmorBlock");
        thisIsRailing = !thisIsHalfBlock && AiSession.Instance.RailingBlockDefinitions.ContainsItem(def);
        thisIsLocker = !thisIsRailing && AiSession.Instance.LockerDefinitions.ContainsItem(def);
        thisIsBeam = !thisIsLocker && AiSession.Instance.BeamBlockDefinitions.ContainsItem(def);
        thisIsPassage = !thisIsBeam && AiSession.Instance.PassageBlockDefinitions.Contains(def);
        thisIsPanel = !thisIsPassage && AiSession.Instance.ArmorPanelAllDefinitions.Contains(def);
        thisIsDeco = !thisIsPanel && AiSession.Instance.DecorativeBlockDefinitions.ContainsItem(def);

        if (thisIsPanel)
        {
          thisIsFullPanel = AiSession.Instance.ArmorPanelFullDefinitions.ContainsItem(def);
          thisIsHalfPanel = !thisIsFullPanel && AiSession.Instance.ArmorPanelHalfDefinitions.ContainsItem(def);
          thisIsSlopePanel = !thisIsHalfPanel && AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(def);
          thisIsHalfSlopePanel = !thisIsSlopePanel && AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(def);
        }
      }

      if (blockBelowThis != null)
      {
        var def = blockBelowThis.BlockDefinition.Id;
        belowThisIsRamp = AiSession.Instance.RampBlockDefinitions.Contains(def);
        belowThisIsHalfSlope = !belowThisIsRamp && AiSession.Instance.SlopedHalfBlockDefinitions.Contains(def);
        belowThisIsPanelSlope = !belowThisIsRamp && !belowThisIsHalfSlope
          && (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(def)
          || AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(def));
        belowThisIsSlope = !belowThisIsPanelSlope
          && (belowThisIsRamp
          || AiSession.Instance.AngledWindowDefinitions.ContainsItem(def)
          || (AiSession.Instance.SlopeBlockDefinitions.Contains(def)
          && !belowThisIsHalfSlope
          && !AiSession.Instance.HalfStairBlockDefinitions.Contains(def)));
      }

      foreach (var dirVec in AiSession.Instance.CardinalDirections)
      {
        var next = nodePos + dirVec;
        Node nextNode;
        if (!OpenTileDict.TryGetValue(next, out nextNode))
        {
          yield return dirVec;
          continue;
        }

        IMySlimBlock nextBlock = null, blockBelowNext = null;
        if (nextNode != null)
        {
          nextBlock = nextNode.Block;
          blockBelowNext = nextNode.Grid?.GetCubeBlock(next - upVec);
        }

        bool nextBlockEmpty = nextBlock == null || !((MyCubeBlockDefinition)nextBlock.BlockDefinition).HasPhysics;
        bool nextisDoor = false, nextIsSlope = false, nextIsHalfSlope = false, nextIsHalfStair = false, nextIsRamp = false, nextIsSolar = false;
        bool nextIsCatwalk = false, nextIsBtnPanel = false, nextIsLadder = false, nextIsWindowFlat = false, nextIsWindowSlope = false;
        bool nextIsHalfBlock = false, nextIsCoverWall = false, nextIsFreight = false, nextIsRailing = false, nextIsPassage = false;
        bool nextIsPanel = false, nextIsFullPanel = false, nextIsHalfPanel = false, nextIsSlopePanel = false, nextIsHalfSlopePanel = false;
        bool nextIsLocker = false, nextIsBeam = false, nextIsDeadBody = false, nextIsDeco = false, belowNextIsPanelSlope = false;
        bool belowNextIsHalfSlope = false, belowNextIsSlope = false, belowNextIsRamp = false;

        var dir = Base6Directions.GetDirection(dirVec);
        var oppositeDir = Base6Directions.GetOppositeDirection(dir);

        if (!nextBlockEmpty)
        {
          var def = nextBlock.BlockDefinition.Id;
          nextisDoor = nextBlock.FatBlock is IMyDoor;
          nextIsSolar = !nextisDoor && nextBlock.FatBlock is IMySolarPanel;
          nextIsDeadBody = !nextIsSolar && def.SubtypeName.StartsWith("DeadBody");
          nextIsFreight = !nextIsDeadBody && AiSession.Instance.FreightBlockDefinitions.ContainsItem(def);
          nextIsSlope = !nextIsFreight && AiSession.Instance.SlopeBlockDefinitions.Contains(def);
          nextIsHalfStair = nextIsSlope && AiSession.Instance.HalfStairBlockDefinitions.Contains(def);
          nextIsHalfSlope = nextIsSlope && !nextIsHalfStair && AiSession.Instance.SlopedHalfBlockDefinitions.Contains(def);
          nextIsRamp = AiSession.Instance.RampBlockDefinitions.Contains(def);
          nextIsCatwalk = !nextIsRamp && AiSession.Instance.CatwalkBlockDefinitions.Contains(def);
          nextIsBtnPanel = !nextIsCatwalk && AiSession.Instance.BtnPanelDefinitions.ContainsItem(def);
          nextIsLadder = !nextIsBtnPanel && AiSession.Instance.LadderBlockDefinitions.Contains(def);
          nextIsWindowFlat = !nextIsLadder && AiSession.Instance.FlatWindowDefinitions.ContainsItem(def);
          nextIsWindowSlope = !nextIsWindowFlat && AiSession.Instance.AngledWindowDefinitions.ContainsItem(def);
          nextIsCoverWall = !nextIsWindowSlope && (def.SubtypeName.StartsWith("LargeCoverWall") || def.SubtypeName.StartsWith("FireCover"));
          nextIsHalfBlock = !nextIsCoverWall && def.SubtypeName.EndsWith("HalfArmorBlock");
          nextIsRailing = !nextIsHalfBlock && AiSession.Instance.RailingBlockDefinitions.ContainsItem(def);
          nextIsLocker = !nextIsRailing && AiSession.Instance.LockerDefinitions.ContainsItem(def);
          nextIsBeam = !nextIsLocker && AiSession.Instance.BeamBlockDefinitions.ContainsItem(def);
          nextIsPassage = !nextIsBeam && AiSession.Instance.PassageBlockDefinitions.Contains(def);
          nextIsPanel = !nextIsPassage && AiSession.Instance.ArmorPanelAllDefinitions.Contains(def);
          nextIsDeco = !nextIsPanel && AiSession.Instance.DecorativeBlockDefinitions.ContainsItem(def);

          if (nextIsPanel)
          {
            nextIsFullPanel = AiSession.Instance.ArmorPanelFullDefinitions.ContainsItem(def);
            nextIsHalfPanel = !nextIsFullPanel && AiSession.Instance.ArmorPanelHalfDefinitions.ContainsItem(def);
            nextIsSlopePanel = !nextIsHalfPanel && AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(def);
            nextIsHalfSlopePanel = !nextIsSlopePanel && AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(def);
          }
        }
        
        if (blockBelowNext != null)
        {
          var def = blockBelowNext.BlockDefinition.Id;
          belowNextIsRamp = AiSession.Instance.RampBlockDefinitions.Contains(def);
          belowNextIsHalfSlope = !belowNextIsRamp && AiSession.Instance.SlopedHalfBlockDefinitions.Contains(def);
          belowNextIsPanelSlope = !belowNextIsRamp && !belowNextIsHalfSlope 
            && (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(def)
            || AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(def));
          belowNextIsSlope = !belowNextIsPanelSlope
            && (belowNextIsRamp
            || AiSession.Instance.AngledWindowDefinitions.ContainsItem(def)
            || (AiSession.Instance.SlopeBlockDefinitions.Contains(def)
            && !belowNextIsHalfSlope
            && !AiSession.Instance.HalfStairBlockDefinitions.Contains(def)));
        }

        if (node.IsGridNodePlanetTile && !nextNode.IsGridNodePlanetTile)
        {
          if (!nextBlockEmpty)
          {
            var nextOr = nextBlock.Orientation;
            var downDir = Base6Directions.GetOppositeDirection(upDir);

            if (nextIsSlope || nextIsHalfSlope || nextIsHalfStair)
            {
              if (nextOr.Up == upDir)
              {
                if (dir != nextBlock.Orientation.Forward)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (nextOr.Forward == downDir)
              {
                if (oppositeDir != nextOr.Up)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else
              {
                yield return dirVec;
                continue;
              }
            }
            else if (nextIsSlopePanel || nextIsHalfSlopePanel)
            {
              if (nextOr.Forward == upDir)
              {
                if (oppositeDir != nextOr.Up)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (nextOr.Forward == downDir)
              {
                if (dir != nextOr.Up)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (nextOr.Up == downDir)
              {
                if (dir != nextOr.Forward)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (nextOr.Up == upDir)
              {
                if (oppositeDir != nextOr.Forward)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else
              {
                yield return dirVec;
                continue;
              }
            }
            else if (nextIsRamp)
            {
              if (nextOr.Up == upDir)
              {
                if (oppositeDir != nextOr.Forward)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (nextOr.Forward == upDir)
              {
                if (oppositeDir != nextOr.Up)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else
              {
                yield return dirVec;
                continue;
              }
            }
            else if (nextIsWindowSlope)
            {
              if (nextOr.Up == upDir)
              {
                if (oppositeDir != nextOr.Forward)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (nextOr.Up == downDir)
              {
                if (dir != nextOr.Forward)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (nextOr.Forward == upDir)
              {
                if (oppositeDir != nextOr.Up)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (nextOr.Forward == downDir)
              {
                if (dir != nextOr.Up)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else
              {
                yield return dirVec;
                continue;
              }
            }
            else if (nextIsLadder)
            {
              if (nextOr.Up != upDir && nextOr.Up != downDir)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (nextIsBeam)
            {
              var blockSubtype = nextBlock.BlockDefinition.Id.SubtypeName;
              if (blockSubtype.IndexOf("Slope", StringComparison.OrdinalIgnoreCase) >= 0)
              {
                if (blockSubtype.EndsWith("HalfSlope"))
                {
                  if (nextOr.Left == downDir)
                  {
                    if (oppositeDir != nextOr.Forward)
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                  else if (nextOr.Forward == upDir)
                  {
                    if (dir != nextOr.Left)
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                  else
                  {
                    yield return dirVec;
                    continue;
                  }
                }
                else if (blockSubtype.EndsWith("BeamBlockSlope") || blockSubtype.IndexOf("2x1") >= 0)
                {
                  if (nextOr.Left == upDir)
                  {
                    if (oppositeDir != nextOr.Forward)
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                  else if (nextOr.Forward == upDir)
                  {
                    if (oppositeDir != nextOr.Left)
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                }
                else
                {
                  yield return dirVec;
                  continue;
                }
              }
              else
              {
                yield return dirVec;
                continue;
              }
            }
            else
            {
              yield return dirVec;
              continue;
            }
          }
          else if (dirVec.RectangularLength() > 1)
          {
            yield return dirVec;
            continue;
          }
        }

        if (!thisBlockEmpty)
        {
          if (thisIsDeadBody)
          {
            if (CheckCatwalkForRails(thisBlock, dirVec))
            {
              yield return dirVec;
              continue;
            }
          }
          else if (thisIsDoor)
          {
            if (dir != thisBlock.Orientation.Forward && oppositeDir != thisBlock.Orientation.Forward)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (thisIsDeco)
          {
            var def = thisBlock.BlockDefinition.Id;
            var subtype = def.SubtypeName;
            if (subtype == "LargeBlockPlanters" || subtype == "LargeBlockKitchen" || subtype == "LargeBlockBarCounter"
              || subtype == "LargeBlockDesk" || subtype == "LargeBlockDeskChairless")
            {
              if (dir == thisBlock.Orientation.Forward)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (subtype == "LargeBlockCouch")
            {
              if (oppositeDir == thisBlock.Orientation.Forward)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (subtype == "LargeBlockCouchCorner")
            {
              if (dir == thisBlock.Orientation.Left || oppositeDir == thisBlock.Orientation.Forward)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (subtype == "LargeBlockBarCounterCorner")
            {
              if (oppositeDir == thisBlock.Orientation.Left || dir == thisBlock.Orientation.Forward)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (subtype == "Shower")
            {
              if (oppositeDir == thisBlock.Orientation.Forward || oppositeDir == thisBlock.Orientation.Left)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (subtype.EndsWith("Toilet"))
            {
              if (oppositeDir == thisBlock.Orientation.Left)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (subtype == "Jukebox" || subtype == "AtmBlock" || subtype == "FoodDispenser" || subtype == "VendingMachine")
            {
              if (dir == thisBlock.Orientation.Left)
              {
                yield return dirVec;
                continue;
              }
            }
          }
          else if (thisIsSlope)
          {
            if (thisBlock.BlockDefinition.Id.SubtypeName == "GratedStairs")
            {
              if (dir == thisBlock.Orientation.Left || oppositeDir == thisBlock.Orientation.Left)
              {
                yield return dirVec;
                continue;
              }
            }

            if (thisIsHalfSlope)
            {
              if (!nextIsHalfBlock && dir != thisBlock.Orientation.Forward && oppositeDir != thisBlock.Orientation.Forward)
              {
                if ((belowThisIsRamp || belowThisIsSlope || belowThisIsHalfSlope) && ExemptNodesUpper.Contains(nodePos))
                {
                  var nextIsBlockBelow = next == blockBelowThis.Position;

                  if (!nextIsBlockBelow)
                  {
                    var dotUp = Base6Directions.GetIntVector(blockBelowThis.Orientation.Up).Dot(ref upVec);
                    var dotFwd = Base6Directions.GetIntVector(blockBelowThis.Orientation.Forward).Dot(ref upVec);
                    bool valid = true;

                    if (dotUp > 0)
                    {
                      if (dir != blockBelowThis.Orientation.Forward
                        && oppositeDir != blockBelowThis.Orientation.Forward
                        && oppositeDir != blockBelowThis.Orientation.Up)
                      {
                        valid = false;
                      }
                    }
                    else if (dotFwd > 0)
                    {
                      if (dir != blockBelowThis.Orientation.Up
                        && oppositeDir != blockBelowThis.Orientation.Up
                        && oppositeDir != blockBelowThis.Orientation.Forward)
                      {
                        valid = false;
                      }
                    }
                    else if (dotFwd < 0 && belowThisIsHalfSlope)
                    {
                      if (dir != blockBelowThis.Orientation.Up
                        && oppositeDir != blockBelowThis.Orientation.Up
                        && dir != blockBelowThis.Orientation.Forward)
                      {
                        valid = false;
                      }
                    }
                    else
                    {
                      valid = false;
                    }
                    
                    if (!valid)
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                }
                else if (ExemptNodesSide.Contains(nodePos))
                {
                  if (dir != thisBlock.Orientation.Up && dir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Left)
                  {
                    yield return dirVec;
                    continue;
                  }
                }
                else
                {
                  yield return dirVec;
                  continue;
                }
              }
            }
            else
            {
              bool thisIsSlopeBase = thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("BlockArmorSlope2Base");
              if (nextIsHalfBlock && thisIsSlopeBase)
              {
                if (upDir != thisBlock.Orientation.Up || oppositeDir != thisBlock.Orientation.Forward)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (nextIsHalfSlope)
              {
                if (!ExemptNodesUpper.Contains(next) && dir != thisBlock.Orientation.Up && oppositeDir != thisBlock.Orientation.Forward)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (dir != thisBlock.Orientation.Up)
              {
                if (oppositeDir != thisBlock.Orientation.Forward)
                {
                  if (!thisIsHalfStair || oppositeDir != thisBlock.Orientation.Up)
                  {
                    yield return dirVec;
                    continue;
                  }
                }
                else if (!nextBlockEmpty)
                {
                  if (nextBlock?.FatBlock is IMyTextPanel)
                  {
                    if (oppositeDir == nextBlock.Orientation.Forward && nextBlock.BlockDefinition.Id.SubtypeName.IndexOf("corner") < 0)
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                  else if (!ExemptNodesUpper.Contains(next) && !ExemptNodesUpper.Contains(nodePos) && !nextIsCatwalk && !nextIsRailing
                    && (!nextIsBtnPanel || oppositeDir == nextBlock.Orientation.Forward))
                  {
                    yield return dirVec;
                    continue;
                  }
                }
              }

              if (dir == thisBlock.Orientation.Up && !nextBlockEmpty && nextBlock != thisBlock)
              {
                if (nextIsHalfStair)
                {
                  if (thisBlock.Orientation.Up != nextBlock.Orientation.Up)
                  {
                    yield return dirVec;
                    continue;
                  }
                }
                else if (!nextIsBtnPanel || oppositeDir == nextBlock.Orientation.Forward)
                {
                  if (!ExemptNodesUpper.Contains(next) && (oppositeDir != upDir || !ExemptNodesUpper.Contains(nodePos)))
                  {
                    if (dir != thisBlock.Orientation.Up || !ExemptNodesSide.Contains(next))
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                }
              }
            }
          }
          else if (thisIsPanel)
          {
            var blockLeft = thisBlock.Orientation.Left;
            var blockFwd = thisBlock.Orientation.Forward;
            var blockUp = thisBlock.Orientation.Up;

            if (thisIsFullPanel)
            {
              if (oppositeDir == blockLeft)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (thisIsHalfPanel)
            {
              if (oppositeDir == blockLeft && thisBlock.BlockDefinition.Id.SubtypeName.StartsWith("LargeArmorHalf"))
              {
                if (Base6Directions.GetIntVector(blockUp).Dot(ref upVec) != 0)
                {
                  yield return dirVec;
                  continue;
                }
              }
            }
            else if (thisIsSlopePanel)
            {
              var blockSubtype = thisBlock.BlockDefinition.Id.SubtypeName;
              var downDir = Base6Directions.GetOppositeDirection(upDir);
              bool isSlope = blockSubtype.StartsWith("LargeArmor2x1SlopedPanel");
              bool isSlopeTip = isSlope && blockSubtype.StartsWith("LargeArmor2x1SlopedPanelTip");
              bool isBase = isSlope && !isSlopeTip;
              bool isDown = dir == downDir;

              if (isDown || (dir != blockUp && dir != blockFwd))
              {
                if (isSlopeTip)
                {
                  var positionBelow = nodePos - upVec;
                  var blockBelow = thisBlock.CubeGrid.GetCubeBlock(positionBelow);

                  if (blockBelow == null || blockBelow.Orientation != thisBlock.Orientation)
                  {
                    yield return dirVec;
                    continue;
                  }

                  var blockBelowSubtype = blockBelow.BlockDefinition.Id.SubtypeName;
                  if (blockBelowSubtype == blockSubtype || !blockBelowSubtype.StartsWith("LargeArmor2x1SlopedPanel"))
                  {
                    yield return dirVec;
                    continue;
                  }
                }
                else if (!isDown && blockSubtype.StartsWith("LargeArmorSlopedPanel"))
                {
                  if (blockFwd == downDir || blockUp == downDir)
                  {
                    if (oppositeDir != blockFwd && oppositeDir != blockUp)
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                }
                else
                {
                  yield return dirVec;
                  continue;
                }
              }

              if (isBase)
              {
                if (dir == blockUp && Base6Directions.GetIntVector(blockUp).Dot(ref upVec) != 0)
                {
                  var positionAbove = nodePos + upVec;
                  var blockAbove = thisBlock.CubeGrid.GetCubeBlock(positionAbove);
                  if (blockAbove == null || blockAbove.Orientation != thisBlock.Orientation)
                  {
                    yield return dirVec;
                    continue;
                  }

                  var blockAboveSubtype = blockAbove.BlockDefinition.Id.SubtypeName;
                  if (!blockAboveSubtype.StartsWith("LargeArmor2x1SlopedPanelTip"))
                  {
                    yield return dirVec;
                    continue;
                  }
                }
              }
            }
            else if (thisIsHalfSlopePanel)
            {
              if (dir != blockUp && dir != blockFwd)
              {
                yield return dirVec;
                continue;
              }

              var blockSubtype = thisBlock.BlockDefinition.Id.SubtypeName;
              if (blockSubtype.StartsWith("LargeArmor2x1HalfSlopedPanel") && dir == blockUp && Base6Directions.GetIntVector(blockUp).Dot(ref upVec) != 0)
              {
                var positionAbove = nodePos + upVec;
                var blockAbove = thisBlock.CubeGrid.GetCubeBlock(positionAbove);
                if (blockAbove == null || blockAbove.Orientation != thisBlock.Orientation)
                {
                  yield return dirVec;
                  continue;
                }

                var blockAboveSubtype = blockAbove.BlockDefinition.Id.SubtypeName;
                if (!blockAboveSubtype.StartsWith("LargeArmor2x1HalfSlopedTip"))
                {
                  yield return dirVec;
                  continue;
                }

                var tail = blockSubtype.Substring(blockSubtype.Length - 4);
                if (!blockAboveSubtype.EndsWith(tail))
                {
                  yield return dirVec;
                  continue;
                }
              }
            }
            else // miscellaneous panels
            {
              if (thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Inv"))
              {
                if (dir == blockLeft)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (oppositeDir == blockLeft)
              {
                yield return dirVec;
                continue;
              }
            }
          }
          else if (thisIsLocker)
          {
            var blockSubtype = thisBlock.BlockDefinition.Id.SubtypeName;
            if (blockSubtype == "LargeBlockLockerRoom")
            {
              if (oppositeDir != thisBlock.Orientation.Forward)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (blockSubtype == "LargeBlockLockerRoomCorner")
            {
              if (dir == thisBlock.Orientation.Forward || oppositeDir == thisBlock.Orientation.Left)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (dir == thisBlock.Orientation.Forward)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (thisIsPassage)
          {
            var blockSubtype = thisBlock.BlockDefinition.Id.SubtypeName;
            var blockFwd = thisBlock.Orientation.Forward;
            var blockUp = thisBlock.Orientation.Up;

            if (thisBlock.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Passage))
            {
              if (dir != blockUp && oppositeDir != blockUp)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (dir == blockUp || oppositeDir == blockUp)
            {
              yield return dirVec;
              continue;
            }
            else if (blockSubtype.EndsWith("Wall") || blockSubtype.EndsWith("Tjunction"))
            {
              if (dir != blockFwd && oppositeDir != blockFwd && oppositeDir != thisBlock.Orientation.Left)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (blockSubtype.EndsWith("Corner"))
            {
              if (oppositeDir != blockFwd && oppositeDir != thisBlock.Orientation.Left)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (!blockSubtype.EndsWith("Intersection") && dir != blockFwd && oppositeDir != blockFwd)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (thisIsBeam)
          {
            var blockSubtype = thisBlock.BlockDefinition.Id.SubtypeName;
            var blockLeft = thisBlock.Orientation.Left;
            var blockFwd = thisBlock.Orientation.Forward;

            if (blockSubtype.EndsWith("Slope"))
            {
              if (!blockSubtype.EndsWith("HalfSlope") && dir != blockLeft && oppositeDir != blockLeft)
              {
                yield return dirVec;
                continue;
              }                
            }
            else if (blockSubtype.EndsWith("End"))
            {
              if (oppositeDir != blockFwd)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (blockSubtype.EndsWith("TJunction"))
            {
              if (dir != blockFwd)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (dir != blockFwd && oppositeDir != blockFwd)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (thisIsFreight)
          {
            if (thisBlock.BlockDefinition.Id.SubtypeName != "Freight1" && oppositeDir == thisBlock.Orientation.Forward)
            {
              yield return dirVec;
              continue;
            }
            else if (nextIsFreight)
            {
              if (thisBlock.Orientation.Up != nextBlock.Orientation.Up)
              {
                yield return dirVec;
                continue;
              }
              else if (thisBlock.Orientation.Forward != nextBlock.Orientation.Forward)
              {
                var opp = Base6Directions.GetOppositeDirection(thisBlock.Orientation.Forward);
                var rightVec = -Base6Directions.GetIntVector(thisBlock.Orientation.Left);

                if (opp == nextBlock.Orientation.Forward && rightVec == (next - nodePos))
                {
                  yield return dirVec;
                  continue;
                }
              }
            }
          }
          else if (thisIsHalfBlock)
          {
            if (dir == thisBlock.Orientation.Forward || oppositeDir == thisBlock.Orientation.Forward)
            {
              yield return dirVec;
              continue;
            }

            if (!nextBlockEmpty)
            {
              var downDir = Base6Directions.GetOppositeDirection(upDir);
     
              if (nextIsHalfBlock && downDir == thisBlock.Orientation.Forward && downDir != nextBlock.Orientation.Forward)
              {
                yield return dirVec;
                continue;
              }
            }
          }
          else if (thisIsCoverWall)
          {
            var thisBlockSubtype = thisBlock.BlockDefinition.Id.SubtypeName;
            if (oppositeDir == thisBlock.Orientation.Forward && !thisBlockSubtype.EndsWith("Half"))
            {
              yield return dirVec;
              continue;
            }

            if (oppositeDir == thisBlock.Orientation.Left && thisBlockSubtype.EndsWith("Corner"))
            {
              yield return dirVec;
              continue;
            }
          }
          else if (thisIsRamp)
          {
            if (dir != thisBlock.Orientation.Up)
            {
              if (dir != thisBlock.Orientation.Forward)
              {
                if (oppositeDir != thisBlock.Orientation.Forward && !ExemptNodesSide.Contains(nodePos))
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (!nextBlockEmpty && (!nextIsBtnPanel || oppositeDir == nextBlock.Orientation.Forward))
              {
                if (!nextIsRamp || nextBlock != thisBlock || dir != thisBlock.Orientation.Forward)
                {
                  if (!ExemptNodesUpper.Contains(next) && !ExemptNodesUpper.Contains(nodePos))
                  {
                    if (!nextIsDeadBody || CheckCatwalkForRails(nextBlock, -dirVec))
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                }
              }
            }
            else if (!nextBlockEmpty && nextBlock != thisBlock)
            {
              if (!nextIsCatwalk && (!nextIsBtnPanel || oppositeDir == nextBlock.Orientation.Forward))
              {
                if (!ExemptNodesUpper.Contains(next) && (!ExemptNodesUpper.Contains(nodePos) || blockBelowThis != nextBlock))
                {
                  if ((!ExemptNodesSide.Contains(next) && !ExemptNodesSide.Contains(nodePos)) || dir != thisBlock.Orientation.Up)
                  {
                    yield return dirVec;
                    continue;
                  }
                }
              }
            }
          }
          else if (thisIsCatwalk)
          {
            if (oppositeDir == thisBlock.Orientation.Up)
            {
              yield return dirVec;
              continue;
            }
            else if (CheckCatwalkForRails(thisBlock, dirVec))
            {
              yield return dirVec;
              continue;
            }
          }
          else if (thisIsRailing)
          {
            // added the rail sides to the catwalk rail array bc lazy..
            if (CheckCatwalkForRails(thisBlock, dirVec))
            {
              yield return dirVec;
              continue;
            }
          }
          else if (thisIsWindowFlat)
          {
            Base6Directions.Direction sideWithPane;
            if (thisBlock.BlockDefinition.Id.SubtypeName == "LargeWindowSquare")
              sideWithPane = thisBlock.Orientation.Forward;
            else
              sideWithPane = Base6Directions.GetOppositeDirection(thisBlock.Orientation.Left);

            if (dir == sideWithPane || (thisBlock.BlockDefinition.Id.SubtypeName.StartsWith("HalfWindowCorner") && dir == thisBlock.Orientation.Forward))
            {
              yield return dirVec;
              continue;
            }
          }
          else if (thisIsSolar)
          {
            if (dir == thisBlock.Orientation.Forward)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (thisIsWindowSlope)
          {
            if (thisBlock.BlockDefinition.Id.SubtypeName == "LargeWindowEdge") // can only walk up one side
            {
              bool dirIsUp = dir == thisBlock.Orientation.Up;

              if (!dirIsUp)
              {
                if (dir != thisBlock.Orientation.Forward)
                {
                  yield return dirVec;
                  continue;
                }
                else if (!nextBlockEmpty)
                {
                  if (nextBlock?.FatBlock is IMyTextPanel)
                  {
                    if (oppositeDir == nextBlock.Orientation.Forward && nextBlock.BlockDefinition.Id.SubtypeName.IndexOf("corner") < 0)
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                  else if (nextIsCatwalk)
                  {
                    if (dir == nextBlock.Orientation.Up || CheckCatwalkForRails(nextBlock, -dirVec))
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                  else if (nextIsRailing)
                  {
                    if (CheckCatwalkForRails(nextBlock, -dirVec))
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                  else if (!nextIsBtnPanel || oppositeDir == nextBlock.Orientation.Forward)
                  {
                    yield return dirVec;
                    continue;
                  }
                }
              }
              else if (!nextBlockEmpty)
              {
                if (nextBlock?.FatBlock is IMyTextPanel)
                {
                  if (oppositeDir == nextBlock.Orientation.Forward && nextBlock.BlockDefinition.Id.SubtypeName.IndexOf("corner") < 0)
                  {
                    yield return dirVec;
                    continue;
                  }
                }
                else if (nextIsCatwalk)
                {
                  if (dir == nextBlock.Orientation.Up || CheckCatwalkForRails(nextBlock, -dirVec))
                  {
                    yield return dirVec;
                    continue;
                  }
                }
                else if (nextIsRailing)
                {
                  if (CheckCatwalkForRails(nextBlock, -dirVec))
                  {
                    yield return dirVec;
                    continue;
                  }
                }
                else if (!nextIsBtnPanel || oppositeDir == nextBlock.Orientation.Forward)
                {
                  yield return dirVec;
                  continue;
                }
              }
            }
            else // can walk up any side
            {
              var downDir = Base6Directions.GetOppositeDirection(upDir);

              if (upDir == thisBlock.Orientation.Up)
              {
                if (dir != upDir)
                {
                  if (dir != thisBlock.Orientation.Forward)
                  {
                    yield return dirVec;
                    continue;
                  }
                  else if (!nextBlockEmpty && (!nextIsBtnPanel || dir == nextBlock.Orientation.Forward))
                  {
                    yield return dirVec;
                    continue;
                  }
                }
                else if (!nextBlockEmpty && (!nextIsBtnPanel || dir == nextBlock.Orientation.Forward))
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (upDir == thisBlock.Orientation.Forward)
              {
                if (dir != upDir)
                {
                  if (dir != thisBlock.Orientation.Up)
                  {
                    if (nextBlockEmpty || nextBlock != thisBlock || dir != downDir)
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                  else if (!nextBlockEmpty && (!nextIsBtnPanel || dir == nextBlock.Orientation.Forward))
                  {
                    yield return dirVec;
                    continue;
                  }
                }
                else if (!nextBlockEmpty)
                {
                  if (nextBlock != thisBlock && (!nextIsBtnPanel || oppositeDir == nextBlock.Orientation.Up))
                  {
                    yield return dirVec;
                    continue;
                  }
                }
              }
              else if (downDir == thisBlock.Orientation.Up)
              {
                if (oppositeDir != thisBlock.Orientation.Up)
                {
                  if (oppositeDir != thisBlock.Orientation.Forward)
                  {
                    yield return dirVec;
                    continue;
                  }
                  else if (!nextBlockEmpty && (!nextIsBtnPanel || dir == nextBlock.Orientation.Forward))
                  {
                    if (!nextIsWindowSlope || nextBlock != thisBlock || oppositeDir != thisBlock.Orientation.Forward)
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                }

                if (oppositeDir == thisBlock.Orientation.Up && !nextBlockEmpty && nextBlock != thisBlock)
                {
                  if (!nextIsBtnPanel || dir == nextBlock.Orientation.Forward)
                  {
                    yield return dirVec;
                    continue;
                  }
                }
              }
              else if (downDir == thisBlock.Orientation.Forward)
              {
                if (oppositeDir != thisBlock.Orientation.Forward)
                {
                  if (oppositeDir != thisBlock.Orientation.Up)
                  {
                    yield return dirVec;
                    continue;
                  }
                  else if (!nextBlockEmpty && (!nextIsBtnPanel || dir == nextBlock.Orientation.Up))
                  {
                    if (!nextIsWindowSlope || nextBlock != thisBlock || oppositeDir != thisBlock.Orientation.Up)
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                }

                if (!nextBlockEmpty && nextBlock != thisBlock && oppositeDir == thisBlock.Orientation.Forward)
                {
                  if (!nextIsBtnPanel || dir == nextBlock.Orientation.Up)
                  {
                    yield return dirVec;
                    continue;
                  }
                }
              }
              else
              {
                yield return dirVec;
                continue;
              }
            }
          }
          else if (thisBlock?.FatBlock is IMyTextPanel)
          {
            if (dir == thisBlock.Orientation.Forward && thisBlock.BlockDefinition.Id.SubtypeName.IndexOf("corner", StringComparison.OrdinalIgnoreCase) < 0)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (thisIsLadder)
          {
            if (oppositeDir == thisBlock.Orientation.Forward)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (thisIsBtnPanel)
          {
            if (dir == thisBlock.Orientation.Forward)
            {
              yield return dirVec;
              continue;
            }
          }
        }
        else if (belowThisIsSlope)
        {
          bool isSlopeBlock = AiSession.Instance.SlopeBlockDefinitions.Contains(blockBelowThis.BlockDefinition.Id);

          if (isSlopeBlock && blockBelowThis.BlockDefinition.Id.SubtypeName == "GratedStairs")
          {
            if (dir == blockBelowThis.Orientation.Left || oppositeDir == blockBelowThis.Orientation.Left)
            {
              yield return dirVec;
              continue;
            }
          }

          bool isSlopeUpsideDown = isSlopeBlock 
            && (blockBelowThis.Orientation.Forward == upDir || blockBelowThis.Orientation.Up == Base6Directions.GetOppositeDirection(upDir));

          if (!isSlopeUpsideDown)
          {
            if (isSlopeBlock
              && (upDir == blockBelowThis.Orientation.Left || Base6Directions.GetOppositeDirection(upDir) == blockBelowThis.Orientation.Left))
            {
              if (dir == blockBelowThis.Orientation.Up || oppositeDir == blockBelowThis.Orientation.Forward)
              {
                yield return dirVec;
                continue;
              }
            }

            if (blockBelowNext != null && !belowNextIsSlope)
            {
              var dirToNext = blockBelowNext.Position - blockBelowThis.Position;
              var dirLeft = Base6Directions.GetIntVector(blockBelowThis.Orientation.Left);

              if (dirLeft.Dot(ref dirToNext) != 0)
              {
                yield return dirVec;
                continue;
              }
            }
          }
        }

        if (!nextBlockEmpty)
        {
          if (nextIsDeadBody)
          {
            if (CheckCatwalkForRails(nextBlock, -dirVec))
            {
              yield return dirVec;
              continue;
            }
          }
          else if (nextisDoor)
          {
            if (dir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Forward)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (nextIsDeco)
          {
            var def = nextBlock.BlockDefinition.Id;
            var subtype = def.SubtypeName;
            if (subtype == "LargeBlockPlanters" || subtype == "LargeBlockKitchen" || subtype == "LargeBlockBarCounter"
              || subtype == "LargeBlockDesk" || subtype == "LargeBlockDeskChairless")
            {
              if (oppositeDir == nextBlock.Orientation.Forward)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (subtype == "LargeBlockCouch")
            {
              if (dir == nextBlock.Orientation.Forward)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (subtype == "LargeBlockCouchCorner")
            {
              if (oppositeDir == nextBlock.Orientation.Left || dir == nextBlock.Orientation.Forward)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (subtype == "LargeBlockBarCounterCorner")
            {
              if (dir == nextBlock.Orientation.Left || oppositeDir == nextBlock.Orientation.Forward)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (subtype == "Shower")
            {
              if (dir == nextBlock.Orientation.Forward || dir == nextBlock.Orientation.Left)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (subtype.EndsWith("Toilet"))
            {
              if (dir == nextBlock.Orientation.Left)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (subtype == "Jukebox" || subtype == "AtmBlock" || subtype == "FoodDispenser" || subtype == "VendingMachine")
            {
              if (oppositeDir == nextBlock.Orientation.Left)
              {
                yield return dirVec;
                continue;
              }
            }
          }
          else if (nextIsSlope)
          {
            if (nextBlock.BlockDefinition.Id.SubtypeName == "GratedStairs")
            {
              if (dir == nextBlock.Orientation.Left || oppositeDir == nextBlock.Orientation.Left)
              {
                yield return dirVec;
                continue;
              }
            }

            if (nextIsHalfSlope)
            {
              if (dir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Forward)
              {
                if ((belowNextIsRamp || belowNextIsSlope || belowNextIsHalfSlope) && ExemptNodesUpper.Contains(next))
                {
                  if (blockBelowNext != thisBlock)
                  {
                    var dotUp = Base6Directions.GetIntVector(blockBelowNext.Orientation.Up).Dot(ref upVec);
                    var dotFwd = Base6Directions.GetIntVector(blockBelowNext.Orientation.Forward).Dot(ref upVec);
                    bool valid = true;

                    if (dotUp > 0)
                    {
                      if (!ExemptNodesUpper.Contains(next) && dir != blockBelowNext.Orientation.Forward)
                      {
                        valid = false;
                      }
                    }
                    else if (dotFwd > 0 || (dotFwd < 0 && belowNextIsHalfSlope))
                    {
                      if (dir != blockBelowNext.Orientation.Up)
                      {
                        valid = false;
                      }
                    }
                    else
                    {
                      valid = false;
                    }

                    if (!valid)
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                }
                else if (ExemptNodesSide.Contains(next))
                {
                  if (!thisBlockEmpty)
                  {
                    var blockUp = Base6Directions.GetIntVector(thisBlock.Orientation.Up);
                    var adjacentBlock = thisBlock.CubeGrid.GetCubeBlock(nodePos + blockUp);
                    if (adjacentBlock == nextBlock)
                    {
                      if (dir != thisBlock.Orientation.Up)
                      {
                        yield return dirVec;
                        continue;
                      }
                    }
                    else if (dir != nextBlock.Orientation.Left && oppositeDir != nextBlock.Orientation.Left)
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                  else if (dir != nextBlock.Orientation.Left && oppositeDir != nextBlock.Orientation.Left)
                  {
                    yield return dirVec;
                    continue;
                  }
                }
                else
                {
                  yield return dirVec;
                  continue;
                }
              }
            }
            else if (thisIsHalfStair && nextIsHalfStair)
            {
              if (dir != thisBlock.Orientation.Up && dir != Base6Directions.GetOppositeDirection(thisBlock.Orientation.Up))
              {
                yield return dirVec;
                continue;
              }

              var thisUp = Base6Directions.GetIntVector(thisBlock.Orientation.Up);
              var nextUp = Base6Directions.GetIntVector(nextBlock.Orientation.Up);
              var upDot = thisUp.Dot(ref nextUp);

              if (thisBlock.BlockDefinition.Id == nextBlock.BlockDefinition.Id)
              {
                if (upDot != 0)
                {
                  if (thisBlock.Orientation.Forward == nextBlock.Orientation.Left)
                  {
                    yield return dirVec;
                    continue;
                  }
                }
                else if (thisBlock.Orientation.Up == nextBlock.Orientation.Forward && thisBlock.Orientation.Forward != nextBlock.Orientation.Up)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (upDot != 0)
              {
                if (upDot > 0 && thisBlock.Orientation.Forward != nextBlock.Orientation.Forward)
                {
                  yield return dirVec;
                  continue;
                }
                else if (upDot < 0 && thisBlock.Orientation.Forward != Base6Directions.GetOppositeDirection(nextBlock.Orientation.Forward))
                {
                  yield return dirVec;
                  continue;
                }
              }
              else
              {
                var thisForward = Base6Directions.GetIntVector(thisBlock.Orientation.Forward);
                var fwdDotUp = thisForward.Dot(ref nextUp);

                if (fwdDotUp == 0)
                {
                  yield return dirVec;
                  continue;
                }
                else if (fwdDotUp > 0)
                {
                  if (nextBlock.Orientation.Forward == thisBlock.Orientation.Left || nextBlock.Orientation.Forward == Base6Directions.GetOppositeDirection(thisBlock.Orientation.Left))
                  {
                    yield return dirVec;
                    continue;
                  }
                }
                else if (thisBlock.Orientation.Up != nextBlock.Orientation.Forward && thisBlock.Orientation.Up != Base6Directions.GetOppositeDirection(nextBlock.Orientation.Forward))
                {
                  yield return dirVec;
                  continue;
                }
              }
            }
            else if (!ExemptNodesUpper.Contains(next) && dir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Up)
            {
              yield return dirVec;
              continue;
            }

            if (thisBlockEmpty && dir == nextBlock.Orientation.Forward && oppositeDir != upDir
              && nextBlock.BlockDefinition.Id.SubtypeName.EndsWith("BlockArmorSlope2Base"))
            {
              yield return dirVec;
              continue;
            }
          }
          else if (nextIsPanel)
          {
            var blockLeft = nextBlock.Orientation.Left;
            var blockFwd = nextBlock.Orientation.Forward;
            var blockUp = nextBlock.Orientation.Up;

            if (nextIsFullPanel)
            {
              if (dir == blockLeft)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (nextIsHalfPanel)
            {
              if (dir == blockLeft && nextBlock.BlockDefinition.Id.SubtypeName.StartsWith("LargeArmorHalf"))
              {
                if (Base6Directions.GetIntVector(blockUp).Dot(ref upVec) != 0)
                {
                  yield return dirVec;
                  continue;
                }
              }
            }
            else if (nextIsSlopePanel)
            {
              bool isUp = dir == upDir;
              bool oppIsBlockUp = oppositeDir == blockUp;
              bool oppIsBlockFwd = oppositeDir == blockFwd;
              bool oppDirNoGood = !oppIsBlockFwd && !oppIsBlockUp;

              if (!thisIsSlopePanel && (isUp || oppDirNoGood))
              {
                if (!isUp && oppDirNoGood && nextBlock.BlockDefinition.Id.SubtypeName.StartsWith("LargeArmorSlopedPanel"))
                {
                  var downDir = Base6Directions.GetOppositeDirection(upDir);

                  if (blockFwd == downDir || blockUp == downDir)
                  {
                    if (dir != blockFwd && dir != blockUp)
                    {
                      yield return dirVec;
                      continue;
                    }
                  }
                }
                else
                {
                  yield return dirVec;
                  continue;
                }
              }
            }
            else if (nextIsHalfSlopePanel)
            {
              if (oppositeDir != blockUp && oppositeDir != blockFwd)
              {
                yield return dirVec;
                continue;
              }
            }
            else // miscellaneous panels
            {
              if (nextBlock.BlockDefinition.Id.SubtypeName.EndsWith("Inv"))
              {
                if (oppositeDir == blockLeft)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (dir == blockLeft)
              {
                yield return dirVec;
                continue;
              }
            }
          }
          else if (nextIsPassage)
          {
            var blockSubtype = nextBlock.BlockDefinition.Id.SubtypeName;
            var blockFwd = nextBlock.Orientation.Forward;
            var blockUp = nextBlock.Orientation.Up;

            if (nextBlock.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Passage))
            {
              if (dir != blockUp && oppositeDir != blockUp)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (dir == blockUp || oppositeDir == blockUp)
            {
              yield return dirVec;
              continue;
            }
            else if (blockSubtype.EndsWith("Wall") || blockSubtype.EndsWith("Tjunction"))
            {
              if (dir != blockFwd && oppositeDir != blockFwd && dir != nextBlock.Orientation.Left)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (blockSubtype.EndsWith("Corner"))
            {
              if (dir != blockFwd && dir != nextBlock.Orientation.Left)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (!blockSubtype.EndsWith("Intersection") && dir != blockFwd && oppositeDir != blockFwd)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (nextIsBeam)
          {
            var blockSubtype = nextBlock.BlockDefinition.Id.SubtypeName;
            var blockLeft = nextBlock.Orientation.Left;
            var blockFwd = nextBlock.Orientation.Forward;

            if (blockSubtype.EndsWith("Slope"))
            {
              if (!blockSubtype.EndsWith("HalfSlope") && oppositeDir != blockLeft && dir != blockLeft)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (blockSubtype.EndsWith("End"))
            {
              if (dir != blockFwd)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (blockSubtype.EndsWith("TJunction"))
            {
              if (oppositeDir != blockFwd)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (oppositeDir != blockFwd && dir != blockFwd)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (nextIsLocker)
          {
            var blockSubtype = nextBlock.BlockDefinition.Id.SubtypeName;
            if (blockSubtype == "LargeBlockLockerRoom")
            {
              if (dir != nextBlock.Orientation.Forward)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (blockSubtype == "LargeBlockLockerRoomCorner")
            {
              if (oppositeDir == nextBlock.Orientation.Forward || dir == nextBlock.Orientation.Left)
              {
                yield return dirVec;
                continue;
              }
            }
            else if (oppositeDir == nextBlock.Orientation.Forward)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (nextIsFreight)
          {
            if (nextBlock.BlockDefinition.Id.SubtypeName != "Freight1" && dir == nextBlock.Orientation.Forward)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (nextIsHalfBlock)
          {
            if (upDir == dir || Base6Directions.GetOppositeDirection(upDir) == dir || oppositeDir == nextBlock.Orientation.Forward)
            {
              yield return dirVec;
              continue;
            }

            if (thisIsHalfBlock)
            {
              var downDir = Base6Directions.GetOppositeDirection(upDir);

              if (downDir == thisBlock.Orientation.Forward && downDir != nextBlock.Orientation.Forward)
              {
                yield return dirVec;
                continue;
              }

            }
            else if (!thisIsSlope)
            {
              yield return dirVec;
              continue;
            }
            else
            {
              bool thisIsSlopeBase = thisIsSlope
                && thisBlock.BlockDefinition.Id.SubtypeName.StartsWith("Large")
                && thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("BlockArmorSlope2Base");

              if (thisIsSlopeBase) 
              {
                if (oppositeDir != thisBlock.Orientation.Forward)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (thisIsHalfSlope)
              {
                if (dir != thisBlock.Orientation.Forward)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else if (thisBlock.BlockDefinition.Id.SubtypeName.StartsWith("Large") && thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("HalfSlopeArmorBlock"))
              {
                if (dir != thisBlock.Orientation.Forward && oppositeDir != thisBlock.Orientation.Up)
                {
                  yield return dirVec;
                  continue;
                }
              }
              else
              {
                yield return dirVec;
                continue;
              }
            }
          }
          else if (nextIsCoverWall)
          {
            var nextBlockSubtype = nextBlock.BlockDefinition.Id.SubtypeName;
            if (dir == nextBlock.Orientation.Forward && !nextBlockSubtype.EndsWith("Half"))
            {
              yield return dirVec;
              continue;
            }

            if (dir == nextBlock.Orientation.Left && nextBlockSubtype.EndsWith("Corner"))
            {
              yield return dirVec;
              continue;
            }
          }
          else if (nextIsRamp)
          {
            if (oppositeDir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Up)
            {
              if (!thisIsRamp || thisBlock != nextBlock || dir != thisBlock.Orientation.Forward)
              {
                if (!ExemptNodesSide.Contains(next) || dir == nextBlock.Orientation.Up)
                {
                  yield return dirVec;
                  continue;
                }
              }
            }
          }
          else if (nextIsCatwalk)
          {
            if (dir == nextBlock.Orientation.Up || CheckCatwalkForRails(nextBlock, -dirVec))
            {
              yield return dirVec;
              continue;
            }
          }
          else if (nextIsRailing)
          {
            if (CheckCatwalkForRails(nextBlock, -dirVec))
            {
              yield return dirVec;
              continue;
            }
          }
          else if (nextIsWindowFlat)
          {
            Base6Directions.Direction sideWithPane;
            if (nextBlock.BlockDefinition.Id.SubtypeName == "LargeWindowSquare")
              sideWithPane = nextBlock.Orientation.Forward;
            else
              sideWithPane = Base6Directions.GetOppositeDirection(nextBlock.Orientation.Left);

            if (oppositeDir == sideWithPane || (nextBlock.BlockDefinition.Id.SubtypeName.StartsWith("HalfWindowCorner") && oppositeDir == nextBlock.Orientation.Forward))
            {
              yield return dirVec;
              continue;
            }
          }
          else if (nextIsSolar)
          {
            if (oppositeDir == nextBlock.Orientation.Forward)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (nextIsWindowSlope)
          {
            if (nextBlock.BlockDefinition.Id.SubtypeName == "LargeWindowEdge") // can only walk up one side
            {
              if (oppositeDir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Up)
              {
                yield return dirVec;
                continue;
              }
            }
            else // can walk up any side
            {
              var downDir = Base6Directions.GetOppositeDirection(upDir);

              if (upDir == nextBlock.Orientation.Up) 
              {
                if (thisBlockEmpty || thisBlock != nextBlock)
                {
                  if (oppositeDir != upDir && oppositeDir != nextBlock.Orientation.Forward)
                  {
                    yield return dirVec;
                    continue;
                  }

                  if (dir == nextBlock.Orientation.Forward)
                  {
                    yield return dirVec;
                    continue;
                  }
                }
              }
              else if (upDir == nextBlock.Orientation.Forward)
              {
                if (thisBlockEmpty || thisBlock != nextBlock)
                {
                  if (oppositeDir != upDir && oppositeDir != nextBlock.Orientation.Up)
                  {
                    yield return dirVec;
                    continue;
                  }

                  if (dir == nextBlock.Orientation.Up)
                  {
                    yield return dirVec;
                    continue;
                  }
                }
              }
              else if (downDir == nextBlock.Orientation.Up)
              {
                if (thisBlockEmpty || thisBlock != nextBlock)
                {
                  if (dir != downDir && dir != nextBlock.Orientation.Forward)
                  {
                    yield return dirVec;
                    continue;
                  }

                  if (oppositeDir == nextBlock.Orientation.Forward)
                  {
                    yield return dirVec;
                    continue;
                  }
                }
              }
              else if (downDir == nextBlock.Orientation.Forward)
              {
                if (thisBlockEmpty || thisBlock != nextBlock)
                {
                  if (dir != downDir && dir != nextBlock.Orientation.Up)
                  {
                    yield return dirVec;
                    continue;
                  }

                  if (oppositeDir == nextBlock.Orientation.Up)
                  {
                    yield return dirVec;
                    continue;
                  }
                }
              }
            }
          }
          else if (nextBlock?.FatBlock is IMyTextPanel)
          {
            if (oppositeDir == nextBlock.Orientation.Forward && nextBlock.BlockDefinition.Id.SubtypeName.IndexOf("corner") < 0)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (nextIsLadder)
          {
            if (dir == nextBlock.Orientation.Forward)
            {
              yield return dirVec;
              continue;
            }
          }
          else if (nextIsBtnPanel)
          {
            if (oppositeDir == nextBlock.Orientation.Forward)
            {
              yield return dirVec;
              continue;
            }
          }
        }
        else if (belowNextIsSlope)
        {
          if (blockBelowNext.BlockDefinition.Id.SubtypeName == "GratedStairs")
          {
            if (dir == blockBelowNext.Orientation.Left || oppositeDir == blockBelowNext.Orientation.Left)
            {
              yield return dirVec;
              continue;
            }
          }

          if (AiSession.Instance.SlopeBlockDefinitions.Contains(blockBelowNext.BlockDefinition.Id)
            && (upDir == blockBelowNext.Orientation.Left || Base6Directions.GetOppositeDirection(upDir) == blockBelowNext.Orientation.Left))
          {
            if (dir == blockBelowNext.Orientation.Forward || oppositeDir == blockBelowNext.Orientation.Up)
            {
              yield return dirVec;
              continue;
            }
          }
        }
      }

      foreach (var dirVec in AiSession.Instance.VoxelMovementDirections)
      {
        Node nNext;
        var next = nodePos + dirVec;
        if (node.IsGridNode && !node.IsGridNodePlanetTile)
        {
          yield return dirVec;
        }
        else if (!OpenTileDict.TryGetValue(next, out nNext) || nNext == null)
        {
          yield return dirVec;
        }
        else if (Grid.CubeExists(next))
        {
          yield return dirVec;
        }
        else if (node.IsGridNodePlanetTile && !nNext.IsGridNodePlanetTile)
        {
          yield return dirVec;
        }
      }

      if (!node.IsGridNode || node.IsGridNodePlanetTile)
      {
        foreach (var dirVec in AiSession.Instance.DiagonalDirections)
        {
          Node nNext;
          var next = nodePos + dirVec;
          if (Grid.CubeExists(next))
          {
            yield return dirVec;
          }
          else if (!OpenTileDict.TryGetValue(next, out nNext) || nNext == null)
          {
            yield return dirVec;
          }
          else if (node.IsGridNodePlanetTile && !nNext.IsGridNodePlanetTile)
          {
            yield return dirVec;
          }
        }
      }
    }

    bool CheckCatwalkForRails(IMySlimBlock block, Vector3I side)
    {
      var def = block.BlockDefinition.Id;
      Base6Directions.Direction[] railArray;
      if (!AiSession.Instance.CatwalkRailDirections.TryGetValue(def, out railArray))
      {
        return false;
      }

      Matrix m = new Matrix
      {
        Up = Base6Directions.GetVector(block.Orientation.Up),
        Forward = Base6Directions.GetVector(block.Orientation.Forward),
        Left = Base6Directions.GetVector(block.Orientation.Left)
      };

      m.TransposeRotationInPlace();
      Vector3I.TransformNormal(ref side, ref m, out side);

      var checkDir = Base6Directions.GetDirection(side);
      return railArray.ContainsItem(checkDir);
    }

    List<Vector3I> _localNodes = new List<Vector3I>();

    public override bool GetRandomNodeNearby(BotBase bot, Vector3D targetPosition, out Vector3I node)
    {
      var botPosition = bot.Position;
      var collection = bot._pathCollection;
      var botWorldMatrix = bot.WorldMatrix;

      node = Grid.WorldToGridInteger(botPosition);

      _localNodes.Clear();
      foreach (var point in Neighbors(bot, node, node, botPosition, true, up: botWorldMatrix.Up))
      {
        if (collection.DeniedDoors.ContainsKey(point))
          continue;

        IHitInfo hitInfo;
        var worldNode = Grid.GridIntegerToWorld(point);

        if (MyAPIGateway.Physics.CastRay(botPosition, worldNode, out hitInfo, CollisionLayers.CharacterCollisionLayer))
        {
          var grid = hitInfo?.HitEntity?.GetTopMostParent() as MyCubeGrid;
          if (grid != null)
            continue;
        }

        if (MyAPIGateway.Physics.CastRay(worldNode, targetPosition, out hitInfo, CollisionLayers.CharacterCollisionLayer))
        {
          var grid = hitInfo?.HitEntity?.GetTopMostParent() as MyCubeGrid;
          if (grid != null)
            continue;
        }

        _localNodes.Add(point);
      }

      if (_localNodes.Count > 0)
      {
        var rnd = MyUtils.GetRandomInt(0, _localNodes.Count);
        node = _localNodes[rnd];
        return true;
      }

      return false;
    }

    public override bool GetRandomOpenNode(BotBase bot, Vector3D requestedPosition, out Node node)
    {
      node = null;
      if (OpenTileDict.Count == 0)
        return false;

      NodeList.Clear();
      Grid.RayCastCells(bot.Position, requestedPosition, NodeList);

      for (int i = 0; i < NodeList.Count; i++)
      {
        var localPosition = NodeList[i];
       
        Node tempNode;
        if (IsPositionUsable(bot, LocalToWorld(localPosition), out tempNode))
        {
          node = tempNode;
          continue;
        }

        break;
      }

      //for (int i = NodeList.Count - 1; i >= 0; i--)
      //{
      //  var node = NodeList[i];
      //  if (OpenTileDict.ContainsKey(node) && !Obstacles.Contains(node) && !TempBlockedNodes.Contains(node))
      //  {
      //    worldNodePosition = LocalToWorld(node);
      //    break;
      //  }
      //}

      NodeList.Clear();
      return node != null;
    }

    List<MyEntity> _tempEntities = new List<MyEntity>();
    public override void UpdateTempObstacles()
    {
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

          if (grid.IsSameConstructAs(Grid) && grid.GridSizeEnum == VRage.Game.MyCubeSize.Large)
          {
            continue;
          }

          var orientation = Quaternion.CreateFromRotationMatrix(grid.WorldMatrix);
          var obb = new MyOrientedBoundingBoxD(grid.PositionComp.WorldAABB.Center, grid.PositionComp.LocalAABB.HalfExtents, orientation);

          var containType = OBB.Contains(ref obb);
          if (containType == ContainmentType.Disjoint)
          {
            continue;
          }

          BoundingBoxI box = BoundingBoxI.CreateInvalid();
          obb.GetCorners(_corners1, 0);
          for (int j = 0; j < _corners1.Length; j++)
          {
            var localCorner = grid.WorldToGridInteger(_corners1[j]);
            box.Include(localCorner);
          }

          if (containType == ContainmentType.Intersects)
          {
            BoundingBoxI otherBox = BoundingBoxI.CreateInvalid();
            OBB.GetCorners(_corners1, 0);
            for (int j = 0; j < _corners1.Length; j++)
            {
              var localCorner = grid.WorldToGridInteger(_corners1[j]);
              otherBox.Include(localCorner);
            }

            box.IntersectWith(ref otherBox);
          }

          box.Inflate(1);
          Vector3I_RangeIterator iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);

          while (iter.IsValid())
          {
            var gridLocal = iter.Current;
            iter.MoveNext();

            if (!grid.CubeExists(gridLocal))
              continue;

            var gridWorld = grid.GridIntegerToWorld(gridLocal);
            var graphLocal = WorldToLocal(gridWorld);

            if (OpenTileDict.ContainsKey(graphLocal) && !ObstacleNodesTemp.ContainsKey(graphLocal) && !Obstacles.ContainsKey(graphLocal))
              ObstacleNodesTemp[graphLocal] = new byte();
          }
        }

        Interlocked.CompareExchange(ref ObstacleNodes, ObstacleNodesTemp, ObstacleNodes);
        _tempEntities.Clear();
      }
    }

    public override Node GetReturnHomePoint(BotBase bot)
    {
      foreach (var kvp in OpenTileDict)
      {
        if (!kvp.Value.IsGridNode)
          continue;

        var localPosition = kvp.Key;
        if (!BlockedDoors.ContainsKey(localPosition) && !TempBlockedNodes.ContainsKey(localPosition) && !ObstacleNodes.ContainsKey(localPosition) && !TempBlockedNodes.ContainsKey(localPosition))
          return kvp.Value;
      }

      var from = OBB.Center - OBB.HalfExtent.Y;
      var to = OBB.Center + OBB.HalfExtent.Y;

      _localNodes.Clear();
      Grid.RayCastCells(from, to, _localNodes);
      Node node;

      for (int i = 0; i < _localNodes.Count; i++)
      {
        var localPosition = _localNodes[i];

        if (IsPositionUsable(bot, LocalToWorld(localPosition), out node))
          return node;

        if (GetClosestValidNode(bot, localPosition, out localPosition) && OpenTileDict.TryGetValue(localPosition, out node))
          return node;
      }

      while (PointInsideVoxel(from, Planet))
        from += WorldMatrix.Up * 0.1;

      var fromNode = Grid.WorldToGridInteger(from);
      if (OpenTileDict.TryGetValue(fromNode, out node) && node.SurfacePosition.HasValue)
        return node;

      Vector3I testNode;
      if (GetClosestValidNode(bot, fromNode, out testNode) && OpenTileDict.TryGetValue(testNode, out node))
        return node;

      var worldNode = Grid.GridIntegerToWorld(fromNode);
      if (PointInsideVoxel(worldNode, Planet))
        worldNode += WorldMatrix.Up * CellSize;

      from += WorldMatrix.Up;
      var localPoint = Grid.WorldToGridInteger(worldNode);
      return new Node(localPoint, Grid, null, from);
    }

    public Vector3D GetLastValidNodeOnLine(Vector3D start, Vector3D directionNormalized, double desiredDistance, bool ensureOpenTiles = true)
    {
      Vector3D result = start;
      var end = start + directionNormalized * desiredDistance;

      NodeList.Clear();
      Grid.RayCastCells(start, end, NodeList);
      Vector3I prevNode = WorldToLocal(start);

      for (int i = 0; i < NodeList.Count; i++)
      {
        var node = NodeList[i];
        var cube = Grid.GetCubeBlock(node) as IMySlimBlock;
        var def = cube?.BlockDefinition as MyCubeBlockDefinition;

        if (!ensureOpenTiles && def?.HasPhysics != true)
        {
          var world = LocalToWorld(node);
          if (!PointInsideVoxel(world, Planet))
          {
            result = world;
            continue;
          }
        }

        Node n;
        if (!OpenTileDict.TryGetValue(node, out n) || BlockedDoors.ContainsKey(node) || TempBlockedNodes.ContainsKey(node) || ObstacleNodes.ContainsKey(node))
          break;

        if (def?.HasPhysics == true)
        {
          if (n?.BlockedEdges?.Contains(prevNode - node) == true)
            break;

          if (OpenTileDict.TryGetValue(prevNode, out n) && n?.BlockedEdges?.Contains(node - prevNode) == true)
            break;
        }

        result = LocalToWorld(node);
        prevNode = node;
      }

      //var attempts = Math.Max(2, (int)Math.Round(desiredDistance / CellSize));
      //for (int i = 0; i < attempts; i++)
      //{
      //  var point = start + directionNormalized * CellSize * i;
      //  var localPoint = WorldToLocal(point);
      //  if (OpenTileDict.ContainsKey(localPoint) && !Obstacles.Contains(localPoint) && !TempBlockedNodes.Contains(localPoint))
      //    continue;

      //  if (i > 0)
      //    return start + directionNormalized * CellSize * (i - 1);

      //  break;
      //}

      return result;
    }

    public override bool IsPositionAvailable(Vector3D position)
    {
      if (base.IsPositionAvailable(position))
      {
        var node = WorldToLocal(position);
        return !BlockedDoors.ContainsKey(node);
      }

      return false;
    }

    public override bool IsPositionAvailable(Vector3I node)
    {
      return base.IsPositionAvailable(node) && !BlockedDoors.ContainsKey(node);
    }

    public void RecalculateOBB()
    {
      var box = BoundingBox;
      var worldCenter = LocalToWorld(box.Center);
      var quat = Quaternion.CreateFromRotationMatrix(Grid.GetBiggestGridInGroup().WorldMatrix);
      OBB = new MyOrientedBoundingBoxD(worldCenter, box.HalfExtents * CellSize + Vector3D.Half * CellSize, quat);

      box.Inflate(-_boxExpansion);
      UnbufferedOBB = new MyOrientedBoundingBoxD(worldCenter, box.HalfExtents * CellSize + Vector3D.Half * CellSize, quat);
    }

    public void CheckPlanet()
    {
      if (Grid?.Physics == null || Grid.MarkedForClose || Grid.Physics.IsStatic)
        return;

      float _;
      var gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out _);
      if (gravity.LengthSquared() > 0)
      {
        var newPlanet = MyGamePruningStructure.GetClosestPlanet(OBB.Center);

        if (Planet != null && newPlanet != Planet)
          Planet_OnMarkForClose(Planet);

        Planet = newPlanet;
        Planet.RangeChanged -= Planet_RangeChanged;
        Planet.OnMarkForClose -= Planet_OnMarkForClose;

        Planet.RangeChanged += Planet_RangeChanged;
        Planet.OnMarkForClose += Planet_OnMarkForClose;
      }
      else if (Planet != null)
      {
        Planet_OnMarkForClose(Planet);
        Planet = null;
      }
    }

    public override bool IsPositionValid(Vector3D position)
    {
      return OBB.Contains(ref position);
    }
  }
}