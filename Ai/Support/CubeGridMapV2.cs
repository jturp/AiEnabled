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
using Direction = VRageMath.Base6Directions.Direction;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

using VRageMath;
using AiEnabled.Support;
using AiEnabled.Utilities;
using VRage.Collections;
using AiEnabled.Bots;
using VRage.ModAPI;
using System.Threading;
using VRage.Voxels;
using AiEnabled.API;
using VRage.Game.Components;
using VRage.Game;
using AiEnabled.Parallel;
using ParallelTasks;
using Sandbox.Game.Entities.Blocks;
using AiEnabled.Bots.Roles.Helpers;
using VRage.ObjectBuilders;

namespace AiEnabled.Ai.Support
{
  public class CubeGridMap : GridBase
  {
    /// <summary>
    /// The main grid this map refers to
    /// </summary>
    public MyCubeGrid MainGrid { get; protected set; }

    /// <summary>
    /// A collection of grids associated with the map
    /// </summary>
    public List<IMyCubeGrid> GridCollection;

    /// <summary>
    /// The cell size (in meters) for the map
    /// </summary>
    public override float CellSize => MainGrid?.GridSize ?? 2.5f;

    /// <summary>
    /// The smallest oriented bounding box that fits the grid
    /// </summary>
    public MyOrientedBoundingBoxD UnbufferedOBB { get; private set; }

    /// <summary>
    /// This will be true when the grid has moved more than 1m from its previous position
    /// </summary>
    public bool HasMoved { get; private set; }

    /// <summary>
    /// If true, there are grids to add to or remove from the GridCollection
    /// </summary>
    public bool NeedsGridUpdate { get; private set; }

    /// <summary>
    /// If true, need to recheck for interior nodes (blocks added or removed)
    /// </summary>
    public bool NeedsInteriorNodesUpdate { get; private set; }

    /// <summary>
    /// If true, planet tiles have already been cleared from the dictionary
    /// </summary>
    public bool PlanetTilesRemoved { get; private set; }

    /// <summary>
    /// If true, the interior nodes list is ready to be used
    /// </summary>
    public bool InteriorNodesReady { get; private set; }

    /// <summary>
    /// Contains information about the components stored in the grid's inventories
    /// </summary>
    public InventoryCache InventoryCache;

    /// <summary>
    /// If repair bots are present on the grid, this will hold any tiles they are actively reparing or building.
    /// Key is grid entity id, value is a Dictionary where Key is block position on grid and Value is bot entity id.
    /// </summary>
    public ConcurrentDictionary<long, ConcurrentDictionary<Vector3I, long>> SelectedRepairTiles = new ConcurrentDictionary<long, ConcurrentDictionary<Vector3I, long>>(5, 10);

    /// <summary>
    /// Doors that are currently closed and non-functional will be in this collection
    /// </summary>
    public ConcurrentDictionary<Vector3I, IMyDoor> BlockedDoors = new ConcurrentDictionary<Vector3I, IMyDoor>(5, 10, Vector3I.Comparer);

    /// <summary>
    /// All doors for the current grid
    /// </summary>
    public ConcurrentDictionary<Vector3I, IMyDoor> AllDoors = new ConcurrentDictionary<Vector3I, IMyDoor>(5, 10, Vector3I.Comparer);

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
    /// Location of any weapon system in the grid collection. Used to determine movement cost of nodes (closer to a weapon = higher movement cost)
    /// </summary>
    public HashSet<Vector3I> WeaponPositions = new HashSet<Vector3I>(Vector3I.Comparer);

    /// <summary>
    /// Used for determining when a boss encounter spawns
    /// </summary>
    public byte BossSpawnChance = 100;

    /// <summary>
    /// The number of bots spawned on this grid
    /// </summary>
    public ushort TotalSpawnCount = 0;


    internal Dictionary<Vector3I, Node> OpenTileDict = new Dictionary<Vector3I, Node>(Vector3I.Comparer);
    internal Dictionary<Vector3I, Node> PlanetTileDictionary = new Dictionary<Vector3I, Node>(Vector3I.Comparer);

    Vector3D _worldPosition;
    MatrixD _lastMatrix;
    public List<Vector3I> InteriorNodeList;
    readonly byte _boxExpansion;
    List<CubeGridMap> _additionalMaps2;
    List<IMyCubeGrid> _gridsToAdd;
    List<IMyCubeGrid> _gridsToRemove;
    ObstacleWorkData _tempObstaclesWorkData;
    List<VoxelUpdateItem> _voxelUpdatesNeeded;
    HashSet<Vector3I> _blockUpdateHash;
    HashSet<Vector3I> _blockApplyHash;
    MyQueue<VoxelUpdateItem> _voxelUpdatesQueue;
    ParallelTasks.Task _obstacleTask, _updateTask, _asyncTileTask, _interiorNodesTask;
    FastResourceLock _pendingLockObject = new FastResourceLock();

    public CubeGridMap(MyCubeGrid grid, MatrixD spawnBlockMatrix)
    {
      try
      {
        if (grid?.Physics == null || grid.MarkedForClose)
        {
          AiSession.Instance.Logger.Warning($"CubeGridMapV2.ctor: Grid '{grid?.DisplayName ?? "NULL"}' was null or marked for close in constructor!");
          return;
        }

        var gridGroup = grid.GetGridGroup(GridLinkTypeEnum.Mechanical);
        if (gridGroup == null)
        {
          AiSession.Instance.Logger.Warning($"CubeGridMapV2.ctor: GridGroup was null for '{grid.DisplayName}'");
          return;
        }

        GridCollection = AiSession.Instance.GridGroupListPool.Get();
        gridGroup.GetGrids(GridCollection);

        if (GridCollection.Count == 0)
        {
          AiSession.Instance.GridGroupListPool?.Return(ref GridCollection);

          AiSession.Instance.Logger.Warning($"CubeGridMapV2.ctor: GridCollection was empty for '{grid.DisplayName}'");
          return;
        }

        IsGridGraph = true;
        Key = (ulong)grid.EntityId;
        gridGroup.OnGridAdded += GridGroup_OnGridAdded;
        gridGroup.OnGridRemoved += GridGroup_OnGridRemoved;
        gridGroup.OnReleased += GridGroup_OnReleased;

        var isStatic = grid.Physics.IsStatic;
        //if (isStatic)
        //  _boxExpansion = 10;
        //else
        _boxExpansion = 5;

        _tempObstaclesWorkData = AiSession.Instance.ObstacleWorkDataPool.Get();
        _additionalMaps2 = AiSession.Instance.GridMapListPool.Get();
        _gridsToAdd = AiSession.Instance.GridGroupListPool.Get();
        _gridsToRemove = AiSession.Instance.GridGroupListPool.Get();
        _voxelUpdatesNeeded = AiSession.Instance.VoxelUpdateListPool.Get();
        _voxelUpdatesQueue = AiSession.Instance.VoxelUpdateQueuePool.Get();
        _blockUpdateHash = AiSession.Instance.LocalVectorHashPool.Get();
        _blockApplyHash = AiSession.Instance.LocalVectorHashPool.Get();

        InteriorNodeList = AiSession.Instance.LineListPool.Get();
        InventoryCache = AiSession.Instance.InvCachePool.Get();
        InventoryCache.SetGrid(grid);
        InventoryCache.Update(false);

        MainGrid = grid;
        WorldMatrix = grid.WorldMatrix;
        _worldPosition = WorldMatrix.Translation;
        _lastMatrix = WorldMatrix;

        var hExtents = grid.PositionComp.LocalAABB.HalfExtents;
        var center = grid.PositionComp.WorldAABB.Center;
        var quat = Quaternion.CreateFromRotationMatrix(WorldMatrix);
        UnbufferedOBB = new MyOrientedBoundingBoxD(center, hExtents, quat);

        hExtents += (new Vector3I(11) * grid.GridSize);
        OBB = new MyOrientedBoundingBoxD(center, hExtents, quat);

        bool matrixSet = false;
        bool gravityChecked = false;
        if (isStatic)
        {
          var planet = MyGamePruningStructure.GetClosestPlanet(OBB.Center);
          if (planet != null && planet.PositionComp.WorldVolume.Intersects(OBB.GetAABB()))
          {
            Vector3D[] corners;
            if (!AiSession.Instance.CornerArrayStack.TryPop(out corners) || corners == null)
              corners = new Vector3D[8];

            var gridOBB = new MyOrientedBoundingBoxD(OBB.Center, grid.PositionComp.LocalAABB.HalfExtents, quat);
            gridOBB.GetCorners(corners, 0);

            using (planet.Pin())
            {
              for (int i = 0; i < corners.Length; i++)
              {
                if (PointInsideVoxel(corners[i], planet))
                {
                  gravityChecked = true;
                  break;
                }
              }
            }

            AiSession.Instance.CornerArrayStack.Push(corners);

            if (gravityChecked)
            {
              float interference;
              var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out interference);
              var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(OBB.Center, interference);
              if (aGrav.LengthSquared() > 0)
              {
                var up = -Vector3D.Normalize(aGrav);
                var fwd = Vector3D.CalculatePerpendicularVector(up);

                var dir = grid.WorldMatrix.GetClosestDirection(up);
                up = grid.WorldMatrix.GetDirectionVector(dir);

                dir = grid.WorldMatrix.GetClosestDirection(fwd);
                fwd = grid.WorldMatrix.GetDirectionVector(dir);

                WorldMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
                matrixSet = true;
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
                matrixSet = true;
              }
            }
          }
        }

        if (!matrixSet)
        {
          int numSeats, numFactories;
          if (grid.HasMainCockpit())
          {
            WorldMatrix = grid.MainCockpit.WorldMatrix;
            matrixSet = true;
          }
          else if (grid.HasMainRemoteControl())
          {
            WorldMatrix = grid.MainRemoteControl.WorldMatrix;
            matrixSet = true;
          }
          else if ((grid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_Cockpit), out numSeats) && numSeats > 0)
            || (grid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_RemoteControl), out numSeats) && numSeats > 0)
            || (grid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_ConveyorSorter), out numFactories) && numFactories > 0))
          {
            foreach (var b in grid.GetFatBlocks())
            {
              if (b is IMyShipController || (b is MyConveyorSorter && b.BlockDefinition.Id.SubtypeName == "RoboFactory"))
              {
                WorldMatrix = b.WorldMatrix;
                matrixSet = true;
                break;
              }
            }
          }

          if (!matrixSet)
          {
            if (!gravityChecked)
            {
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
            }
            else
              WorldMatrix = spawnBlockMatrix;

            bool rotate = true;
            foreach (var block in grid.GetFatBlocks())
            {
              rotate = false;
              break;
            }

            if (rotate)
            {
              var rotatedMatrix = MatrixD.CreateRotationY(MathHelper.PiOver2) * WorldMatrix;
              WorldMatrix = rotatedMatrix;
            }
          }
        }

        UnhookEventsForGrid(MainGrid);
        HookEventsForGrid(MainGrid);
        AiSession.Instance.MapInitQueue.Enqueue(this);
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in CubeGridMapV2.ctor: {ex}");
      }
    }

    private void GridGroup_OnGridRemoved(IMyGridGroupData groupRemovedFrom, IMyCubeGrid grid, IMyGridGroupData groupAddedTo)
    {
      try
      {
        if (grid != null && !grid.IsSameConstructAs(MainGrid))
          return;

        Remake = true;
        Dirty = true;
        Ready = false;

        var myGrid = grid as MyCubeGrid;

        if (myGrid != null)
        {
          _gridsToRemove.Add(grid);
          NeedsGridUpdate = true;
        }
        else if (MainGrid == null || MainGrid.MarkedForClose || grid?.EntityId == MainGrid.EntityId)
          UpdateMainGrid();
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in CubeGridMapV2.GridGroup_OnGridRemoved: {ex}");
      }
    }

    private void GridGroup_OnGridAdded(IMyGridGroupData groupAddedTo, IMyCubeGrid grid, IMyGridGroupData groupRemovedFrom)
    {
      try
      {
        if (grid == null || !grid.IsSameConstructAs(MainGrid))
          return;

        Remake = true;
        Dirty = true;
        Ready = false;

        var myGrid = grid as MyCubeGrid;

        if (myGrid?.Physics != null && !myGrid.IsPreview && !grid.MarkedForClose)
        {
          _gridsToAdd.Add(grid);
          NeedsGridUpdate = true;
        }
        else if (MainGrid == null || MainGrid.MarkedForClose)
          UpdateMainGrid();
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in CubeGridMapV2.GridGroup_OnGridAdded: {ex}");
      }
    }

    private void GridGroup_OnReleased(IMyGridGroupData gridGroup)
    {
      try
      {
        Remake = true;
        Dirty = true;
        Ready = false;

        gridGroup.OnGridAdded -= GridGroup_OnGridAdded;
        gridGroup.OnGridRemoved -= GridGroup_OnGridRemoved;
        gridGroup.OnReleased -= GridGroup_OnReleased;

        if (MainGrid == null || MainGrid.MarkedForClose)
        {
          for (int i = GridCollection.Count - 1; i >= 0; i--)
          {
            var grid = GridCollection[i] as MyCubeGrid;
            if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose || grid.GridSizeEnum == MyCubeSize.Small)
              continue;

            MainGrid = grid;
            break;
          }
        }

        if (MainGrid != null && !MainGrid.MarkedForClose)
        {
          gridGroup = MainGrid.GetGridGroup(GridLinkTypeEnum.Mechanical);

          if (gridGroup != null)
          {
            gridGroup.OnGridAdded += GridGroup_OnGridAdded;
            gridGroup.OnGridRemoved += GridGroup_OnGridRemoved;
            gridGroup.OnReleased += GridGroup_OnReleased;
          }
          else
          {
            Close();
          }
        }
        else
        {
          Close();
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in CubeGridMapV2.GridGroup_OnReleased: {ex}");
      }
    }

    public void UpdateGridCollection()
    {
      try
      {
        Remake = true;
        Dirty = true;
        Ready = false;
        NeedsGridUpdate = false;

        for (int i = 0; i < _gridsToRemove.Count; i++)
        {
          var grid = _gridsToRemove[i];
          if (grid != null)
          {
            CloseGrid(grid as MyEntity);

            for (int j = GridCollection.Count; j < GridCollection.Count; j++)
            {
              if (grid.EntityId == GridCollection[j]?.EntityId)
              {
                GridCollection.RemoveAtFast(j);
                break;
              }
            }
          }
        }

        for (int i = 0; i < _gridsToAdd.Count; i++)
        {
          var myGrid = _gridsToAdd[i] as MyCubeGrid;
          if (myGrid != null)
          {
            GridCollection.Add(myGrid);
          }
        }

        _gridsToAdd.Clear();
        _gridsToRemove.Clear();
        UpdateMainGrid();
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in CubeGridMapV2.UpdateGridCollection: {ex}");
      }
    }

    public override void Close()
    {
      try
      {
        IsGridGraph = false;

        if (AiSession.Instance != null && AiSession.Instance.Registered)
        {
          if (InventoryCache != null)
          {
            InventoryCache.SetGrid(null);
            InventoryCache._needsUpdate = false;

            AiSession.Instance.InvCachePool?.Return(ref InventoryCache);
          }

          if (_additionalMaps2 != null)
          {
            AiSession.Instance.GridMapListPool?.Return(ref _additionalMaps2);
          }

          if (GridCollection != null)
          {
            for (int i = 0; i < GridCollection.Count; i++)
            {
              var grid = GridCollection[i] as MyEntity;
              if (grid != null)
                CloseGrid(grid);
            }

            AiSession.Instance.GridGroupListPool?.Return(ref GridCollection);
          }

          if (_gridsToAdd != null)
          {
            AiSession.Instance.GridGroupListPool?.Return(ref _gridsToAdd);
          }

          if (_gridsToRemove != null)
          {
            AiSession.Instance.GridGroupListPool?.Return(ref _gridsToRemove);
          }

          if (InteriorNodeList != null)
          {
            AiSession.Instance.LineListPool?.Return(ref InteriorNodeList);
          }

          if (_tempObstaclesWorkData != null)
          {
            AiSession.Instance.ObstacleWorkDataPool?.Return(ref _tempObstaclesWorkData);
          }

          if (_voxelUpdatesNeeded != null)
          {
            AiSession.Instance.VoxelUpdateListPool?.Return(ref _voxelUpdatesNeeded);
          }

          if (_voxelUpdatesQueue != null)
          {
            AiSession.Instance.VoxelUpdateQueuePool?.Return(ref _voxelUpdatesQueue);
          }

          if (_blockUpdateHash != null)
          {
            AiSession.Instance.LocalVectorHashPool?.Return(ref _blockUpdateHash);
          }

          if (_blockApplyHash != null)
          {
            AiSession.Instance.LocalVectorHashPool?.Return(ref _blockApplyHash);
          }
        }
        else
        {
          _gridsToAdd?.Clear();
          _gridsToRemove?.Clear();
          _additionalMaps2?.Clear();
          _voxelUpdatesNeeded?.Clear();
          _voxelUpdatesQueue?.Clear();
          _blockUpdateHash?.Clear();
          _blockApplyHash?.Clear();

          GridCollection?.Clear();
          InteriorNodeList?.Clear();

          _tempObstaclesWorkData = null;
          _gridsToAdd = null;
          _gridsToRemove = null;
          _additionalMaps2 = null;
          _voxelUpdatesNeeded = null;
          _voxelUpdatesQueue = null;
          _blockUpdateHash = null;
          _blockApplyHash = null;

          InventoryCache = null;
          GridCollection = null;
        }

        AllDoors?.Clear();
        AllDoors = null;

        BlockedDoors?.Clear();
        BlockedDoors = null;

        OpenTileDict?.Clear();
        OpenTileDict = null;

        SelectedRepairTiles?.Clear();
        SelectedRepairTiles = null;

        PlanetTileDictionary?.Clear();
        PlanetTileDictionary = null;

        WeaponPositions?.Clear();
        WeaponPositions = null;

        WeaponPositions?.Clear();
        WeaponPositions = null;

        ExemptNodesUpper?.Clear();
        ExemptNodesUpper = null;

        _blockBoxList?.Clear();
        _blockBoxList = null;

        //_positionsToRemove?.Clear();
        //_positionsToRemove = null;

        _tempKVPList?.Clear();
        _tempKVPList = null;

        _pendingLockObject = null;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in CubeGridMapV2.Close: {ex}");
      }
      finally
      {
        base.Close();
      }
    }

    void HookEventsForGrid(MyCubeGrid grid)
    {
      try
      {
        if (grid != null)
        {
          grid.OnBlockAdded += OnBlockAddRemove;
          grid.OnBlockRemoved += OnBlockAddRemove;
          grid.OnGridSplit += OnGridSplit;
          grid.OnMarkForClose += CloseGrid;
          grid.OnClosing += CloseGrid;
          grid.OnClose += CloseGrid;
          grid.PositionComp.OnPositionChanged += OnGridPositionChanged;
          grid.OnStaticChanged += Grid_OnStaticChanged;
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in CubeGridMapV2.HookEventsForGrid: {ex}");
      }
    }

    private void Grid_OnStaticChanged(MyCubeGrid grid, bool isStatic)
    {
      if (isStatic && grid != null && !grid.MarkedForClose)
      {
        bool checkGravity = false;
        var planet = MyGamePruningStructure.GetClosestPlanet(OBB.Center);
        if (planet != null && planet.PositionComp.WorldVolume.Intersects(UnbufferedOBB.GetAABB()))
        {
          Vector3D[] corners;
          if (!AiSession.Instance.CornerArrayStack.TryPop(out corners) || corners == null)
            corners = new Vector3D[8];

          var gridOBB = new MyOrientedBoundingBoxD(OBB.Center, grid.PositionComp.LocalAABB.HalfExtents, Quaternion.CreateFromRotationMatrix(grid.WorldMatrix));
          gridOBB.GetCorners(corners, 0);

          using (planet.Pin())
          {
            for (int i = 0; i < corners.Length; i++)
            {
              if (PointInsideVoxel(corners[i], planet))
              {
                checkGravity = true;
                break;
              }
            }
          }

          AiSession.Instance.CornerArrayStack.Push(corners);

          if (checkGravity)
          {
            MatrixD newMatrix = WorldMatrix;
            Direction oldUpDir = Direction.Up;
            Direction newUpDir = Direction.Up;

            float interference;
            var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out interference);
            var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(OBB.Center, interference);
            if (aGrav.LengthSquared() > 0)
            {
              var up = -Vector3D.Normalize(aGrav);
              var fwd = Vector3D.CalculatePerpendicularVector(up);

              oldUpDir = WorldMatrix.GetClosestDirection(up);
              newUpDir = grid.WorldMatrix.GetClosestDirection(up);
              up = grid.WorldMatrix.GetDirectionVector(newUpDir);

              var dir = grid.WorldMatrix.GetClosestDirection(fwd);
              fwd = grid.WorldMatrix.GetDirectionVector(dir);

              newMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
            }
            else if (nGrav.LengthSquared() > 0)
            {
              var up = -Vector3D.Normalize(nGrav);
              var fwd = Vector3D.CalculatePerpendicularVector(up);

              oldUpDir = WorldMatrix.GetClosestDirection(up);
              newUpDir = grid.WorldMatrix.GetClosestDirection(up);
              up = grid.WorldMatrix.GetDirectionVector(newUpDir);

              var dir = grid.WorldMatrix.GetClosestDirection(fwd);
              fwd = grid.WorldMatrix.GetDirectionVector(dir);

              newMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
            }

            if (oldUpDir != newUpDir)
            {
              WorldMatrix = newMatrix;
              Dirty = true;
              Remake = true;
            }
          }
        }
      }
    }

    void UnhookEventsForGrid(MyCubeGrid grid)
    {
      try
      {
        if (grid != null)
        {
          grid.OnBlockAdded -= OnBlockAddRemove;
          grid.OnBlockRemoved -= OnBlockAddRemove;
          grid.OnGridSplit -= OnGridSplit;
          grid.OnMarkForClose -= CloseGrid;
          grid.OnClosing -= CloseGrid;
          grid.OnClose -= CloseGrid;
          grid.PositionComp.OnPositionChanged -= OnGridPositionChanged;
          grid.OnStaticChanged -= Grid_OnStaticChanged;
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in CubeGridMapV2.UnhookEventsForGrid: {ex}");
      }
    }

    void UpdateMainGrid()
    {
      try
      {
        Ready = false;
        Dirty = true;
        Remake = true;

        var lastMainGrid = MainGrid;

        for (int i = GridCollection.Count - 1; i >= 0; i--)
        {
          var grid = GridCollection[i] as MyCubeGrid;
          if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose || grid.GridSizeEnum == MyCubeSize.Small)
            continue;

          if (MainGrid == null || MainGrid.MarkedForClose || grid.PositionComp.WorldAABB.Volume > MainGrid.PositionComp.WorldAABB.Volume)
            MainGrid = grid;
        }

        if (MainGrid == null || MainGrid.MarkedForClose)
        {
          Close();
        }
        else
        {
          if (lastMainGrid != null && lastMainGrid.EntityId != MainGrid.EntityId)
          {
            UnhookEventsForGrid(lastMainGrid);
          }

          UnhookEventsForGrid(MainGrid);
          HookEventsForGrid(MainGrid);
        }

      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in CubeGridMapV2.UpdateMainGrid: {ex}");
      }
    }

    private void OnGridPositionChanged(MyPositionComponentBase positionComp)
    {
      try
      {
        if (!HasMoved && MainGrid != null && !MainGrid.MarkedForClose && !MainGrid.IsStatic)
        {
          var pos = MainGrid.WorldMatrix.Translation;
          if (Vector3D.DistanceSquared(_worldPosition, pos) < 1 && MainGrid.Physics.AngularVelocity.LengthSquared() < 0.001)
            return;

          HasMoved = true;
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in CubeGridMapV2.OnGridPositionChanged: {ex}");
      }
    }

    /// <summary>
    /// Checks if a given position is being repaired by a different repair bot
    /// </summary>
    /// <param name="gridEntityId">The EntityId of the grid</param>
    /// <param name="position">Block position on the grid</param>
    /// <param name="botId">Entity Id of the asking repair bot</param>
    /// <returns>true if a DIFFERENT bot is currently planning to repair the given block, otherwise false</returns>
    public bool IsTileBeingRepaired(long gridEntityId, Vector3I position, long botId)
    {
      ConcurrentDictionary<Vector3I, long> repairDict;
      if (SelectedRepairTiles.TryGetValue(gridEntityId, out repairDict))
      {
        long id;
        return repairDict.TryGetValue(position, out id) && id != botId;
      }

      return false;
    }

    /// <summary>
    /// Adds the bots repair target to the dictionary to keep other bots from trying to repair the same block
    /// </summary>
    /// <param name="gridEntityId">The EntityId of the grid</param>
    /// <param name="position">Block position on the grid</param>
    /// <param name="botId">Entity Id of the asking repair bot</param>
    /// <param name="botRepairTiles">The repair bot's repair dictionary</param>
    public void AddRepairTile(long gridEntityId, Vector3I position, long botId, Dictionary<long, HashSet<Vector3I>> botRepairTiles)
    {
      ConcurrentDictionary<Vector3I, long> repairDict;
      if (!SelectedRepairTiles.TryGetValue(gridEntityId, out repairDict))
      {
        repairDict = new ConcurrentDictionary<Vector3I, long>(Vector3I.Comparer);
        SelectedRepairTiles[gridEntityId] = repairDict;
      }

      repairDict[position] = botId;

      HashSet<Vector3I> tiles;
      if (!botRepairTiles.TryGetValue(gridEntityId, out tiles))
      {
        tiles = new HashSet<Vector3I>(Vector3I.Comparer);
        botRepairTiles[gridEntityId] = tiles;
      }

      tiles.Add(position);
    }

    /// <summary>
    /// Clears all repair tiles for the a bot
    /// </summary>
    /// <param name="tiles">The repair bot's current repair dictionary</param>
    public void RemoveRepairTiles(Dictionary<long, HashSet<Vector3I>> tiles)
    {
      ConcurrentDictionary<Vector3I, long> repairDict;

      foreach (var kvp in tiles)
      {
        if (SelectedRepairTiles.TryGetValue(kvp.Key, out repairDict))
        {
          foreach (var tile in kvp.Value)
          {
            long id;
            repairDict.TryRemove(tile, out id);
          }

          kvp.Value.Clear();
        }
      }
    }

    public void ResetMovement()
    {
      _worldPosition = MainGrid.WorldMatrix.Translation;
      WorldMatrix.Translation = _worldPosition;
      HasMoved = false;

      if (RootVoxel != null && !RootVoxel.MarkedForClose)
      {
        using (RootVoxel.Pin())
        {
          var tuple = RootVoxel?.GetVoxelContentInBoundingBox_Fast(BoundingBox, WorldMatrix, true) ?? MyTuple.Create(0f, 0f);

          if (tuple.Item1 > 0 || tuple.Item2 > 0)
          {
            Dirty = true;
          }
        }
      }
    }

    private void CloseGrid(MyEntity obj)
    {
      try
      {
        Ready = false;
        Dirty = true;

        var myGrid = obj as MyCubeGrid;
        if (myGrid != null)
        {
          myGrid.OnBlockAdded -= OnBlockAddRemove;
          myGrid.OnBlockRemoved -= OnBlockAddRemove;
          myGrid.OnGridSplit -= OnGridSplit;
          myGrid.OnMarkForClose -= CloseGrid;
          myGrid.OnClosing -= CloseGrid;
          myGrid.OnClose -= CloseGrid;
          myGrid.PositionComp.OnPositionChanged -= OnGridPositionChanged;

          InvokePositionsRemoved(true);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMapV2.CloseGrid: {ex}");
      }
    }

    public void RemovePlanetTiles()
    {
      if (!_asyncTileTask.IsComplete)
        return;

      if (IsValid && _asyncTileTask.Exceptions != null)
      {
        AiSession.Instance.Logger.ClearCached();
        AiSession.Instance.Logger.AddLine($"Exceptions found during RemovePlanetTiles task!\n");
        foreach (var ex in _asyncTileTask.Exceptions)
          AiSession.Instance.Logger.AddLine($" -> {ex}\n");

        AiSession.Instance.Logger.LogAll();
        MyAPIGateway.Utilities.ShowNotification($"Exception during task!");
      }

      if (!GraphLocked)
      {
        Ready = false;
        GraphLocked = true;
        PlanetTilesRemoved = true;

        _asyncTileTask = MyAPIGateway.Parallel.Start(RemovePlanetTilesAsync, SetReady);
      }
    }

    void RemovePlanetTilesAsync()
    {
      try
      {
        //AiSession.Instance.Logger.Log($"{this}.RemovePlanetTilesAsync: Start");
        var iter = new Vector3I_RangeIterator(ref BoundingBox.Min, ref BoundingBox.Max);

        while (iter.IsValid())
        {
          if (Dirty || Remake)
            return;

          var localPoint = iter.Current;
          iter.MoveNext();

          if (OpenTileDict.ContainsKey(localPoint))
            continue;

          // TODO: This doesn't look right... updating tiles, not removing them??
          // think this is so bots can still fly around while grid is moving!

          Node pn;
          if (!PlanetTileDictionary.TryGetValue(localPoint, out pn) || pn == null)
          {
            pn = AiSession.Instance.NodePool.Get();
          }

          pn.Update(localPoint, Vector3.Zero, this, NodeType.GridPlanet, 0, MainGrid);
        }

        foreach (var kvp in PlanetTileDictionary)
        {
          GetBlockedNodeEdges(kvp.Value);
        }

        //AiSession.Instance.Logger.Log($"{this}.RemovePlanetTilesAsync: Finished");
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in CubeGridMapV2.RemovePlanetTilesAsync: {ex}");
      }
    }

    private void OnBlockAddRemove(IMySlimBlock obj)
    {
      var grid = obj?.CubeGrid as MyCubeGrid;

      if (MainGrid == null || MainGrid.MarkedForClose)
      {
        Remake = true;
        Dirty = true;
      }
      else if (grid != null && !grid.MarkedForClose && !grid.IsPreview && grid.EntityId == MainGrid.EntityId)
      {
        // Only care about main grid because all subgrids are now obstacles

        var blockPos = obj.Position;
        _blockUpdateHash.Add(blockPos);

        NeedsBlockUpdate = true;
      }
    }

    private void OnGridSplit(MyCubeGrid originalGrid, MyCubeGrid newGrid)
    {
      if (MainGrid == null || MainGrid.MarkedForClose
        || originalGrid?.EntityId == MainGrid.EntityId
        || newGrid?.EntityId == MainGrid.EntityId)
      {
        Remake = true;
        Dirty = true;
      }
    }

    public override bool IsInBufferZone(Vector3D botPosition)
    {
      var allExt = OBB.HalfExtent;
      var ext = UnbufferedOBB.HalfExtent;
      var extra = (allExt - ext) * 0.75;

      var excludeOBB = new MyOrientedBoundingBoxD(UnbufferedOBB.Center, ext + extra, UnbufferedOBB.Orientation);
      return OBB.Contains(ref botPosition) && !excludeOBB.Contains(ref botPosition);
    }

    public override void Refresh() => Init();

    internal override void Init()
    {
      if (GraphLocked || !IsValid)
        return;

      GraphLocked = true;
      InteriorNodesReady = false;
      Ready = false;
      Dirty = false;
      Remake = false;
      ObstacleNodes.Clear();
      AllDoors.Clear();
      BlockedDoors.Clear();
      ExemptNodesUpper.Clear();
      ExemptNodesSide.Clear();
      WeaponPositions.Clear();
      InvokePositionsRemoved(true);

      if (AiSession.Instance?.NodePool != null)
      {
        foreach (var kvp in OpenTileDict)
        {
          var val = kvp.Value;
          AiSession.Instance.NodePool?.Return(ref val);
        }

        foreach (var kvp in PlanetTileDictionary)
        {
          var val = kvp.Value;
          AiSession.Instance.NodePool?.Return(ref val);
        }
      }

      OpenTileDict.Clear();
      PlanetTileDictionary.Clear();

      IsGridGraph = MainGrid != null && !MainGrid.MarkedForClose;
      if (IsGridGraph)
        MyAPIGateway.Parallel.StartBackground(InitGridArea, SetReady);

      // Testing only!
      //if (IsGridGraph)
      //{
      //  InitGridArea();
      //  SetReady();
      //}
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


    public override Vector3D LocalToWorld(Vector3I localVector) => MainGrid?.GridIntegerToWorld(localVector) ?? Vector3D.PositiveInfinity;

    public override Vector3I WorldToLocal(Vector3D worldVector) => MainGrid?.WorldToGridInteger(worldVector) ?? Vector3I.MaxValue;

    public override bool InBounds(Vector3I node) => BoundingBox.Contains(node) != ContainmentType.Disjoint;

    public override bool GetClosestValidNode(BotBase bot, Vector3I testPosition, out Vector3I localPosition, Vector3D? up = null, bool isSlimBlock = false, bool currentIsDenied = false, bool allowAirNodes = true, bool preferGroundNode = true)
    {
      localPosition = testPosition;
      if (!IsValid || !Ready)
        return false;

      Node node;
      TryGetNodeForPosition(testPosition, out node);

      if (bot != null && node != null && !currentIsDenied && !IsObstacle(testPosition, bot, true) && !TempBlockedNodes.ContainsKey(localPosition))
      {
        var isAir = node.IsAirNode;
        var isWater = node.IsWaterNode;
        if ((bot.RequiresJetpack || !preferGroundNode || !isAir) && (allowAirNodes || !isAir)
          && (!isWater || bot.CanUseWaterNodes) && (!isAir || bot.CanUseAirNodes)
          && (!bot.WaterNodesOnly || isWater) && (bot.CanUseSpaceNodes || !node.IsSpaceNode(this)))
        {

          if (isSlimBlock)
          {
            var cube = node.Block ?? GetBlockAtPosition(testPosition);
            if (cube == null
              || (cube.BlockDefinition as MyCubeBlockDefinition)?.HasPhysics != true
              || (cube.FatBlock is IMyTextPanel && !AiSession.Instance.SlopeBlockDefinitions.Contains(cube.BlockDefinition.Id))
              || AiSession.Instance.PassageBlockDefinitions.Contains(cube.BlockDefinition.Id)
              || AiSession.Instance.CatwalkBlockDefinitions.Contains(cube.BlockDefinition.Id)
              || AiSession.Instance.RailingBlockDefinitions.ContainsItem(cube.BlockDefinition.Id)
              || AiSession.Instance.FlatWindowDefinitions.ContainsItem(cube.BlockDefinition.Id)
              || cube.BlockDefinition.Id.SubtypeName.EndsWith("PassageStairs_Large"))
            {
              return true;
            }
          }
          else
            return true;
        }
      }

      if (isSlimBlock)
      {
        var block = node?.Block != null ? node.Block : GetBlockAtPosition(localPosition, true);

        if (node?.IsGridNodeUnderGround == true)
        {
          var grid = block?.CubeGrid ?? node.Grid;
          if (grid != null)
          {
            var upDir = grid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
            var intVec = Base6Directions.GetIntVector(upDir);

            localPosition = testPosition + intVec;

            if (PointInsideVoxel(localPosition, RootVoxel))
            {
              localPosition = testPosition - intVec;
            }
          }
        }

        if (block != null && !block.IsDestroyed)
        {
          var min = block.Min;
          var max = block.Max;

          if ((max - min) != Vector3I.Zero)
          {
            if (block.CubeGrid.EntityId != MainGrid.EntityId)
            {
              var minWorld = block.CubeGrid.GridIntegerToWorld(block.Min);
              var maxWorld = block.CubeGrid.GridIntegerToWorld(block.Max);
              min = MainGrid.WorldToGridInteger(minWorld);
              max = MainGrid.WorldToGridInteger(maxWorld);
            }

            Vector3I.MinMax(ref min, ref max);
            Vector3I_RangeIterator iter = new Vector3I_RangeIterator(ref min, ref max);

            while (iter.IsValid())
            {
              var current = iter.Current;
              iter.MoveNext();

              //if (current != block.Position && TryGetNodeForPosition(current, out node) && node != null)
              //{
              //  if (allowAirNodes || !node.IsAirNode) 
              //    return true;
              //}

              if (GetClosestNodeInternal(bot, current, out localPosition, up, isSlimBlock, currentIsDenied, allowAirNodes, preferGroundNode)
                && TryGetNodeForPosition(localPosition, out node) && node.Block?.BlockDefinition.Id.SubtypeName.Contains("Stair") != true)
                return true;
            }

            return false;
          }
        }
      }

      return GetClosestNodeInternal(bot, testPosition, out localPosition, up, isSlimBlock, currentIsDenied, allowAirNodes, preferGroundNode);
    }

    public bool CanBotUseTile(BotBase bot, Node node, bool allowAirNodes = true)
    {
      if (node == null || bot?.Character == null || bot.IsDead)
        return false;

      var isAir = node.IsAirNode;
      var isWater = node.IsWaterNode;
      if ((allowAirNodes || !isAir) && (!isWater || bot.CanUseWaterNodes) && (!isAir || bot.CanUseAirNodes) && (bot.CanUseSpaceNodes || !node.IsSpaceNode(this)))
      {
        return !bot.WaterNodesOnly || !isWater;
      }

      return false;
    }

    bool GetClosestNodeInternal(BotBase bot, Vector3I testPosition, out Vector3I localPosition, Vector3D? up = null, bool isSlimBlock = false, bool currentIsDenied = false, bool allowAirNodes = true, bool preferGroundNode = true)
    {
      localPosition = testPosition;
      Node node;

      if (!currentIsDenied
        && !IsObstacle(localPosition, bot, true)
        && TryGetNodeForPosition(localPosition, out node))
      {
        var isAir = node.IsAirNode;
        var isWater = node.IsWaterNode;
        if ((bot.RequiresJetpack || !preferGroundNode || !isAir) && (allowAirNodes || !isAir)
          && (!isWater || bot.CanUseWaterNodes) && (!isAir || bot.CanUseAirNodes)
          && (!bot.WaterNodesOnly || isWater) && (bot.CanUseSpaceNodes || !node.IsSpaceNode(this)))
        {

          if (isSlimBlock)
          {
            var cube = node.Block ?? GetBlockAtPosition(testPosition);
            if (cube == null
              || (cube.BlockDefinition as MyCubeBlockDefinition)?.HasPhysics != true
              || (cube.FatBlock is IMyTextPanel && !AiSession.Instance.SlopeBlockDefinitions.Contains(cube.BlockDefinition.Id))
              || AiSession.Instance.PassageBlockDefinitions.Contains(cube.BlockDefinition.Id)
              || AiSession.Instance.CatwalkBlockDefinitions.Contains(cube.BlockDefinition.Id)
              || AiSession.Instance.RailingBlockDefinitions.ContainsItem(cube.BlockDefinition.Id)
              || AiSession.Instance.FlatWindowDefinitions.ContainsItem(cube.BlockDefinition.Id)
              || cube.BlockDefinition.Id.SubtypeName.EndsWith("PassageStairs_Large"))
            {
              return true;
            }
          }
          else
            return true;
        }
      }

      var rBot = bot as RepairBot;
      bool checkRepairInfo = false;
      IMySlimBlock slimTgt = null;

      if (rBot != null)
      {
        slimTgt = rBot.Target.Entity as IMySlimBlock ?? rBot.CurrentlyConsideredTarget;

        if (slimTgt != null)
        {
          var position = rBot.GetAdjustedLocalTargetPosition(slimTgt);
          checkRepairInfo = position == testPosition;
        }
      }

      List<MyEntity> entList = AiSession.Instance.EntListPool.Get();

      var center = localPosition;
      double localDistance = double.MaxValue;
      double groundDistance = double.MaxValue;
      var worldPosition = MainGrid.GridIntegerToWorld(localPosition);
      Vector3I? closestGround = null;

      //AiSession.Instance.Logger.AddLine($"GetClosestNode: Checking Neighbors for {testPosition}, Center = {center}");
      foreach (var point in Neighbors(bot, center, center, worldPosition, true, true, isSlimBlock, up, checkRepairInfo: checkRepairInfo))
      {
        TryGetNodeForPosition(point, out node);
        var isAirNode = node.IsAirNode;
        var isWaterNode = node.IsWaterNode;

        if (!allowAirNodes && isAirNode && !bot.RequiresJetpack)
          continue;

        KeyValuePair<IMyCubeGrid, bool> kvp;
        if (ObstacleNodes.TryGetValue(point, out kvp))
        {
          if (!checkRepairInfo || (kvp.Key?.EntityId == slimTgt.CubeGrid.EntityId && !kvp.Value))
            continue;

          //entList.Clear();
          //var worldPoint = LocalToWorld(point);
          //var sphere = new BoundingSphereD(worldPoint, 0.2);
          //MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entList);

          //bool allowed = true;
          //for (int i = 0; i < entList.Count; i++)
          //{
          //  var grid = entList[i] as MyCubeGrid;
          //  if (grid != null)
          //  {
          //    var gridLocal = grid.WorldToGridInteger(worldPoint);
          //    var cube = grid.GetCubeBlock(gridLocal) as IMySlimBlock;
          //    if (cube != null && ((MyCubeBlockDefinition)cube.BlockDefinition).HasPhysics)
          //    {
          //      allowed = false;
          //      break;
          //    }
          //  }
          //}

          //if (!allowed)
          //  continue;
        }
        else if (TempBlockedNodes.ContainsKey(point))
        {
          continue;
        }

        IMyDoor door;
        if (BlockedDoors.TryGetValue(point, out door) && door != null)
        {
          if (bot == null || bot.Target.Entity == null)
            continue;

          var ch = bot.Target.Entity as IMyCharacter;
          if (ch != null && ch == bot.Owner?.Character)
            continue;

          var grid = door.CubeGrid;
          long gridOwner;
          try
          {
            gridOwner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : door.OwnerId;
          }
          catch
          {
            gridOwner = door.OwnerId;
          }

          var botOwner = bot.Owner?.IdentityId ?? bot.BotIdentityId;

          var rel = MyIDModule.GetRelationPlayerPlayer(gridOwner, botOwner);
          if (rel != MyRelationsBetweenPlayers.Enemies)
            continue;
        }

        if (bot != null && !isWaterNode && bot.WaterNodesOnly)
          continue;

        if (isSlimBlock)
        {
          var block = node.Block;
          if (block != null && (AiSession.Instance.RampBlockDefinitions.Contains(block.BlockDefinition.Id)
            || AiSession.Instance.SlopeBlockDefinitions.Contains(block.BlockDefinition.Id)))
          {
            continue;
          }
        }

        if (bot == null || ((!isAirNode || bot.CanUseAirNodes) && (!isWaterNode || bot.CanUseWaterNodes) && (bot.CanUseSpaceNodes || !node.IsSpaceNode(this))))
        {
          var testPositionWorld = MainGrid.GridIntegerToWorld(point);
          var dist = Vector3D.DistanceSquared(testPositionWorld, worldPosition);

          if (dist < localDistance)
          {
            localDistance = dist;
            localPosition = point;
          }

          if (preferGroundNode && !isAirNode && dist < groundDistance)
          {
            groundDistance = dist;
            closestGround = point;
          }
        }
      }

      AiSession.Instance.EntListPool?.Return(ref entList);

      if (preferGroundNode && closestGround.HasValue)
        localPosition = closestGround.Value;

      //AiSession.Instance.Logger.AddLine($" -> Finished: FinalPosition = {localPosition}, Valid = {localDistance < double.MaxValue}\n");
      return localDistance < double.MaxValue;
    }

    public override IEnumerable<Vector3I> Neighbors(BotBase bot, Vector3I previousNode, Vector3I currentNode, Vector3D worldPosition, bool checkDoors, bool currentIsObstacle = false, bool isSlimBlock = false, Vector3D? up = null, bool checkRepairInfo = false)
    {
      Vector3I upVec = Vector3I.Zero;
      var rBot = bot as RepairBot;
      var checkAdditionalTile = false;

      if (rBot != null && checkRepairInfo)
      {
        var repairTgt = rBot.CurrentlyConsideredTarget ?? rBot.Target.Entity as IMySlimBlock;
        checkAdditionalTile = repairTgt?.CubeGrid.GridSizeEnum == MyCubeSize.Small;
      }

      if (up.HasValue)
      {
        var upDir = WorldMatrix.GetClosestDirection(up.Value);
        upVec = Base6Directions.GetIntVector(upDir);
      }

     // AiSession.Instance.Logger.AddLine($" -> Bot = {bot}, Prev = {previousNode}, Cur = {currentNode}, CurIsObs = {currentIsObstacle}, IsSlim = {isSlimBlock}, Up = {upVec}");

     // AiSession.Instance.Logger.AddLine($" -> Cardinals: ");
      foreach (var dir in AiSession.Instance.CardinalDirections)
      {
       // AiSession.Instance.Logger.AddLine($"  -> Dir = {dir}, Next = {currentNode + dir}");
        if (dir.Dot(ref upVec) != 0)
        {
         // AiSession.Instance.Logger.AddLine($"   -> Dot Up != 0, continuing");
          continue;
        }

        var next = currentNode + dir;
        if (TestCondition(next))
        {
          MyAPIGateway.Utilities.ShowNotification($"Hi", 1);
        }

        if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, checkDoors, 
          currentIsObstacle: currentIsObstacle, isSlimBlock: isSlimBlock, checkRepairInfo: checkRepairInfo))
        {
          yield return next;
        }
        //else if (!InBounds(next))
        //{
        //  AiSession.Instance.Logger.AddLine($"   -> Not in bounds");
        //}
        //else
        //  AiSession.Instance.Logger.AddLine($"   -> Not passable");

        if (checkAdditionalTile)
        {
          next = currentNode + dir * 2;
         // AiSession.Instance.Logger.AddLine($"   -> Checking additional: Next = {next}");

          if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, checkDoors,
            currentIsObstacle: currentIsObstacle, isSlimBlock: isSlimBlock, checkRepairInfo: checkRepairInfo))
          {
            yield return next;
          }
          //else if (!InBounds(next))
          //{
          //  AiSession.Instance.Logger.AddLine($"   -> Not in bounds");
          //}
          //else
          //  AiSession.Instance.Logger.AddLine($"   -> Not passable");
        }
      }

      bool isVoxelNode = false;
      Node node;
      if (TryGetNodeForPosition(currentNode, out node))
      {
        isVoxelNode = !node.IsGridNode || node.IsGridNodePlanetTile;
      }

      if (isSlimBlock)
      {
        if (node?.IsGridNodeUnderGround == true)
        {
         // AiSession.Instance.Logger.AddLine($" -> VoxelMovements (underground): ");
          foreach (var dir in AiSession.Instance.VoxelMovementDirections)
          {
           // AiSession.Instance.Logger.AddLine($"  -> Dir = {dir}, Next = {currentNode + dir}");
            if (dir.Dot(ref upVec) != 0)
            {
             // AiSession.Instance.Logger.AddLine($"   -> Dot Up != 0, continuing");
              continue;
            }

            var next = currentNode + dir;
            if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, checkDoors,
              currentIsObstacle: currentIsObstacle, isSlimBlock: isSlimBlock, checkRepairInfo: checkRepairInfo))
            {
              yield return next;
            }
            //else if (!InBounds(next))
            //{
            //  AiSession.Instance.Logger.AddLine($"   -> Not in bounds");
            //}
            //else
            //  AiSession.Instance.Logger.AddLine($"   -> Not passable");
          }
        }

        yield break;
      }

      if (isVoxelNode || (currentIsObstacle && bot?.Target.IsFloater != true))
      {
       // AiSession.Instance.Logger.AddLine($" -> Diagonals: ");
        foreach (var dir in AiSession.Instance.DiagonalDirections)
        {
         // AiSession.Instance.Logger.AddLine($"  -> Dir = {dir}, Next = {currentNode + dir}");
          if (dir.Dot(ref upVec) != 0)
          {
           // AiSession.Instance.Logger.AddLine($"   -> Dot Up != 0, continuing");
            continue;
          }

          var next = currentNode + dir;
          if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, checkDoors,
            currentIsObstacle: currentIsObstacle, isSlimBlock: isSlimBlock, checkRepairInfo: checkRepairInfo))
          {
            yield return next;
          }
          //else if (!InBounds(next))
          //{
          //  AiSession.Instance.Logger.AddLine($"   -> Not in bounds");
          //}
          //else
          //  AiSession.Instance.Logger.AddLine($"   -> Not passable");
        }
      }

      if (!isVoxelNode || currentIsObstacle)
      {
        yield break;
      }

     // AiSession.Instance.Logger.AddLine($" -> VoxelMovements: ");
      foreach (var dir in AiSession.Instance.VoxelMovementDirections)
      {
       // AiSession.Instance.Logger.AddLine($"  -> Dir = {dir}, Next = {currentNode + dir}");
        if (dir.Dot(ref upVec) != 0)
        {
         // AiSession.Instance.Logger.AddLine($"   -> Dot Up != 0, continuing");
          continue;
        }

        var next = currentNode + dir;
        if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, checkDoors, checkRepairInfo: checkRepairInfo))
        {
          yield return next;
        }
        //else if (!InBounds(next))
        //{
        //  AiSession.Instance.Logger.AddLine($"   -> Not in bounds");
        //}
        //else
        //  AiSession.Instance.Logger.AddLine($"   -> Not passable");
      }
    }

    public override bool Passable(BotBase bot, Vector3I previousNode, Vector3I currentNode, Vector3I nextNode, Vector3D worldPosition, bool checkDoors, bool currentIsObstacle = false, bool isSlimBlock = false, bool checkRepairInfo = false)
    {

      if (TestCondition(currentNode))
      {
        MyAPIGateway.Utilities.ShowNotification($"Hi", 1);
      }

      IMyDoor door;
      if (checkDoors && BlockedDoors.TryGetValue(nextNode, out door) && door != null)
      {
        if (bot == null || bot.Target.Entity == null)
          return false;

        var ch = bot.Target.Entity as IMyCharacter;
        if (ch != null && ch == bot.Owner?.Character)
          return false;

        var grid = door.CubeGrid;
        long gridOwner;
        try
        {
          gridOwner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : door.OwnerId;
        }
        catch
        {
          gridOwner = door.OwnerId;
        }

        var botOwner = bot.Owner?.IdentityId ?? bot.BotIdentityId;

        var rel = MyIDModule.GetRelationPlayerPlayer(gridOwner, botOwner);
        if (rel != MyRelationsBetweenPlayers.Enemies)
          return false;
      }

      if (TempBlockedNodes.ContainsKey(nextNode) || bot?._pathCollection?.Obstacles.ContainsKey(nextNode) == true)
      {
        return false;
      }

      KeyValuePair<IMyCubeGrid, bool> kvp;
      bool obstacle = ObstacleNodes.TryGetValue(nextNode, out kvp);
      if (!checkRepairInfo)
      {
        if (obstacle)
          return false;
      }
      else if (obstacle && !kvp.Value)
      {
        return false;
      }

      Node nNext;
      if (!TryGetNodeForPosition(nextNode, out nNext))
      {
        return false;
      }

      var nextIsWater = nNext.IsWaterNode;
      if ((nextIsWater && !bot.CanUseWaterNodes) || (!nextIsWater && bot.WaterNodesOnly)
        || (nNext.IsAirNode && !bot.CanUseAirNodes) || (!bot.CanUseSpaceNodes && nNext.IsSpaceNode(this)))
      {
        return false;
      }

      if (isSlimBlock)
      {
        if (TempBlockedNodes.ContainsKey(currentNode) || (bot?.Target.IsFloater == true && DoesBlockExist(nextNode)))
        {
          return false;
        }

        if (RootVoxel != null && !nNext.IsGridNodeUnderGround)
        {
          Vector3D worldNext = MainGrid.GridIntegerToWorld(nNext.Position) + nNext.Offset;

          if (PointInsideVoxel(worldNext, RootVoxel))
            return false;
        }

        return true;
      }

      if (currentIsObstacle)
      {
        var castTo = LocalToWorld(nextNode);
        var castFrom = LocalToWorld(currentNode);
        var dirToNext = Vector3D.Normalize(castTo - castFrom);
        castFrom -= dirToNext * CellSize * 0.4;

        List<IHitInfo> hitInfoList = AiSession.Instance.HitListPool.Get();

        MyAPIGateway.Physics.CastRay(castFrom, castTo, hitInfoList, CollisionLayers.CharacterCollisionLayer);

        var result = true;
        for (int i = 0; i < hitInfoList.Count; i++)
        {
          var hit = hitInfoList[i];
          var grid = hit?.HitEntity?.GetTopMostParent() as MyCubeGrid;
          if (grid != null)
          {
            if (grid.GridSizeEnum == MyCubeSize.Small)
            {
              var gridLocal = grid.WorldToGridInteger(castTo);
              if (grid.CubeExists(gridLocal))
              {
                result = false;
                break;
              }
            }
            else
            {
              result = false;
              break;
            }
          }
        }

        AiSession.Instance.HitListPool?.Return(ref hitInfoList);
        return result;
      }

      if (nNext.IsBlocked(currentNode - nextNode))
      {
        return false;
      }

      Node nCur;
      if (!TryGetNodeForPosition(currentNode, out nCur) || nCur.IsBlocked(nextNode - currentNode))
      {
        return false;
      }

      if (RootVoxel != null && !nCur.IsGridNodeUnderGround && !nNext.IsGridNodeUnderGround)
      {
        Vector3D worldCurrent = MainGrid.GridIntegerToWorld(nCur.Position) + nCur.Offset;
        Vector3D worldNext = MainGrid.GridIntegerToWorld(nNext.Position) + nNext.Offset;

        using (RootVoxel.Pin())
        {
          if (LineIntersectsVoxel(ref worldCurrent, ref worldNext, RootVoxel))
            return false;
        }
      }

      var movement = nextNode - currentNode;
      if (movement.RectangularLength() > 1)
      {
        Node testNode;
        if (movement.Y != 0)
        {
          var dirVec = new Vector3I(0, movement.Y, 0);
          var testPosition = currentNode + dirVec;

          if (DoesBlockExist(testPosition) == true || (TryGetNodeForPosition(testPosition, out testNode) && (testNode.Block != null || nCur.IsBlocked(dirVec) || testNode.IsBlocked(-dirVec))))
          {
            return false;
          }
        }

        if (movement.X != 0)
        {
          var dirVec = new Vector3I(movement.X, 0, 0);
          var testPosition = currentNode + dirVec;

          if (DoesBlockExist(testPosition) == true || (TryGetNodeForPosition(testPosition, out testNode) && (testNode.Block != null || nCur.IsBlocked(dirVec) || testNode.IsBlocked(-dirVec))))
          {
            return false;
          }
        }

        if (movement.Z != 0)
        {
          var dirVec = new Vector3I(0, 0, movement.Z);
          var testPosition = currentNode + dirVec;

          if (DoesBlockExist(testPosition) == true || (TryGetNodeForPosition(testPosition, out testNode) && (testNode.Block != null || nCur.IsBlocked(dirVec) || testNode.IsBlocked(-dirVec))))
          {
            return false;
          }
        }
      }

      Vector3 totalMovement = nextNode - previousNode;
      Vector3 botUpVec = Base6Directions.GetIntVector(MainGrid.WorldMatrix.GetClosestDirection(WorldMatrix.Up));

      var checkVec = Vector3I.Round(Vector3.ProjectOnVector(ref totalMovement, ref botUpVec));
      var checkTotal = checkVec.RectangularLength();

      if (checkTotal > 1)
      {
        if (!nCur.IsGridNode && nNext.IsGridNode)
        {
          return false;
        }

        IMySlimBlock prevBlock = GetBlockAtPosition(previousNode);
        IMySlimBlock curBlock = GetBlockAtPosition(currentNode);
        bool usePrevCur = prevBlock != null && curBlock != null 
          && (prevBlock != curBlock || IsIndustrialRefinery(prevBlock));

        IMySlimBlock nextBlock = null;
        bool useCurNext = false;
        if (!usePrevCur)
        {
          nextBlock = GetBlockAtPosition(nextNode);
          useCurNext = curBlock != null && nextBlock != null
            && (curBlock != nextBlock || IsIndustrialRefinery(curBlock));

          usePrevCur &= !useCurNext;
        }

        if (usePrevCur || useCurNext)
        {
          IMySlimBlock from = usePrevCur ? prevBlock : curBlock;
          IMySlimBlock to = usePrevCur ? curBlock : nextBlock;

          bool isIndustrialRefinery = from.BlockDefinition.Id == to.BlockDefinition.Id && IsIndustrialRefinery(from);

          bool bothHalfStairs = !isIndustrialRefinery
            && AiSession.Instance.HalfStairBlockDefinitions.Contains(from.BlockDefinition.Id)
            && AiSession.Instance.HalfStairBlockDefinitions.Contains(to.BlockDefinition.Id);

          if (isIndustrialRefinery || bothHalfStairs)
          {
            Vector3I insertPosition;
            if (!GetValidPositionForStackedStairs(currentNode, out insertPosition))
            {
              return false;
            }

            bot._pathCollection.StackedStairsFound.Enqueue(MyTuple.Create(previousNode, currentNode, insertPosition));
          }
        }
      }
      else if (checkTotal > 0 && !nCur.IsGridNode && nNext.IsGridNode)
      {
        return false;
      }

      return true;
    }

    bool IsIndustrialRefinery(IMySlimBlock block)
    {
      return block?.FatBlock is IMyRefinery && block.BlockDefinition.Id.SubtypeName == "LargeRefineryIndustrial";
    }

    public bool GetValidPositionForStackedStairs(Vector3I stairPosition, out Vector3I adjusted)
    {
      var botUp = MainGrid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
      var upVec = Base6Directions.GetIntVector(botUp);
      Vector3I center = stairPosition;

      foreach (var dir in AiSession.Instance.CardinalDirections)
      {
        if (dir.Dot(ref upVec) != 0)
          continue;

        adjusted = center + dir;
        if (!BlockedDoors.ContainsKey(adjusted) && !ObstacleNodes.ContainsKey(adjusted) && !TempBlockedNodes.ContainsKey(adjusted) && IsOpenTile(adjusted))
        {
          Node node;
          if (TryGetNodeForPosition(center, out node) && !node.IsBlocked(dir))
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
        if (AiSession.Instance == null || !AiSession.Instance.Registered)
          return;

        //AiSession.Instance.Logger.Log($"{this}.InitGridArea: Start");

        ApiWorkData data = AiSession.Instance.ApiWorkDataPool.Get();

        data.Grid = MainGrid;
        data.NodeList = InteriorNodeList;
        data.EnclosureRating = 5;
        data.AirtightNodesOnly = false;
        data.AllowAirNodes = false;
        data.CallBack = null;

        BotFactory.GetInteriorNodes(data);
        AiSession.Instance.ApiWorkDataPool?.Return(ref data);
        InteriorNodesReady = true;

        List<IMySlimBlock> blocks = AiSession.Instance.SlimListPool.Get();
        Direction upDir;
        Vector3I upVec;

        var cellSize = CellSize;
        var halfCellSize = CellSize * 0.5f;

        BoundingBox = GetMapBoundingBoxLocal();
        upDir = MainGrid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
        upVec = Base6Directions.GetIntVector(upDir);

        try
        {
          blocks.Clear();
          ((IMyCubeGrid)MainGrid).GetBlocks(blocks);
        }
        catch // in case the grid's blocks are updated while this is iterating
        {
          blocks.Clear();
          Dirty = true;
          Remake = true;
        }

        foreach (var block in blocks)
        {
          if (Dirty || Remake)
            break;

          if (block == null || block.IsDestroyed)
            continue;

          if (block.FatBlock is IMyUserControllableGun)
          {
            WeaponPositions.Add(block.Position);
          }

          var cubeDef = block.BlockDefinition as MyCubeBlockDefinition;
          if (cubeDef == null || !cubeDef.HasPhysics || block.FatBlock is IMyButtonPanel
            || AiSession.Instance.ButtonPanelDefinitions.ContainsItem(cubeDef.Id)
            || block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_MotorRotor))
            continue;

          if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(cubeDef.Id))
            continue;

          InitBlockV2(block, ref upVec, ref upDir, ref cellSize, ref halfCellSize, cubeDef);
        }

        AiSession.Instance.SlimListPool?.Return(ref blocks);

        if (Dirty || Remake)
          return;

        var worldCenter = LocalToWorld(BoundingBox.Center);
        var quat = Quaternion.CreateFromRotationMatrix(MainGrid.WorldMatrix);
        var halfVector = Vector3D.Half * cellSize;
        UnbufferedOBB = new MyOrientedBoundingBoxD(worldCenter, BoundingBox.HalfExtents * cellSize + halfVector, quat);

        BoundingBox.Inflate(_boxExpansion);
        OBB = new MyOrientedBoundingBoxD(worldCenter, BoundingBox.HalfExtents * cellSize + halfVector, quat);

        List<MyVoxelBase> voxelMaps = AiSession.Instance.VoxelMapListPool.Get();

        var obbCenter = OBB.Center;
        var distanceToOBBCenter = double.MaxValue;

        float _;
        Vector3 gravityNorm;
        var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(obbCenter, out _);
        if (nGrav.LengthSquared() > 0)
        {
          RootVoxel = MyGamePruningStructure.GetClosestPlanet(WorldMatrix.Translation);
          gravityNorm = Vector3.Normalize(nGrav);
          distanceToOBBCenter = Vector3D.DistanceSquared(RootVoxel.PositionComp.WorldAABB.Center, obbCenter);

          var aabb = OBB.GetAABB();
          MyGamePruningStructure.GetAllVoxelMapsInBox(ref aabb, voxelMaps);
          if (voxelMaps.Count > 0)
          {
            MyVoxelBase rootVoxel = null;
            for (int i = 0; i < voxelMaps.Count; i++)
            {
              var voxel = voxelMaps[i]?.RootVoxel;
              if (voxel != null && voxel.EntityId != rootVoxel?.EntityId)
              {
                var dSquared = Vector3D.DistanceSquared(voxel.PositionComp.WorldAABB.Center, obbCenter);
                if (dSquared < distanceToOBBCenter)
                {
                  distanceToOBBCenter = dSquared;
                  rootVoxel = voxel;
                }
              }
            }

            if (rootVoxel != null)
            {
              RootVoxel = rootVoxel;

              var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(OBB.Center, 0);
              if (aGrav.LengthSquared() > 0)
                gravityNorm = Vector3D.Normalize(aGrav);
            }
          }
        }
        else
        {
          var aabb = OBB.GetAABB();
          MyGamePruningStructure.GetAllVoxelMapsInBox(ref aabb, voxelMaps);
          if (voxelMaps.Count > 0)
          {
            MyVoxelBase rootVoxel = null;
            for (int i = 0; i < voxelMaps.Count; i++)
            {
              var voxel = voxelMaps[i]?.RootVoxel;
              if (voxel != null && voxel.EntityId != rootVoxel?.EntityId)
              {
                var dSquared = Vector3D.DistanceSquared(voxel.PositionComp.WorldAABB.Center, obbCenter);
                if (dSquared < distanceToOBBCenter)
                {
                  distanceToOBBCenter = dSquared;
                  rootVoxel = voxel;
                }
              }
            }

            if (rootVoxel != null)
            {
              RootVoxel = rootVoxel;

              var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(OBB.Center, 0);
              if (aGrav.LengthSquared() > 0)
                gravityNorm = Vector3D.Normalize(aGrav);
              else
                gravityNorm = (Vector3)WorldMatrix.Down;
            }
            else
            {
              RootVoxel = null;
              gravityNorm = (Vector3)WorldMatrix.Down;
            }
          }
          else
          {
            RootVoxel = null;
            gravityNorm = (Vector3)WorldMatrix.Down;
          }
        }

        AiSession.Instance.VoxelMapListPool?.Return(ref voxelMaps);

        upDir = MainGrid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
        upVec = Base6Directions.GetIntVector(upDir);
        CheckForPlanetTiles(ref BoundingBox, ref gravityNorm, ref upVec);
        PlanetTilesRemoved = false;

        foreach (var kvp in OpenTileDict)
        {
          if (Dirty || Remake)
            return;

          var tile = kvp.Value;
          if (tile.IsGridNodeAdditional)
            continue;

          GetBlockedNodeEdges(tile);

          var worldNode = MainGrid.GridIntegerToWorld(tile.Position);
          if (PointInsideVoxel(worldNode, RootVoxel))
          {
            tile.SetNodeType(NodeType.GridUnderground, this);

            if (tile.Offset == Vector3.Zero)
              tile.Offset = WorldMatrix.Up * halfCellSize;
          }
        }

        foreach (var kvp in PlanetTileDictionary)
        {
          if (Dirty || Remake)
            return;

          var tile = kvp.Value;
          GetBlockedNodeEdges(tile);
        }

        //if (!MainGrid.Physics.IsStatic)
        //  AddAdditionalGridTiles();

        //AiSession.Instance.Logger.Log($"{this}.InitGridArea: Finished");
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception during InitGridArea: {ex}");
      }
    }

    void InitBlockV2(IMySlimBlock block, ref Vector3I upVec, ref Direction upDir, ref float cellSize, ref float halfCellSize, MyCubeBlockDefinition cubeDef)
    {
      if (cubeDef == null)
        cubeDef = block.BlockDefinition as MyCubeBlockDefinition;

      CheckFaces(block, upVec, cubeDef);

      var position = block.Position;
      var mainGridPosition = position;

      var blockDirInfo = AiSession.Instance.BlockInfo.BlockDirInfo;
      var blockMatrix = new MatrixI(block.Orientation);
      var center = Vector3I.TransformNormal(cubeDef.Center, ref blockMatrix);
      var adjustedPosition = block.Position - center;


      Matrix blockMatrixWorld;
      if (block.FatBlock != null)
      {
        blockMatrixWorld = (Matrix)block.FatBlock.WorldMatrix;
      }
      else
      {
        block.Orientation.GetMatrix(out blockMatrixWorld);
      }

      var door = block.FatBlock as IMyDoor;
      if (door != null)
      {
        if (door.MarkedForClose)
          return;

        AllDoors[mainGridPosition] = door;

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
      }
      else if (AiSession.Instance.SlopeBlockDefinitions.Contains(cubeDef.Id)
        || AiSession.Instance.RampBlockDefinitions.Contains(cubeDef.Id)
        || AiSession.Instance.LadderBlockDefinitions.Contains(cubeDef.Id))
      {
        MatrixI blockMatrixInv;
        MatrixI.Invert(ref blockMatrix, out blockMatrixInv);
        Vector3I localUpVec = Vector3I.TransformNormal(upVec, ref blockMatrixInv);
        var localUpDir = Base6Directions.GetDirectionFlag(Base6Directions.GetDirection(localUpVec));
        var localDwnDir = Base6Directions.GetDirectionFlag(Base6Directions.GetDirection(-localUpVec));

        if (!cubeDef.Context.IsBaseGame && cubeDef.Context.ModName == "PassageIntersections" && cubeDef.Id.SubtypeName.Contains("PassageStairs"))
        {
          foreach (var cell in cubeDef.IsCubePressurized.Keys)
          {
            UsableEntry entry;
            var tuple = MyTuple.Create(cubeDef.Id, cell);
            if (blockDirInfo.TryGetValue(tuple, out entry))
            {
              var gridCell = adjustedPosition + Vector3I.TransformNormal(cell, ref blockMatrix);
              mainGridPosition = gridCell;

              var offset = entry.GetOffset(block);
              var nType = entry.IsGroundNode ? NodeType.Ground : NodeType.None;

              Node n;
              if (!OpenTileDict.TryGetValue(mainGridPosition, out n))
              {
                Node newNode = AiSession.Instance.NodePool.Get();
                newNode.Update(mainGridPosition, offset, this, nType, 0, MainGrid, block);

                AddTileToMap(mainGridPosition, newNode);
              }
              else
              {
                if (n.Offset == Vector3.Zero)
                {
                  n.Offset = offset;
                }

                if (!n.IsGroundNode && nType == NodeType.Ground)
                { 
                  n.SetNodeType(NodeType.Ground, this);
                }
              }
            }
          }
        }
        else
        {
          bool isLadder = AiSession.Instance.LadderBlockDefinitions.Contains(cubeDef.Id);

          foreach (var cell in cubeDef.IsCubePressurized.Keys)
          {
            var tuple = MyTuple.Create(cubeDef.Id, cell);

            UsableEntry entry;
            if (blockDirInfo.TryGetValue(tuple, out entry))
            {
              var dirFlags = entry.Mask;
              bool forceGroundNode = (dirFlags & localUpDir) == 0;
              var gridCell = adjustedPosition + Vector3I.TransformNormal(cell, ref blockMatrix);
              mainGridPosition = gridCell;

              var offset = entry.GetOffset(block);
              var nType = (forceGroundNode || entry.IsGroundNode) ? NodeType.Ground : NodeType.None;

              Node nMain;
              if (!OpenTileDict.TryGetValue(mainGridPosition, out nMain))
              {
                Node newNode = AiSession.Instance.NodePool.Get();
                newNode.Update(mainGridPosition, offset, this, nType, 0, MainGrid, block);
                AddTileToMap(mainGridPosition, newNode);
              }
              else
              {
                if (!nMain.IsGroundNode && nType == NodeType.Ground)
                { 
                  nMain.SetNodeType(NodeType.Ground, this);
                }

                if (nMain.Offset == Vector3.Zero)
                {
                  nMain.Offset = offset;
                }
              }

              if (forceGroundNode)
              {
                var positionAbove = mainGridPosition + upVec;
                var cubeAbove = GetBlockAtPosition(positionAbove);
                bool addAboveAsGroundNode = cubeAbove == null;
                UsableEntry entryAbove = null;

                if (cubeAbove != null)
                {
                  var cellAbove = AiUtils.GetCellForPosition(cubeAbove, positionAbove);
                  var tupleAbove = MyTuple.Create(cubeAbove.BlockDefinition.Id, cellAbove);
                  blockDirInfo.TryGetValue(tupleAbove, out entryAbove);
                }

                if (!addAboveAsGroundNode && cubeAbove.Position != block.Position && entryAbove != null && (entryAbove.Mask & localDwnDir) == 0)
                {
                  addAboveAsGroundNode = true;
                  ExemptNodesUpper.Add(positionAbove);
                }

                if (addAboveAsGroundNode)
                {
                  mainGridPosition = positionAbove;
                  Vector3 offsetAbove;
                  bool allowSkip = false;

                  if (isLadder)
                  {
                    offsetAbove = entryAbove?.GetOffset(cubeAbove) ?? Vector3.Zero;
                  }
                  else if (entry.SpecialConsideration)
                  {
                    offsetAbove = Vector3.Zero;
                    allowSkip = true;
                  }
                  else
                  {
                    offsetAbove = entry.GetOffsetForCellAbove(block, mainGridPosition);
                  }

                  nType = NodeType.Ground;

                  if (allowSkip)
                    nType |= NodeType.Skip;

                  Node nAbove;
                  if (!OpenTileDict.TryGetValue(mainGridPosition, out nAbove))
                  {
                    Node newNode = AiSession.Instance.NodePool.Get();
                    newNode.Update(mainGridPosition, offsetAbove, this, nType, 0, MainGrid, cubeAbove);

                    AddTileToMap(mainGridPosition, newNode);
                  }
                  else
                  {
                    if (nAbove.Offset == Vector3.Zero)
                    {
                      nAbove.Offset = offsetAbove;
                    }

                    if (!nAbove.IsGroundNode || (allowSkip && !nAbove.CanSkip))
                    {
                      nAbove.SetNodeType(nType, this);
                    }
                  }
                }
              }
            }
          }
        }
      }
      else
      {
        foreach (var cell in cubeDef.IsCubePressurized.Keys)
        {
          UsableEntry usableEntry;
          var tuple = MyTuple.Create(cubeDef.Id, cell);
          if (blockDirInfo.TryGetValue(tuple, out usableEntry))
          {
            var gridCell = adjustedPosition + Vector3I.TransformNormal(cell, ref blockMatrix);
            mainGridPosition = gridCell;

            var offset = usableEntry.GetOffset(block);
            NodeType nType = usableEntry.IsGroundNode ? NodeType.Ground : NodeType.None;

            if (nType == NodeType.None && ShouldBeGroundNode(block, cell, ref upVec, ref upDir))
            {
              nType |= NodeType.Ground;
            }

            Node node;
            if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
            {
              Node newNode = AiSession.Instance.NodePool.Get();
              newNode.Update(mainGridPosition, offset, this, nType, 0, MainGrid, block);

              AddTileToMap(mainGridPosition, newNode);
            }
            else
            {
              if (node.Offset == Vector3D.Zero)
              {
                node.Offset = offset;
              }
              
              if (!node.IsGroundNode && nType == NodeType.Ground)
              { 
                node.SetNodeType(NodeType.Ground, this);
              }
            }
          }
        }
      }
    }

    bool ShouldBeGroundNode(IMySlimBlock block, Vector3I cell, ref Vector3I upVec, ref Direction upDir)
    {
      if (block != null)
      {
        var blockDef = block.BlockDefinition.Id;
        var subtype = blockDef.SubtypeName;

        var isGCMCatwalk = AiSession.Instance.GratedCatwalkExpansionBlocks.Contains(blockDef);

        if (isGCMCatwalk || AiSession.Instance.CatwalkBlockDefinitions.Contains(blockDef))
        {
          // catwalks should be ground nodes as long as they are oriented properly

          var multiplier = (isGCMCatwalk && subtype.EndsWith("Raised")) ? -1 : 1;
          var testVec = multiplier * Base6Directions.GetIntVector(block.Orientation.Up);

          if (upVec == testVec)
            return true;
        }
      }

      return false;
    }

    List<BoundingBoxI> _blockBoxList = new List<BoundingBoxI>();

    public void ProcessBlockChanges()
    {
      try
      {
        using (_pendingLockObject.AcquireExclusiveUsing())
        {
          if (GraphLocked || !Ready || !_updateTask.IsComplete)
            return;

          if (IsValid && _updateTask.Exceptions != null)
          {
            AiSession.Instance.Logger.ClearCached();
            AiSession.Instance.Logger.AddLine($"Exceptions found during update task!\n");
            foreach (var ex in _updateTask.Exceptions)
              AiSession.Instance.Logger.AddLine($" -> {ex}\n");

            AiSession.Instance.Logger.LogAll();
            MyAPIGateway.Utilities.ShowNotification($"Exception during task!");
          }

          InteriorNodesReady = false;
          NeedsBlockUpdate = false;
          GraphLocked = true;
          Ready = false;

          _blockApplyHash.Clear();
          _blockApplyHash.UnionWith(_blockUpdateHash);
          _updateTask = MyAPIGateway.Parallel.Start(ApplyBlockChanges, SetReady);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in CubeGridMapV2.ProcessBlockChanges: {ex}");
      }
    }

    void ApplyBlockChanges()
    {
      try
      {
        //AiSession.Instance.Logger.Log($"{this}.ApplyBlockChanges: Start");

        _blockBoxList.Clear();
        var dirArray = AiSession.Instance.CardinalDirections;

        foreach (var pos in _blockApplyHash)
        {
          var box = BoundingBoxI.CreateInvalid();
          box.Include(pos);

          foreach (var dir in dirArray)
          {
            box.Include(pos + dir);
          }

          box.Inflate(1);

          //var iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);
          //while (iter.IsValid())
          //{
          //  var current = iter.Current;
          //  iter.MoveNext();

          //  _positionsToRemove.Add(current);
          //}

          bool found = false;
          for (int i = 0; i < _blockBoxList.Count; i++)
          {
            var otherBox = _blockBoxList[i];

            if (otherBox.Intersects(box))
            {
              found = true;
              otherBox.Include(ref box);
              _blockBoxList[i] = otherBox;
              break;
            }
          }

          if (!found)
          {
            _blockBoxList.Add(box);
          }
        }

        InvokePositionsRemoved(true);

        var newBox = GetMapBoundingBoxLocal();
        newBox.Inflate(_boxExpansion);

        var worldCenter = MainGrid.GridIntegerToWorld(newBox.Center);
        var quat = Quaternion.CreateFromRotationMatrix(MainGrid.WorldMatrix);
        var halfVector = Vector3D.Half * CellSize;
        var newOBB = new MyOrientedBoundingBoxD(worldCenter, newBox.HalfExtents * CellSize + halfVector, quat);

        if (newOBB.HalfExtent.LengthSquared() > OBB.HalfExtent.LengthSquared())
        {
          Remake = true;
          Dirty = true;
          return;
        }

        WeaponPositions.Clear();
        foreach (var fatblock in MainGrid.GetFatBlocks())
        {
          if (fatblock is IMyUserControllableGun)
          {
            WeaponPositions.Add(fatblock.Position);
          }
        }

        var upDir = MainGrid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
        var upVec = Base6Directions.GetIntVector(upDir);
        Vector3I upVecPlanet;

        var cellSize = CellSize;
        var halfCellSize = CellSize * 0.5f;

        float _;
        var gravityNorm = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out _);

        if (gravityNorm.LengthSquared() > 0)
        {
          gravityNorm.Normalize();

          var upDirPlanet = MainGrid.WorldMatrix.GetClosestDirection(-gravityNorm);
          upVecPlanet = Base6Directions.GetIntVector(upDirPlanet);
        }
        else
        {
          gravityNorm = WorldMatrix.Down;
          upVecPlanet = upVec;
        }

        for (int i = 0; i < _blockBoxList.Count; i++)
        {
          var box = _blockBoxList[i];
          box.Min = Vector3I.Max(box.Min, BoundingBox.Min);
          box.Max = Vector3I.Min(box.Max, BoundingBox.Max);

          var iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);

          while (iter.IsValid())
          {
            var pos = iter.Current;
            iter.MoveNext();

            Node n;
            if (PlanetTileDictionary.TryGetValue(pos, out n))
            {
              PlanetTileDictionary.Remove(pos);
              AiSession.Instance.NodePool?.Return(ref n);
            }
            else if (OpenTileDict.TryGetValue(pos, out n))
            {
              OpenTileDict.Remove(pos);
              AiSession.Instance.NodePool?.Return(ref n);
            }
          }

          iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);

          while (iter.IsValid())
          {
            var pos = iter.Current;
            iter.MoveNext();

            var slim = GetBlockAtPosition(pos);
            if (slim != null)
            {
              var cubeDef = slim.BlockDefinition as MyCubeBlockDefinition;
              InitBlockV2(slim, ref upVec, ref upDir, ref cellSize, ref halfCellSize, cubeDef);
            }
          }

          CheckForPlanetTiles(ref box, ref gravityNorm, ref upVecPlanet);
        }

        for (int i = 0; i < _blockBoxList.Count; i++)
        {
          var box = _blockBoxList[i];
          box.Min = Vector3I.Max(box.Min, BoundingBox.Min);
          box.Max = Vector3I.Min(box.Max, BoundingBox.Max);

          var iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);

          while (iter.IsValid())
          {
            var pos = iter.Current;
            iter.MoveNext();

            Node n;
            if (OpenTileDict.TryGetValue(pos, out n))
            {
              GetBlockedNodeEdges(n);
            }
          }

          iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);

          while (iter.IsValid())
          {
            var pos = iter.Current;
            iter.MoveNext();

            Node n;
            if (PlanetTileDictionary.TryGetValue(pos, out n))
            {
              GetBlockedNodeEdges(n);
            }
          }

        }

        _blockApplyHash.Clear();

        NeedsInteriorNodesUpdate = true;
        //AiSession.Instance.Logger.Log($"{this}.ApplyBlockChanges: Finish");
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception during block update task: {ex}");
      }
    }

    public void InteriorNodesCheck()
    {
      if (_interiorNodesTask.IsComplete)
      {
        if (IsValid && _interiorNodesTask.Exceptions != null)
        {
          AiSession.Instance.Logger.ClearCached();
          AiSession.Instance.Logger.AddLine($"Exceptions found during update task!\n");
          foreach (var ex in _interiorNodesTask.Exceptions)
            AiSession.Instance.Logger.AddLine($" -> {ex}\n");

          AiSession.Instance.Logger.LogAll();
          MyAPIGateway.Utilities.ShowNotification($"Exception during task!");
        }

        NeedsInteriorNodesUpdate = false;

        ApiWorkData data = AiSession.Instance.ApiWorkDataPool.Get();

        data.Grid = MainGrid;
        data.NodeList = InteriorNodeList;
        data.EnclosureRating = 5;
        data.AirtightNodesOnly = false;
        data.AllowAirNodes = false;
        data.CallBack = InteriorNodeCallback;

        _interiorNodesTask = MyAPIGateway.Parallel.StartBackground(BotFactory.GetInteriorNodes, BotFactory.GetInteriorNodesCallback, data);
      }
    }

    void InteriorNodeCallback(IMyCubeGrid grid, List<Vector3I> nodeList)
    {
      //AiSession.Instance.Logger.Log($"InteriorNodeCallback");
      InteriorNodesReady = true;
    }

    BoundingBoxI GetMapBoundingBoxLocal()
    {
      var box = new BoundingBoxI(MainGrid.Min, MainGrid.Max);
      for (int i = GridCollection.Count - 1; i >= 0; i--)
      {
        var connectedGrid = GridCollection[i] as MyCubeGrid;
        if (connectedGrid?.Physics == null || connectedGrid.MarkedForClose)
        {
          GridCollection.RemoveAtFast(i);
          continue;
        }

        bool connectedIsMain = connectedGrid.EntityId == MainGrid.EntityId;

        Vector3I min, max;
        if (connectedIsMain)
        {
          min = connectedGrid.Min;
          max = connectedGrid.Max;
        }
        else
        {
          min = MainGrid.WorldToGridInteger(connectedGrid.GridIntegerToWorld(connectedGrid.Min));
          max = MainGrid.WorldToGridInteger(connectedGrid.GridIntegerToWorld(connectedGrid.Max));
        }

        box.Include(ref min);
        box.Include(ref max);
      }

      return box;
    }

    private void Door_OnDoorStateChanged(IMyDoor door, bool open)
    {
      Door_EnabledChanged(door);
    }

    private void Door_OnMarkForClose(IMyEntity ent)
    {
      try
      {
        if (ent != null)
        {
          ent.OnMarkForClose -= Door_OnMarkForClose;

          var door = ent as IMyDoor;
          if (door != null)
          {
            door.EnabledChanged -= Door_EnabledChanged;
            door.IsWorkingChanged -= Door_EnabledChanged;
            door.OnDoorStateChanged -= Door_OnDoorStateChanged;

            Door_EnabledChanged(door);
          }
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in CubeGridMapV2.Door_OnMarkForClose: {ex}");

        if (MyAPIGateway.Session?.Player != null)
          AiSession.Instance.ShowMessage($"Exception in CubeGridMapV2.Door_OnMarkForClose: {ex.Message}");
      }
    }

    internal void Door_EnabledChanged(IMyCubeBlock block)
    {
      try
      {
        if (!IsValid || AiSession.Instance == null || !AiSession.Instance.Registered)
          return;

        var door = block as IMyDoor;
        if (door == null)
          return;

        Vector3I pos = block.Position;
        var grid = block.CubeGrid as MyCubeGrid;
        var needsPositionAdjusted = MainGrid.EntityId != grid.EntityId;

        if (needsPositionAdjusted)
          pos = MainGrid.WorldToGridInteger(grid.GridIntegerToWorld(pos));

        bool isOpen = false;
        if (door.SlimBlock.IsBlockUnbuilt()
          || door.CustomName.IndexOf("airlock", StringComparison.OrdinalIgnoreCase) >= 0
          || door.CustomName.IndexOf("[AiE]", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          isOpen = true;
        }

        if (isOpen || door.MarkedForClose || door.Enabled || door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open)
        {
          IMyDoor _;
          if (BlockedDoors.TryRemove(pos, out _))
          {
            List<Vector3I> positionList = AiSession.Instance.LineListPool.Get();
            AiUtils.FindAllPositionsForBlock(door.SlimBlock, positionList);

            for (int i = 0; i < positionList.Count; i++)
            {
              var point = positionList[i];

              if (needsPositionAdjusted)
                point = MainGrid.WorldToGridInteger(grid.GridIntegerToWorld(point));

              BlockedDoors.TryRemove(point, out _);
            }

            AiSession.Instance.LineListPool?.Return(ref positionList);
          }
        }
        else if (!door.Enabled && !BlockedDoors.ContainsKey(pos))
        {
          List<Vector3I> positionList = AiSession.Instance.LineListPool.Get();
          AiUtils.FindAllPositionsForBlock(door.SlimBlock, positionList);

          for (int i = 0; i < positionList.Count; i++)
          {
            var point = positionList[i];

            if (needsPositionAdjusted)
              point = MainGrid.WorldToGridInteger(grid.GridIntegerToWorld(point));

            BlockedDoors[point] = door;
          }

          AiSession.Instance.LineListPool?.Return(ref positionList);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in CubeGridMapV2.Door_EnabledChanged: {ex}");

        if (MyAPIGateway.Session?.Player != null)
          AiSession.Instance.ShowMessage($"Exception in CubeGridMapV2.Door_EnabledChanged: {ex.Message}");
      }
    }

    void CheckForPlanetTiles(ref BoundingBoxI box, ref Vector3 gravityNorm, ref Vector3I upVec)
    {
      var blockedVoxelEdges = AiSession.Instance.BlockedVoxelEdges;
      var cellSize = CellSize;
      var cellSizeCutoff = cellSize * 0.65f;
      cellSizeCutoff *= cellSizeCutoff;
      var worldMatrix = WorldMatrix;
      var planet = RootVoxel as MyPlanet;

      bool checkForWater = false;
      bool checkForVoxel = false;
      if (RootVoxel != null && !RootVoxel.MarkedForClose)
      {
        checkForWater = planet != null && WaterAPI.Registered && WaterAPI.HasWater(planet);
        checkForVoxel = true;

        RootVoxel.RangeChanged -= Planet_RangeChanged;
        RootVoxel.OnMarkForClose -= Planet_OnMarkForClose;

        RootVoxel.RangeChanged += Planet_RangeChanged;
        RootVoxel.OnMarkForClose += Planet_OnMarkForClose;
      }

      double edgeDistance;
      if (!GetEdgeDistanceInDirection(WorldMatrix.Up, out edgeDistance))
        edgeDistance = VoxelGridMap.DefaultHalfSize * cellSize;

      var topOfGraph = OBB.Center + WorldMatrix.Up * edgeDistance;
      var iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);
      int tunnelCount = 0;

      while (iter.IsValid())
      {
        if (Dirty || Remake)
        {
          return;
        }

        var localPoint = iter.Current;
        iter.MoveNext();

        Node node;
        if (OpenTileDict.TryGetValue(localPoint, out node))
          continue;

        bool isGroundNode = false;
        bool isTunnelNode = false;
        bool addNodeBelow = false;

        Vector3D worldPoint = MainGrid.GridIntegerToWorld(localPoint);
        var groundPoint = worldPoint;
        var groundPointBelow = worldPoint;

        var worldBelow = worldPoint + gravityNorm;
        var localBelow = localPoint - upVec;

        if (checkForVoxel)
        {
          if (RootVoxel == null || RootVoxel.MarkedForClose)
            checkForVoxel = false;

          if (PointInsideVoxel(worldPoint, RootVoxel))
            continue;

          var pointBelow = worldPoint + gravityNorm * cellSize;
          if (PointInsideVoxel(pointBelow, RootVoxel))
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

            var offsetPosition = pointInAir - gravityNorm;

            if (Vector3D.DistanceSquared(pointInAir, worldPoint) > cellSizeCutoff)
            {
              isGroundNode = false;
              addNodeBelow = true;
              groundPointBelow = offsetPosition;
            }
            else
            {
              groundPoint = offsetPosition;
            }
          }

          Vector3D? surfacePoint = null;
          if (planet != null)
          {
            surfacePoint = planet.GetClosestSurfacePointGlobal(worldPoint) - gravityNorm;
          }
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

            var vector = worldPoint - surfaceValue;
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

        if (addNodeBelow && PlanetTileDictionary.TryGetValue(localBelow, out node))
        {
          NodeType nType = NodeType.None;

          if (!node.IsGroundNode)
            nType |= NodeType.Ground;

          if (!node.IsTunnelNode && isTunnelNode)
            nType |= NodeType.Tunnel;

          node.SetNodeType(nType, this);
          addNodeBelow = false;
        }

        if (PlanetTileDictionary.TryGetValue(localPoint, out node))
        {
          NodeType nType = NodeType.None;

          if (isGroundNode && !node.IsGroundNode)
            nType |= NodeType.Ground;

          if (isTunnelNode && !node.IsTunnelNode)
            nType |= NodeType.Tunnel;

          node.SetNodeType(nType, this);
          continue;
        }

        bool add = true;
        for (int i = _additionalMaps2.Count - 1; i >= 0; i--)
        {
          if (Dirty || Remake)
          {
            return;
          }

          var otherGrid = _additionalMaps2[i]?.MainGrid;
          if (otherGrid == null || otherGrid.MarkedForClose)
            continue;

          if (addNodeBelow)
          {
            var localBelowOther = otherGrid.WorldToGridInteger(worldBelow);
            var slimBelow = otherGrid.GetCubeBlock(localBelowOther) as IMySlimBlock;
            if (slimBelow != null && ((MyCubeBlockDefinition)slimBelow.BlockDefinition).HasPhysics)
            {
              if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(slimBelow.BlockDefinition.Id))
              {
                var turretBasePosition = slimBelow.Position - Base6Directions.GetIntVector(slimBelow.Orientation.Up);
                if (turretBasePosition == localBelowOther || slimBelow.Position == localBelowOther)
                  addNodeBelow = false;
              }
              else if (slimBelow.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_WindTurbine))
              {
                var turbineBasePosition = slimBelow.Position;
                var turbineUpVec = Base6Directions.GetIntVector(slimBelow.Orientation.Up);
                var midPoint = turbineBasePosition + turbineUpVec;
                var topPoint = midPoint + turbineUpVec;
                if (localBelowOther == turbineBasePosition || localBelowOther == midPoint || localBelowOther == topPoint)
                {
                  addNodeBelow = false;
                }
              }
              else if (slimBelow.BlockDefinition.Id.SubtypeName.StartsWith("LargeBlockLargeFlatAtmosphericThrust"))
              {
                var thrustBasePosition = slimBelow.Position;
                var thrustForwardVec = Base6Directions.GetIntVector(slimBelow.Orientation.Forward);
                var thrustUpVec = Base6Directions.GetIntVector(slimBelow.Orientation.Up);

                var flamePosition = thrustBasePosition + thrustForwardVec;
                var adjustedPosition = localBelowOther - thrustForwardVec;

                if (localBelowOther == flamePosition || slimBelow.CubeGrid.GetCubeBlock(adjustedPosition)?.Position != slimBelow.Position)
                {
                  addNodeBelow = false;
                }
              }
              else if (slimBelow.BlockDefinition.Id.SubtypeName.EndsWith("Slope2Tip"))
              {
                if (Base6Directions.GetIntVector(slimBelow.Orientation.Up).Dot(ref upVec) != 0)
                {
                  addNodeBelow = false;
                }
              }
              else if (!AiSession.Instance.FlatWindowDefinitions.ContainsItem(slimBelow.BlockDefinition.Id))
                addNodeBelow = false;
            }
          }

          var localPointOther = otherGrid.WorldToGridInteger(worldPoint);
          var slim = otherGrid.GetCubeBlock(localPointOther) as IMySlimBlock;
          if (slim != null && ((MyCubeBlockDefinition)slim.BlockDefinition).HasPhysics && !(slim.FatBlock is IMyLightingBlock))
          {
            if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(slim.BlockDefinition.Id))
            {
              var turretBasePosition = slim.Position - Base6Directions.GetIntVector(slim.Orientation.Up);
              if (turretBasePosition == localPointOther || slim.Position == localPointOther)
              {
                add = false;
                break;
              }
            }
            else if (slim.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_WindTurbine))
            {
              var turbineBasePosition = slim.Position;
              var turbineUpVec = Base6Directions.GetIntVector(slim.Orientation.Up);
              var midPoint = turbineBasePosition + turbineUpVec;
              var topPoint = midPoint + turbineUpVec;
              if (localPointOther == turbineBasePosition || localPointOther == midPoint || localPointOther == topPoint)
              {
                add = false;
                break;
              }
            }
            else if (slim.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_SolarPanel))
            {
              if (slim.BlockDefinition.Id.SubtypeName.IndexOf("center", StringComparison.OrdinalIgnoreCase) >= 0
                || slim.BlockDefinition.Id.SubtypeName.IndexOf("colorable", StringComparison.OrdinalIgnoreCase) >= 0)
              {
                add = false;
                break;
              }
            }
            else if (AiSession.Instance.ArmorPanelAllDefinitions.Contains(slim.BlockDefinition.Id))
            {
              if (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(slim.BlockDefinition.Id)
                || slim.BlockDefinition.Id.SubtypeName.Contains("Centered"))
              {
                add = false;
                break;
              }
            }
            else if (slim.BlockDefinition.Id.SubtypeName.StartsWith("LargeBlockLargeFlatAtmosphericThrust"))
            {
              var thrustBasePosition = slim.Position;
              var thrustForwardVec = Base6Directions.GetIntVector(slim.Orientation.Forward);
              var thrustUpVec = Base6Directions.GetIntVector(slim.Orientation.Up);

              var flamePosition = thrustBasePosition + thrustForwardVec;
              var adjustedPosition = localPointOther - thrustForwardVec;

              if (localPointOther == flamePosition || slim.CubeGrid.GetCubeBlock(adjustedPosition)?.Position != slim.Position)
              {
                add = false;
              }
            }
            else if (slim.BlockDefinition.Id.SubtypeName.EndsWith("Slope2Tip"))
            {
              if (Base6Directions.GetIntVector(slim.Orientation.Up).Dot(ref upVec) != 0)
              {
                add = false;
                break;
              }
            }
            else if (!AiSession.Instance.FlatWindowDefinitions.ContainsItem(slim.BlockDefinition.Id)
              && !AiSession.Instance.CatwalkBlockDefinitions.Contains(slim.BlockDefinition.Id))
            {
              add = false;
              break;
            }
          }
        }

        if (add)
        {
          if (addNodeBelow)
          {
            var blockBelow = GetBlockAtPosition(localBelow);
            if (blockBelow != null && blockBelow.CubeGrid.EntityId == MainGrid.EntityId && !(blockBelow.FatBlock is IMyLightingBlock))
            {
              if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(blockBelow.BlockDefinition.Id))
              {
                var turretBasePosition = blockBelow.Position - Base6Directions.GetIntVector(blockBelow.Orientation.Up);
                if (turretBasePosition == localBelow || blockBelow.Position == localBelow)
                  addNodeBelow = false;
              }
              else if (blockBelow.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_WindTurbine))
              {
                var turbineBasePosition = blockBelow.Position;
                var turbineUpVec = Base6Directions.GetIntVector(blockBelow.Orientation.Up);
                var midPoint = turbineBasePosition + turbineUpVec;
                var topPoint = midPoint + turbineUpVec;
                if (localBelow == turbineBasePosition || localBelow == midPoint || localBelow == topPoint)
                {
                  addNodeBelow = false;
                }
              }
              else if (blockBelow.BlockDefinition.Id.SubtypeName.StartsWith("LargeBlockLargeFlatAtmosphericThrust"))
              {
                var thrustBasePosition = blockBelow.Position;
                var thrustForwardVec = Base6Directions.GetIntVector(blockBelow.Orientation.Forward);
                var thrustUpVec = Base6Directions.GetIntVector(blockBelow.Orientation.Up);

                var flamePosition = thrustBasePosition + thrustForwardVec;
                var adjustedPosition = localBelow - thrustForwardVec;

                if (localBelow == flamePosition || blockBelow.CubeGrid.GetCubeBlock(adjustedPosition)?.Position != blockBelow.Position)
                {
                  addNodeBelow = false;
                }
              }
              else if (!AiSession.Instance.FlatWindowDefinitions.ContainsItem(blockBelow.BlockDefinition.Id))
                addNodeBelow = false;
            }
          }

          if (Dirty || Remake)
          {
            return;
          }

          var block = GetBlockAtPosition(localPoint);
          bool blockValid = block != null && ((MyCubeBlockDefinition)block.BlockDefinition).HasPhysics;
          if (blockValid && block.CubeGrid.EntityId == MainGrid.EntityId && !(block.FatBlock is IMyLightingBlock))
          {
            if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(block.BlockDefinition.Id))
            {
              var turretBasePosition = block.Position - Base6Directions.GetIntVector(block.Orientation.Up);
              if (turretBasePosition == localPoint || localPoint == block.Position)
                add = false;
            }
            else if (block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_WindTurbine))
            {
              var turbineBasePosition = block.Position;
              var turbineUpVec = Base6Directions.GetIntVector(block.Orientation.Up);
              var midPoint = turbineBasePosition + turbineUpVec;
              var topPoint = midPoint + turbineUpVec;
              if (localPoint == turbineBasePosition || localPoint == midPoint || localPoint == topPoint)
              {
                add = false;
              }
            }
            else if (block.BlockDefinition.Id.SubtypeName.StartsWith("LargeBlockLargeFlatAtmosphericThrust"))
            {
              var thrustBasePosition = block.Position;
              var thrustForwardVec = Base6Directions.GetIntVector(block.Orientation.Forward);
              var thrustUpVec = Base6Directions.GetIntVector(block.Orientation.Up);


              var flamePosition = thrustBasePosition + thrustForwardVec;
              var adjustedPosition = localPoint - thrustForwardVec;

              if (localPoint == flamePosition || block.CubeGrid.GetCubeBlock(adjustedPosition)?.Position != block.Position)
              {
                add = false;
              }
            }
            else if (block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_SolarPanel))
            {
              if (block.BlockDefinition.Id.SubtypeName.IndexOf("center", StringComparison.OrdinalIgnoreCase) >= 0
                || block.BlockDefinition.Id.SubtypeName.IndexOf("colorable", StringComparison.OrdinalIgnoreCase) >= 0)
              {
                add = false;
              }
            }
            else if (AiSession.Instance.ArmorPanelAllDefinitions.Contains(block.BlockDefinition.Id))
            {
              if (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(block.BlockDefinition.Id)
                || block.BlockDefinition.Id.SubtypeName.Contains("Centered"))
              {
                add = false;
              }
            }
            else if (block.BlockDefinition.Id.SubtypeName.EndsWith("Slope2Tip"))
            {
              if (Base6Directions.GetIntVector(block.Orientation.Up).Dot(ref upVec) != 0)
              {
                add = false;
              }
            }
            else if (!AiSession.Instance.FlatWindowDefinitions.ContainsItem(block.BlockDefinition.Id)
              && !AiSession.Instance.CatwalkBlockDefinitions.Contains(block.BlockDefinition.Id))
            {
              add = false;
            }
          }

          if (add && isGroundNode)
          {
            var localAbove = localPoint + upVec;
            var slim = GetBlockAtPosition(localAbove);
            var slimValid = slim != null && ((MyCubeBlockDefinition)slim.BlockDefinition).HasPhysics;
            if (slimValid && slim.CubeGrid.EntityId == MainGrid.EntityId && !(slim.FatBlock is IMyLightingBlock))
            {
              if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(slim.BlockDefinition.Id))
              {
                var turretBasePosition = slim.Position - Base6Directions.GetIntVector(slim.Orientation.Up);
                if (turretBasePosition == localAbove || localAbove == slim.Position)
                  add = false;
              }
              else if (slim.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_WindTurbine))
              {
                var turbineBasePosition = slim.Position;
                var turbineUpVec = Base6Directions.GetIntVector(slim.Orientation.Up);
                var midPoint = turbineBasePosition + turbineUpVec;
                var topPoint = midPoint + turbineUpVec;
                if (localAbove == turbineBasePosition || localAbove == midPoint || localAbove == topPoint)
                {
                  add = false;
                }
              }
              else if (AiSession.Instance.ArmorPanelAllDefinitions.Contains(slim.BlockDefinition.Id))
              {
                if (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(slim.BlockDefinition.Id)
                  || slim.BlockDefinition.Id.SubtypeName.Contains("Centered"))
                {
                  add = false;
                }
              }
              else if (slim.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_SolarPanel))
              {
                if (slim.BlockDefinition.Id.SubtypeName.IndexOf("center", StringComparison.OrdinalIgnoreCase) >= 0
                  || slim.BlockDefinition.Id.SubtypeName.IndexOf("colorable", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                  add = false;
                }
              }
              else if (slim.BlockDefinition.Id.SubtypeName.StartsWith("LargeBlockLargeFlatAtmosphericThrust"))
              {
                var thrustBasePosition = slim.Position;
                var thrustForwardVec = Base6Directions.GetIntVector(slim.Orientation.Forward);
                var thrustUpVec = Base6Directions.GetIntVector(slim.Orientation.Up);
                var flamePosition = thrustBasePosition + thrustForwardVec + thrustUpVec;

                var adjustedPosition = localAbove - thrustForwardVec;
                if (localAbove == flamePosition || slim.CubeGrid.GetCubeBlock(adjustedPosition)?.Position != slim.Position)
                {
                  add = false;
                }
              }
              else if (slim.BlockDefinition.Id.SubtypeName.EndsWith("Slope2Tip"))
              {
                if (Base6Directions.GetIntVector(slim.Orientation.Up).Dot(ref upVec) != 0)
                {
                  add = false;
                }
              }
              else if ((!AiSession.Instance.FlatWindowDefinitions.ContainsItem(slim.BlockDefinition.Id)
                && !AiSession.Instance.CatwalkBlockDefinitions.Contains(slim.BlockDefinition.Id)) || Base6Directions.GetIntVector(slim.Orientation.Up) != -upVec)
              {
                var actualSurface = groundPoint + gravityNorm;
                var direction = Base6Directions.GetDirection(-upVec);
                var downVec = slim.CubeGrid.WorldMatrix.GetDirectionVector(direction);
                var bottomEdge = slim.CubeGrid.GridIntegerToWorld(localAbove) + downVec * slim.CubeGrid.GridSize * 0.5;
                if (Vector3D.DistanceSquared(actualSurface, bottomEdge) < 7)
                {
                  add = false;
                }
              }
            }
          }

          if (Dirty || Remake)
          {
            return;
          }

          if (add)
          {
            var isInWater = checkForWater && WaterAPI.IsUnderwater(groundPoint);

            NodeType nType = NodeType.GridPlanet;
            if (isGroundNode)
              nType |= NodeType.Ground;
            if (isInWater)
              nType |= NodeType.Water;
            if (isTunnelNode)
              nType |= NodeType.Tunnel;

            var offset = (Vector3)(groundPoint - LocalToWorld(localPoint));
            block = GetBlockAtPosition(localPoint);

            Node newNode = AiSession.Instance.NodePool.Get();
            newNode.Update(localPoint, offset, this, nType, 0, MainGrid, block);

            if (checkForVoxel)
            {
              foreach (var dir in blockedVoxelEdges)
              {
                newNode.SetBlocked(dir);
              }
            }

            PlanetTileDictionary[localPoint] = newNode;

            if (addNodeBelow)
            {
              nType |= NodeType.Ground;
              offset = (Vector3)(groundPointBelow - LocalToWorld(localBelow));

              Node nodeBelow = AiSession.Instance.NodePool.Get();
              nodeBelow.Update(localBelow, offset, this, nType, newNode.BlockedMask, MainGrid);
              PlanetTileDictionary[localBelow] = nodeBelow;
            }
          }
        }
      }

      GraphHasTunnel = tunnelCount > 25;
    }

    private void Planet_OnMarkForClose(MyEntity obj)
    {
      if (RootVoxel != null)
      {
        RootVoxel.OnMarkForClose -= Planet_OnMarkForClose;
        RootVoxel.RangeChanged -= Planet_RangeChanged;
      }
    }

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
            VoxelUpdateItem updateItem = AiSession.Instance.VoxelUpdateItemPool.Get();
            updateItem.Init(ref min, ref max);
            _voxelUpdatesNeeded.Add(updateItem);
          }

          NeedsVoxelUpdate = true;
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMapV2.Planet_RangeChanged: {ex}");
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

        if (!Ready || GraphLocked)
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
            if (IsValid && _updateTask.Exceptions != null)
            {
              AiSession.Instance.Logger.ClearCached();
              AiSession.Instance.Logger.AddLine($"Exceptions found during update task!\n");
              foreach (var ex in _updateTask.Exceptions)
                AiSession.Instance.Logger.AddLine($" -> {ex}\n");

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
        AiSession.Instance.Logger.Log($"Exception in CubeGridMapV2.UpdateVoxels: {ex}");
      }
    }

    void ApplyVoxelChanges()
    {
      try
      {
        //AiSession.Instance.Logger.Log($"{this}.ApplyVoxelChanges: Start");

        //_positionsToRemove.Clear();
        while (_voxelUpdatesQueue.Count > 0)
        {
          var updateItem = _voxelUpdatesQueue.Dequeue();
          var updateBox = updateItem.BoundingBox;

          var minWorld = Vector3D.Transform((Vector3)updateBox.Min - RootVoxel.SizeInMetresHalf, RootVoxel.WorldMatrix);
          var maxWorld = Vector3D.Transform((Vector3)updateBox.Max - RootVoxel.SizeInMetresHalf, RootVoxel.WorldMatrix);

          if (!OBB.Contains(ref minWorld) && !OBB.Contains(ref maxWorld))
          {
            AiSession.Instance.VoxelUpdateItemPool?.Return(ref updateItem);
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
            if (Dirty || Remake || RootVoxel == null || RootVoxel.MarkedForClose)
            {
              AiSession.Instance.VoxelUpdateItemPool?.Return(ref updateItem);
              return;
            }

            var current = iter.Current;
            iter.MoveNext();

            Node node;
            if (PlanetTileDictionary.TryGetValue(current, out node))
            {
              if (node != null)
              {
                AiSession.Instance.NodePool?.Return(ref node);
              }

              PlanetTileDictionary.Remove(current);
            }

            //_positionsToRemove.Add(current);
          }

          InvokePositionsRemoved(true);

          float _;
          var gravityNorm = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out _);
          gravityNorm.Normalize();

          var upDir = MainGrid.WorldMatrix.GetClosestDirection(-gravityNorm);
          var upVec = Base6Directions.GetIntVector(upDir);
          var box = new BoundingBoxI(mapMin, mapMax);

          CheckForPlanetTiles(ref box, ref gravityNorm, ref upVec);

          iter = new Vector3I_RangeIterator(ref mapMin, ref mapMax);

          while (iter.IsValid())
          {
            if (Dirty || Remake || RootVoxel == null || RootVoxel.MarkedForClose)
            {
              AiSession.Instance.VoxelUpdateItemPool?.Return(ref updateItem);
              return;
            }

            var current = iter.Current;
            iter.MoveNext();

            Node node;
            if (PlanetTileDictionary.TryGetValue(current, out node))
            {
              GetBlockedNodeEdges(node);
            }
          }

          AiSession.Instance.VoxelUpdateItemPool?.Return(ref updateItem);
        }

        //AiSession.Instance.Logger.Log($"{this}.ApplyVoxelChanges: Finished");
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in ApplyVoxelChanges: {ex}");
      }
    }

    void CheckFaces(IMySlimBlock block, Vector3I normal, MyCubeBlockDefinition cubeDef = null)
    {
      if (cubeDef == null)
        cubeDef = block.BlockDefinition as MyCubeBlockDefinition;

      bool isPassageStair = cubeDef.Context.ModName == "PassageIntersections" && cubeDef.Id.SubtypeName.EndsWith("PassageStairs_Large");
      if (isPassageStair && normal != Base6Directions.GetIntVector(block.Orientation.Up))
        return;

      var isGCMCatwalk = AiSession.Instance.GratedCatwalkExpansionBlocks.Contains(cubeDef.Id);

      if (isGCMCatwalk)
      {
        var multiplier = (cubeDef.Id.SubtypeName.EndsWith("Raised") ? 1 : -1);
        var testVec = multiplier * Base6Directions.GetIntVector(block.Orientation.Up);

        if (normal != testVec)
          return;
      }

      bool allowSolar = false;
      bool airTight = cubeDef.IsAirTight ?? false;

      if (!airTight && block.FatBlock is IMySolarPanel)
      {
        bool isColorable = block.BlockDefinition.Id.SubtypeName.IndexOf("colorable", StringComparison.OrdinalIgnoreCase) >= 0;
        Vector3I vecFwd = Base6Directions.GetIntVector(block.Orientation.Forward);

        if (isColorable)
        {
          allowSolar = vecFwd.Dot(ref normal) < 0;
        }
        else
        {
          allowSolar = vecFwd.Dot(ref normal) > 0;
        }
      }

      bool isFlatWindow = !allowSolar && AiSession.Instance.FlatWindowDefinitions.ContainsItem(cubeDef.Id);
      bool isCylinder = !isFlatWindow && AiSession.Instance.PipeBlockDefinitions.ContainsItem(cubeDef.Id);
      bool isAllowedConveyor = !isCylinder && AiSession.Instance.ConveyorFullBlockDefinitions.ContainsItem(cubeDef.Id);
      bool isAutomatonsFull = !isAllowedConveyor && AiSession.Instance.AutomatonsFullBlockDefinitions.ContainsItem(cubeDef.Id);
      bool isSlopeBlock = !isAutomatonsFull && AiSession.Instance.SlopeBlockDefinitions.Contains(cubeDef.Id)
        && !AiSession.Instance.SlopedHalfBlockDefinitions.Contains(cubeDef.Id)
        && !AiSession.Instance.HalfStairBlockDefinitions.Contains(cubeDef.Id);

      bool isScaffold = !isSlopeBlock && AiSession.Instance.ScaffoldBlockDefinitions.Contains(cubeDef.Id);
      bool exclude = isScaffold && (cubeDef.Id.SubtypeName.EndsWith("Open") || cubeDef.Id.SubtypeName.EndsWith("Structure"));
      bool isPlatform = isScaffold && !exclude && cubeDef.Id.SubtypeName.EndsWith("Unsupported");
      bool isCatwalk = isGCMCatwalk || (!isPlatform && AiSession.Instance.CatwalkBlockDefinitions.Contains(cubeDef.Id));

      var grid = block.CubeGrid as MyCubeGrid;
      bool needsPositionAdjusted = grid.EntityId != MainGrid.EntityId;

      List<Vector3I> positionList = AiSession.Instance.LineListPool.Get();
      AiUtils.FindAllPositionsForBlock(block, positionList);

      foreach (var cell in positionList)
      {
        var position = cell;

        if (isPassageStair)
        {
          var cellKey = AiUtils.GetCellForPosition(block, position);
          if (cellKey != new Vector3I(1, 0, 0))
          {
            if (needsPositionAdjusted)
              position = MainGrid.WorldToGridInteger(grid.GridIntegerToWorld(position));

            Vector3 offset;
            if (cellKey == new Vector3I(0, 0, 0))
            {
              offset = Vector3.Zero;
            }
            else if (cellKey == new Vector3I(0, 1, 0))
            {
              offset = -(Vector3)WorldMatrix.GetDirectionVector(block.Orientation.Up) * CellSize * 0.75f + -(Vector3)WorldMatrix.GetDirectionVector(block.Orientation.Left) * CellSize * 0.5f;
            }
            else
            {
              offset = -(Vector3)WorldMatrix.GetDirectionVector(block.Orientation.Up) * CellSize * 0.25f + -(Vector3)WorldMatrix.GetDirectionVector(block.Orientation.Left) * CellSize * 0.25f;
            }

            var node = AiSession.Instance.NodePool.Get();
            node.Update(position, offset, this, NodeType.Ground, 0, grid, block);
            AddTileToMap(position, node);
          }

          continue;
        }

        var positionAbove = position + normal;
        var mainGridPosition = needsPositionAdjusted ? MainGrid.WorldToGridInteger(grid.GridIntegerToWorld(positionAbove)) : positionAbove;
        var cubeAbove = GetBlockAtPosition(positionAbove);
        var cubeAboveDef = cubeAbove?.BlockDefinition as MyCubeBlockDefinition;
        bool cubeAboveEmpty = cubeAbove == null || !cubeAboveDef.HasPhysics || cubeAboveDef.Id.SubtypeName.StartsWith("LargeWarningSign");
        bool cubeAboveIsThis = cubeAbove != null && cubeAbove.Position == block.Position && cubeAbove.CubeGrid.EntityId == block.CubeGrid.EntityId;
        bool aboveisScaffold = cubeAboveDef != null && AiSession.Instance.ScaffoldBlockDefinitions.Contains(cubeAboveDef.Id);
        bool aboveIsPassageStair = cubeAbove != null && cubeAbove.BlockDefinition.Id.SubtypeName.EndsWith("PassageStairs_Large");
        bool aboveIsConveyorCap = cubeAbove != null && AiSession.Instance.ConveyorEndCapDefinitions.ContainsItem(cubeAbove.BlockDefinition.Id);
        bool aboveisAutomatonsFlat = cubeAbove != null && AiSession.Instance.AutomatonsFlatBlockDefinitions.ContainsItem(cubeAbove.BlockDefinition.Id);
        bool isPressurized = AiUtils.IsSidePressurizedForBlock(block, position, normal);
        bool checkAbove = !exclude && (airTight || allowSolar || isCylinder || aboveisScaffold || isAllowedConveyor || isCatwalk || isPressurized);

        if (cubeAboveEmpty)
        {
          if (checkAbove)
          {
            if (allowSolar && block.BlockDefinition.Id.SubtypeName.IndexOf("colorablesolarpanelcorner", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              var cellKey = AiUtils.GetCellForPosition(block, position);
              var xVal = block.BlockDefinition.Id.SubtypeName.EndsWith("Inverted") ? 1 : 0;

              if (cellKey == new Vector3I(xVal, 0, 0) || cellKey == new Vector3I(xVal, 1, 0))
              {
                var node = AiSession.Instance.NodePool.Get();
                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid);
                AddTileToMap(mainGridPosition, node);
              }
            }
            else
            {
              var node = AiSession.Instance.NodePool.Get();
              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid);
              AddTileToMap(mainGridPosition, node);
            }
          }
          else if (isAutomatonsFull)
          {
            var subtype = cubeDef.Id.SubtypeName;
            if (subtype.EndsWith("WallB"))
            {
              if (Base6Directions.GetIntVector(block.Orientation.Left).Dot(ref normal) != 0)
              {
                var node = AiSession.Instance.NodePool.Get();
                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid);
                AddTileToMap(mainGridPosition, node);
              }
            }
            else if (subtype == "AirDuct2")
            {
              if (Base6Directions.GetIntVector(block.Orientation.Up).Dot(ref normal) > 0)
              {
                var node = AiSession.Instance.NodePool.Get();
                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid);
                AddTileToMap(mainGridPosition, node);
              }
            }
            else
            {
              var node = AiSession.Instance.NodePool.Get();
              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid);
              AddTileToMap(mainGridPosition, node);
            }
          }
          else if (isPlatform)
          {
            if (Base6Directions.GetIntVector(block.Orientation.Up).Dot(ref normal) > 0)
            {
              var node = AiSession.Instance.NodePool.Get();
              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid);
              AddTileToMap(mainGridPosition, node);
            }
          }
          else if (isScaffold)
          {
            // TODO ??
            //AiSession.Instance.Logger.Log($"Scaffold block found: {cubeDef.Id.SubtypeName}, HasPhysics = {cubeDef.HasPhysics}");
          }
          else if (isFlatWindow)
          {
            if (block.BlockDefinition.Id.SubtypeName == "LargeWindowSquare")
            {
              if (Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) > 0)
              {
                var node = AiSession.Instance.NodePool.Get();
                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid);
                AddTileToMap(mainGridPosition, node);
              }
            }
            else if (Base6Directions.GetIntVector(block.Orientation.Left).Dot(ref normal) < 0)
            {
              var node = AiSession.Instance.NodePool.Get();
              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid);
              AddTileToMap(mainGridPosition, node);
            }
            else if (block.BlockDefinition.Id.SubtypeName.StartsWith("HalfWindowCorner")
              && Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) > 0)
            {
              var node = AiSession.Instance.NodePool.Get();
              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid);
              AddTileToMap(mainGridPosition, node);
            }
          }
          else if (isSlopeBlock)
          {
            var leftVec = Base6Directions.GetIntVector(block.Orientation.Left);
            if (leftVec.Dot(ref normal) == 0)
              return;

            var node = AiSession.Instance.NodePool.Get();
            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid);
            var up = Base6Directions.GetIntVector(block.Orientation.Up);
            var bwd = -Base6Directions.GetIntVector(block.Orientation.Forward);
            node.SetBlocked(up);
            node.SetBlocked(bwd);

            AddTileToMap(mainGridPosition, node);
          }
        }
        else if (!aboveIsPassageStair)
        {
          bool blockPositionIsValid = BlockPositionHasValidTile(cubeAbove, positionAbove);

          if (checkAbove || aboveisScaffold || aboveIsConveyorCap || aboveisAutomatonsFlat || blockPositionIsValid)
          {
            if (aboveisScaffold)
            {
              Node node;
              if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
              {
                node = AiSession.Instance.NodePool.Get();
              }

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              AddTileToMap(mainGridPosition, node);
            }
            else if (cubeAboveDef.Id.SubtypeName.StartsWith("LargeBlockLargeFlatAtmosphericThrust"))
            {
              var thrustForwardVec = Base6Directions.GetIntVector(cubeAbove.Orientation.Forward);
              var subtractedPosition = mainGridPosition - thrustForwardVec;

              if (thrustForwardVec.Dot(ref normal) == 0 && cubeAbove.CubeGrid.GetCubeBlock(subtractedPosition)?.Position == cubeAbove.Position)
              {
                Node node;
                if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                {
                  node = AiSession.Instance.NodePool.Get();
                }

                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                AddTileToMap(mainGridPosition, node);
              }
            }
            else if (aboveisAutomatonsFlat)
            {
              Node node;
              if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
              {
                node = AiSession.Instance.NodePool.Get();
              }

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              AddTileToMap(mainGridPosition, node);
            }
            else if (aboveIsConveyorCap)
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) >= 0)
              {
                Node node;
                if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                {
                  node = AiSession.Instance.NodePool.Get();
                }

                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                AddTileToMap(mainGridPosition, node);
              }
            }
            else if (cubeAbove.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_WindTurbine))
            {
              var blockPos = cubeAbove.Position;
              if (needsPositionAdjusted)
                blockPos = MainGrid.WorldToGridInteger(cubeAbove.CubeGrid.GridIntegerToWorld(blockPos));

              if (blockPos != mainGridPosition)
              {
                Node node;
                if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                {
                  node = AiSession.Instance.NodePool.Get();
                }

                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                AddTileToMap(mainGridPosition, node);
              }
            }
            else if (cubeAbove.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_MedicalRoom))
            {
              var subtype = cubeAbove.BlockDefinition.Id.SubtypeName;
              if (subtype == "LargeMedicalRoom" || subtype == "LargeMedicalRoomReskin")
              {
                var relPosition = positionAbove - cubeAbove.Position;
                if (relPosition.RectangularLength() > 1)
                {
                  Node node;
                  if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                  {
                    node = AiSession.Instance.NodePool.Get();
                  }

                  node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                  AddTileToMap(mainGridPosition, node);
                }
              }
            }
            else if (cubeAbove.BlockDefinition.Id.SubtypeName.IndexOf("NeonTubes", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              Node node;
              if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
              {
                node = AiSession.Instance.NodePool.Get();
              }

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              AddTileToMap(mainGridPosition, node);
            }
            else if (cubeAbove.FatBlock is IMyInteriorLight && cubeAbove.BlockDefinition.Id.SubtypeName == "LargeLightPanel")
            {
              Node node;
              if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
              {
                node = AiSession.Instance.NodePool.Get();
              }

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              AddTileToMap(mainGridPosition, node);
            }
            else if (cubeAbove.BlockDefinition.Id.SubtypeName.StartsWith("DeadBody"))
            {
              Node node;
              if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
              {
                node = AiSession.Instance.NodePool.Get();
              }

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              AddTileToMap(mainGridPosition, node);
            }
            else if (cubeAbove.BlockDefinition.Id.SubtypeName == "RoboFactory")
            {
              var cubeAbovePosition = cubeAbove.Position;
              if (needsPositionAdjusted)
                cubeAbovePosition = MainGrid.WorldToGridInteger(cubeAbove.CubeGrid.GridIntegerToWorld(cubeAbovePosition));

              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) > 0 && positionAbove != cubeAbovePosition)
              {
                Node node;
                if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                {
                  node = AiSession.Instance.NodePool.Get();
                }

                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                AddTileToMap(mainGridPosition, node);
              }
            }
            else if (AiSession.Instance.DecorativeBlockDefinitions.ContainsItem(cubeAbove.BlockDefinition.Id))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) > 0)
              {
                Node node;
                if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                {
                  node = AiSession.Instance.NodePool.Get();
                }

                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                AddTileToMap(mainGridPosition, node);
              }
            }
            else if (AiSession.Instance.RailingBlockDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              Node node;
              if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
              {
                node = AiSession.Instance.NodePool.Get();
              }

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              AddTileToMap(mainGridPosition, node);
            }
            else if (cubeAboveDef.Id.SubtypeName.StartsWith("LargeCoverWall") || cubeAboveDef.Id.SubtypeName.StartsWith("FireCover"))
            {
              Node node;
              if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
              {
                node = AiSession.Instance.NodePool.Get();
              }

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              AddTileToMap(mainGridPosition, node);
            }
            else if (AiSession.Instance.LockerDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) > 0)
              {
                Node node;
                if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                {
                  node = AiSession.Instance.NodePool.Get();
                }

                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                AddTileToMap(mainGridPosition, node);
              }
            }
            else if (AiSession.Instance.ArmorPanelAllDefinitions.Contains(cubeAboveDef.Id))
            {
              if (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(cubeAboveDef.Id)
                || AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(cubeAboveDef.Id))
              {
                if (Base6Directions.GetIntVector(cubeAbove.Orientation.Left).Dot(ref normal) == 0)
                {
                  Node node;
                  if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                  {
                    node = AiSession.Instance.NodePool.Get();
                  }

                  node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                  AddTileToMap(mainGridPosition, node);
                }
              }
              else
              {
                Node node;
                if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                {
                  node = AiSession.Instance.NodePool.Get();
                }

                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                AddTileToMap(mainGridPosition, node);
              }
            }
            else if (AiSession.Instance.FreightBlockDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              Node node;
              if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
              {
                node = AiSession.Instance.NodePool.Get();
              }

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              AddTileToMap(mainGridPosition, node);
            }
            else if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              var turretBasePosition = cubeAbove.Position - Base6Directions.GetIntVector(cubeAbove.Orientation.Up);
              if (needsPositionAdjusted)
                turretBasePosition = MainGrid.WorldToGridInteger(cubeAbove.CubeGrid.GridIntegerToWorld(turretBasePosition));

              if (turretBasePosition != positionAbove || cubeAboveDef.Id.TypeId == typeof(MyObjectBuilder_InteriorTurret))
              {
                Node node;
                if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                {
                  node = AiSession.Instance.NodePool.Get();
                }

                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                AddTileToMap(mainGridPosition, node);
              }
            }
            else if (AiSession.Instance.CatwalkBlockDefinitions.Contains(cubeAboveDef.Id))
            {
              Node node;
              if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
              {
                node = AiSession.Instance.NodePool.Get();
              }

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              AddTileToMap(mainGridPosition, node);
            }
            else if (AiSession.Instance.FlatWindowDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              Node node;
              if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
              {
                node = AiSession.Instance.NodePool.Get();
              }

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              AddTileToMap(mainGridPosition, node);
            }
            else if (AiSession.Instance.HalfBlockDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) != 0)
              {
                Node node;
                if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                {
                  node = AiSession.Instance.NodePool.Get();
                }

                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                AddTileToMap(mainGridPosition, node);
              }
            }
            else if (cubeAboveDef.Id.SubtypeName.EndsWith("Slope2Tip"))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Left).Dot(ref normal) != 0)
              {
                Node node;
                if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                {
                  node = AiSession.Instance.NodePool.Get();
                }

                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                AddTileToMap(mainGridPosition, node);
              }
            }
            else if (cubeAbove.FatBlock != null)
            {
              var door = cubeAbove.FatBlock as IMyDoor;
              if (door != null)
              {
                var doorPosition = door.Position;
                if (needsPositionAdjusted)
                  doorPosition = MainGrid.WorldToGridInteger(cubeAbove.CubeGrid.GridIntegerToWorld(doorPosition));

                if (door is IMyAirtightHangarDoor)
                {
                  if (positionAbove != doorPosition)
                  {
                    Node node;
                    if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                    {
                      node = AiSession.Instance.NodePool.Get();
                    }

                    node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                    AddTileToMap(mainGridPosition, node);
                  }
                }
                else if (cubeAbove.BlockDefinition.Id.SubtypeName.StartsWith("SlidingHatchDoor"))
                {
                  if (positionAbove == doorPosition)
                  {
                    Node node;
                    if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                    {
                      node = AiSession.Instance.NodePool.Get();
                    }

                    node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                    AddTileToMap(mainGridPosition, node);
                  }
                }
                else if (cubeAbove.BlockDefinition.Id.SubtypeName == "LargeBlockGate")
                {
                  var doorCenter = door.WorldAABB.Center;
                  var nextPos = MainGrid.GridIntegerToWorld(positionAbove);
                  var vector = nextPos - doorCenter;

                  if (vector.LengthSquared() < 8)
                  {
                    Node node;
                    if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                    {
                      node = AiSession.Instance.NodePool.Get();
                    }

                    node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                    AddTileToMap(mainGridPosition, node);
                  }
                }
                else
                {
                  Node node;
                  if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                  {
                    node = AiSession.Instance.NodePool.Get();
                  }

                  node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                  AddTileToMap(mainGridPosition, node);
                }
              }
              else if (cubeAbove.FatBlock is IMySolarPanel)
              {
                if (cubeAbove.BlockDefinition.Id.SubtypeName.IndexOf("colorable") < 0 && Base6Directions.GetIntVector(cubeAbove.Orientation.Forward).Dot(ref normal) > 0)
                {
                  Node node;
                  if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                  {
                    node = AiSession.Instance.NodePool.Get();
                  }

                  node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                  AddTileToMap(mainGridPosition, node);
                }
              }
              else if (cubeAbove.FatBlock is IMyButtonPanel || AiSession.Instance.ButtonPanelDefinitions.ContainsItem(cubeAboveDef.Id)
                || (cubeAbove.FatBlock is IMyTextPanel && !AiSession.Instance.SlopeBlockDefinitions.Contains(cubeAboveDef.Id)))
              {
                Node node;
                if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
                {
                  node = AiSession.Instance.NodePool.Get();
                }

                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                AddTileToMap(mainGridPosition, node);
              }
            }

            if (blockPositionIsValid && isPressurized)
            {
              Node node;
              if (!OpenTileDict.TryGetValue(mainGridPosition, out node))
              {
                node = AiSession.Instance.NodePool.Get();
              }

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              AddTileToMap(mainGridPosition, node);
            }
          }
        }
      }

      AiSession.Instance.LineListPool?.Return(ref positionList);
    }

    public bool BlockPositionHasValidTile(IMySlimBlock cubeAbove, Vector3I positionAbove)
    {
      var cell = AiUtils.GetCellForPosition(cubeAbove, positionAbove);
      var keyTuple = MyTuple.Create(cubeAbove.BlockDefinition.Id, cell);
      return AiSession.Instance.BlockInfo.BlockDirInfo.ContainsKey(keyTuple);
    }

    void AddTileToMap(Vector3I mainGridPosition, Node node)
    {
      //if (OpenTileDict.Count == 0)
      //  AiSession.Instance.Logger.Log($"~~~ Starting Grid Map Entries ~~~");

      //if (mainGridPosition != node.Position)
      //{
      //  AiSession.Instance.Logger.Log($" !! Node != Position !!");
      //}

      //AiSession.Instance.Logger.Log($" {mainGridPosition} | {node.Position} | {node.NodeType}");

      OpenTileDict[mainGridPosition] = node;
    }

    public override void GetBlockedNodeEdges(Node node)
    {
      if (node?.Grid == null || node.Grid.MarkedForClose)
      {
        Dirty = true;
        return;
      }

      // Testing only!
      //if (TestCondition(node.Position))
      //{
      //  AiSession.Instance.Logger.Log("\n");
      //}

      var blockDirInfo = AiSession.Instance.BlockInfo.BlockDirInfo;
      var alwaysBlocked = AiSession.Instance.BlockInfo.NoPathBlocks;

      var upDir = MainGrid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
      var upVec = Base6Directions.GetIntVector(upDir);

      var nodePos = node.Position;
      var thisBlock = node.Block;

      MatrixI mThisInv = new MatrixI();

      if (thisBlock != null)
      {
        MatrixI mThis = new MatrixI(thisBlock.Orientation);
        MatrixI.Invert(ref mThis, out mThisInv);
      }

      // Check if node is above GCE stair block and assign blocked edges depending on stair orientation
      bool isGCEStair = false;
      bool isUTurn = false;
      bool isCorner = false;
      //MatrixI belowMatrixInv = new MatrixI();
      IMySlimBlock blockBelow = null;

      if (node.CanSkip) // only nodes above stair blocks can be skipped
      {
        blockBelow = GetBlockAtPosition(nodePos - upVec);
        var belowDef = blockBelow?.BlockDefinition as MyCubeBlockDefinition;
        var belowSubtype = belowDef?.Id.SubtypeName;

        isGCEStair = blockBelow != null && blockBelow != thisBlock
          && AiSession.Instance.GratedCatwalkExpansionBlocks.Contains(belowDef.Id)
          && belowSubtype.IndexOf("Stair", StringComparison.OrdinalIgnoreCase) >= 0;

        if (isGCEStair)
        {
          isUTurn = belowSubtype.IndexOf("UTurn", StringComparison.OrdinalIgnoreCase) >= 0;
          isCorner = belowSubtype.IndexOf("Corner", StringComparison.OrdinalIgnoreCase) >= 0;

          //MatrixI belowMatrix = new MatrixI(blockBelow.Orientation);
          //MatrixI.Invert(ref belowMatrix, out belowMatrixInv);
        }
      }

      foreach (var dirVec in AiSession.Instance.CardinalDirections)
      {
        var next = nodePos + dirVec;

        Node nextNode;
        if (!TryGetNodeForPosition(next, out nextNode))
        {
          node.SetBlocked(dirVec);
          continue;
        }

        if (isGCEStair)
        {
          //var localDirVec = Vector3I.TransformNormal(dirVec, ref belowMatrixInv);
          var dir = Base6Directions.GetDirection(dirVec);
          var opDir = Base6Directions.GetOppositeDirection(dir);

          if (opDir != blockBelow.Orientation.Up)
          {
            if (isUTurn)
            {
              if (dir != blockBelow.Orientation.Forward)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (isCorner)
            {
              if (AiUtils.AreStairsOnLeftSide(blockBelow))
              {
                if (opDir != blockBelow.Orientation.Left)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (dir != blockBelow.Orientation.Left)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (opDir != blockBelow.Orientation.Forward) // straight
            {
              node.SetBlocked(dirVec);
              continue;
            }
          }
        }

        if (thisBlock != null)
        {
          var localDirVec = Vector3I.TransformNormal(dirVec, ref mThisInv);
          var dir = Base6Directions.GetDirection(localDirVec);
          var dirFlag = Base6Directions.GetDirectionFlag(dir);

          var thisDef = thisBlock.BlockDefinition as MyCubeBlockDefinition;
          var blockCell = thisDef.Size.AbsMax() > 1 ? AiUtils.GetCellForPosition(thisBlock, nodePos) : Vector3I.Zero;
          var tuple = MyTuple.Create(thisDef.Id, blockCell);

          UsableEntry entry;
          if (!blockDirInfo.TryGetValue(tuple, out entry) || (entry.Mask & dirFlag) > 0)
          {
            node.SetBlocked(dirVec);
            continue;
          }
        }

        var nextBlock = nextNode.Block;
        if (nextBlock != null)
        {
          if (alwaysBlocked.Contains(nextBlock.BlockDefinition.Id))
          {
            node.SetBlocked(dirVec);
            continue;
          }

          MatrixI mNextInv;
          if (nextBlock == thisBlock)
          {
            mNextInv = mThisInv;
          }
          else
          {
            MatrixI mNext = new MatrixI(nextBlock.Orientation);
            MatrixI.Invert(ref mNext, out mNextInv);
          }

          var localDirVec = Vector3I.TransformNormal(dirVec, ref mNextInv);
          var oppositeDir = Base6Directions.GetOppositeDirection(Base6Directions.GetDirection(localDirVec));
          var opDirFlag = Base6Directions.GetDirectionFlag(oppositeDir);

          var nextDef = nextBlock.BlockDefinition as MyCubeBlockDefinition;
          var blockCell = nextDef.Size.AbsMax() > 1 ? AiUtils.GetCellForPosition(nextBlock, next) : Vector3I.Zero;
          var tuple = MyTuple.Create(nextDef.Id, blockCell);

          UsableEntry entry;
          if (!blockDirInfo.TryGetValue(tuple, out entry) || (entry.Mask & opDirFlag) > 0)
          {
            node.SetBlocked(dirVec);
            continue;
          }
        }
      }

      foreach (var dirVec in AiSession.Instance.VoxelMovementDirections)
      {
        Node nextNode;
        var next = nodePos + dirVec;
        if (node.IsGridNode && !node.IsGridNodePlanetTile)
        {
          node.SetBlocked(dirVec);
        }
        else if (DoesBlockExist(next))
        {
          node.SetBlocked(dirVec);
        }
        else if (!TryGetNodeForPosition(next, out nextNode))
        {
          node.SetBlocked(dirVec);
        }
        else if (node.IsGridNodePlanetTile && !nextNode.IsGridNodePlanetTile)
        {
          node.SetBlocked(dirVec);
        }
      }

      if (!node.IsGridNode || node.IsGridNodePlanetTile)
      {
        foreach (var dirVec in AiSession.Instance.DiagonalDirections)
        {
          Node nextNode;
          var next = nodePos + dirVec;
          if (DoesBlockExist(next))
          {
            node.SetBlocked(dirVec);
          }
          else if (!TryGetNodeForPosition(next, out nextNode))
          {
            node.SetBlocked(dirVec);
          }
          else if (node.IsGridNodePlanetTile && !nextNode.IsGridNodePlanetTile)
          {
            node.SetBlocked(dirVec);
          }
        }
      }
    }

    internal HashSet<Vector3I> testPositions = new HashSet<Vector3I>(Vector3I.Comparer)
    {
      new Vector3I(3,2,-17),
    };

    bool TestCondition(Vector3I position)
    {
      return testPositions.Contains(position);
    }

    public bool CheckBlockInfoForInvalidDirection(IMySlimBlock block, Vector3I gridPosition, Vector3I side)
    {
      if (TestCondition(gridPosition))
      {
        AiSession.Instance.Logger.Log("\n");
      }

      var def = block.BlockDefinition.Id;
      if (AiSession.Instance.BlockInfo.NoPathBlocks.Contains(def))
      {
        return true;
      }

      MatrixI m = new MatrixI(block.Orientation);
      MatrixI mInv;
      MatrixI.Invert(ref m, out mInv);

      var localSide = Vector3I.TransformNormal(side, ref mInv);
      var localPosition = Vector3I.TransformNormal(gridPosition, ref mInv);
      var tuple = MyTuple.Create(def, localPosition);

      var dir = Base6Directions.GetDirection(localSide);
      UsableEntry entry;

      if (!AiSession.Instance.BlockInfo.BlockDirInfo.TryGetValue(tuple, out entry))
      {
        return true;
      }

      return (entry.Mask & Base6Directions.GetDirectionFlag(dir)) > 0;
    }

    public override bool GetRandomNodeNearby(BotBase bot, Vector3D targetPosition, out Vector3I node)
    {
      List<Vector3I> localNodes = AiSession.Instance.LineListPool.Get();

      var botPosition = bot.BotInfo.CurrentBotPositionActual;
      var collection = bot._pathCollection;
      var botWorldMatrix = bot.WorldMatrix;

      node = MainGrid.WorldToGridInteger(botPosition);

      foreach (var point in Neighbors(bot, node, node, botPosition, true, up: botWorldMatrix.Up))
      {
        if (collection.DeniedDoors.ContainsKey(point))
          continue;

        IHitInfo hitInfo;
        var worldNode = MainGrid.GridIntegerToWorld(point);

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

        localNodes.Add(point);
      }

      bool result = false;
      if (localNodes.Count > 0)
      {
        var rnd = MyUtils.GetRandomInt(0, localNodes.Count);
        node = localNodes[rnd];
        result = true;
      }

      AiSession.Instance.LineListPool?.Return(ref localNodes);
      return result;
    }

    public bool GetRandomOpenTile(BotBase bot, out Node node, bool allowAirNodes = true, bool allowPlanetTiles = true, bool interiorFirst = true)
    {
      node = null;
      if (OpenTileDict.Count == 0 && (!allowPlanetTiles || PlanetTileDictionary.Count == 0))
        return false;

      foreach (var tile in OpenTileDict)
      {
        if (tile.Value != null && tile.Value.Block == null && CanBotUseTile(bot, tile.Value, allowAirNodes))
        {
          node = tile.Value;

          if (!interiorFirst || (node.IsGridNode && node.Grid?.IsRoomAtPositionAirtight(node.Position) == true))
          {
            break;
          }
        }
      }

      if (node == null && allowPlanetTiles)
      {
        foreach (var tile in PlanetTileDictionary)
        {
          if (tile.Value != null && tile.Value.Block == null && CanBotUseTile(bot, tile.Value, allowAirNodes))
          {
            node = tile.Value;
            break;
          }
        }
      }

      return node != null;
    }

    public override bool GetRandomOpenNode(BotBase bot, Vector3D requestedPosition, out Node node)
    {
      node = null;
      if (OpenTileDict.Count == 0 && PlanetTileDictionary.Count == 0)
        return false;

      List<Vector3I> nodeList = AiSession.Instance.LineListPool.Get();
      List<Vector3I> testList = AiSession.Instance.LineListPool.Get();

      MainGrid.RayCastCells(bot.BotInfo.CurrentBotPositionActual, requestedPosition, nodeList, new Vector3I(11));

      bool getGroundFirst = !bot.CanUseAirNodes;

      for (int i = 0; i < nodeList.Count; i++)
      {
        var localPosition = nodeList[i];

        if (getGroundFirst)
          GetClosestGroundNode(localPosition, testList, out localPosition);

        Node tempNode;
        if (IsPositionUsable(bot, LocalToWorld(localPosition), out tempNode))
        {
          node = tempNode;
          continue;
        }

        break;
      }

      //var localBot = WorldToLocal(bot.Position);
      //var botPosition = LocalToWorld(localBot);

      //var vector = requestedPosition - botPosition;
      //var length = vector.Normalize();
      //var cellSize = (double)CellSize;

      //var count = (int)Math.Floor(length / cellSize);

      //for (int i = count; i >= 0; i--)
      //{
      //  var worldPos = botPosition + (vector * count * cellSize);
      //  if (!OBB.Contains(ref worldPos))
      //    continue;

      //  Vector3I tempNode;
      //  if (!bot.CanUseAirNodes && GetClosestGroundNode(WorldToLocal(worldPos), testList, out tempNode))
      //    worldPos = LocalToWorld(tempNode);

      //  if (IsPositionUsable(bot, worldPos, out node))
      //    break;
      //}

      nodeList.Clear();
      testList.Clear();
      AiSession.Instance.LineListPool?.Return(ref nodeList);
      AiSession.Instance.LineListPool?.Return(ref testList);

      return node != null;
    }

    bool GetClosestGroundNode(Vector3I pos, List<Vector3I> list, out Vector3I groundPos)
    {
      groundPos = pos;

      Node node;
      if (TryGetNodeForPosition(pos, out node) && node.IsGroundNode)
      {
        groundPos = node.Position;
        return true;
      }

      var top = pos + Vector3I.Up * 10;
      var btm = pos - Vector3I.Up * 10;

      var posWorld = MainGrid.GridIntegerToWorld(pos);
      var topWorld = MainGrid.GridIntegerToWorld(top);
      var btmWorld = MainGrid.GridIntegerToWorld(btm);

      list.Clear();
      MainGrid.RayCastCells(topWorld, btmWorld, list, new Vector3I(11));

      bool result = false;
      int distance = int.MaxValue;

      for (int i = 0; i < list.Count; i++)
      {
        var localPos = list[i];

        if (TryGetNodeForPosition(localPos, out node) && node.IsGroundNode)
        {
          var dManhattan = Vector3I.DistanceManhattan(localPos, pos);
          if (dManhattan < distance)
          {
            distance = dManhattan;
            groundPos = localPos;
            result = true;
          }
        }
      }

      return result;
    }

    public override void UpdateTempObstacles()
    {
      if (!_obstacleTask.IsComplete)
        return;

      if (IsValid && _obstacleTask.Exceptions != null)
      {
        AiSession.Instance.Logger.ClearCached();
        AiSession.Instance.Logger.AddLine($"Exceptions found during update task!\n");
        foreach (var ex in _obstacleTask.Exceptions)
          AiSession.Instance.Logger.AddLine($" -> {ex}\n");

        AiSession.Instance.Logger.LogAll();

        if (MyAPIGateway.Session.Player != null)
          MyAPIGateway.Utilities.ShowNotification($"Exception during ObstacleTask!");
      }

      List<MyEntity> tempEntities = AiSession.Instance.EntListPool.Get();
      List<IMySlimBlock> blocks = AiSession.Instance.SlimListPool.Get();

      var sphere = new BoundingSphereD(OBB.Center, OBB.HalfExtent.AbsMax());
      MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, tempEntities);

      for (int i = tempEntities.Count - 1; i >= 0; i--)
      {
        try
        {
          if (i >= tempEntities.Count)
            continue;

          var grid = tempEntities[i] as MyCubeGrid;
          if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose || grid.EntityId == MainGrid?.EntityId)
            continue;

          ((IMyCubeGrid)grid).GetBlocks(blocks);
        }
        catch { }
      }

      tempEntities.Clear();

      if (blocks.Count > 0)
      {
        _tempObstaclesWorkData.Blocks = blocks;
        _tempObstaclesWorkData.Entities = tempEntities;
        _obstacleTask = MyAPIGateway.Parallel.Start(UpdateTempObstaclesAsync, UpdateTempObstaclesCallback, _tempObstaclesWorkData);
      }
      else
      {
        AiSession.Instance.EntListPool?.Return(ref tempEntities);
        AiSession.Instance.SlimListPool?.Return(ref blocks);
      }
    }

    List<KeyValuePair<IMySlimBlock, Vector3I>> _tempKVPList = new List<KeyValuePair<IMySlimBlock, Vector3I>>();

    void UpdateTempObstaclesAsync(WorkData data)
    {
      //AiSession.Instance.Logger.Log($"{this}.UpdateTempObstaclesAsync: Start");

      var obstacleData = data as ObstacleWorkData;
      if (obstacleData != null && AiSession.Instance.Registered)
      {
        ObstacleNodesTemp.Clear();
        var blocks = obstacleData.Blocks;
        var tempEntities = obstacleData.Entities;
        _tempKVPList.Clear();

        List<Vector3I> positionList = AiSession.Instance.LineListPool.Get();

        for (int i = 0; i < blocks.Count; i++)
        {
          var b = blocks[i];
          if (b?.CubeGrid == null || b.IsDestroyed || b.CubeGrid.MarkedForClose)
            continue;

          positionList.Clear();
          AiUtils.FindAllPositionsForBlock(b, positionList);
          var grid = b.CubeGrid;

          foreach (var position in positionList)
          {
            var worldPoint = b.CubeGrid.GridIntegerToWorld(position);
            if (!OBB.Contains(ref worldPoint))
              continue;

            var graphLocal = WorldToLocal(worldPoint);
            if (IsOpenTile(graphLocal) && !ObstacleNodesTemp.ContainsKey(graphLocal))
            {
              ObstacleNodesTemp[graphLocal] = new KeyValuePair<IMyCubeGrid, bool>(grid, false);
            }

            var sphere = new BoundingSphereD(worldPoint, b.CubeGrid.GridSize * 0.6f);

            foreach (var dir in AiSession.Instance.CardinalDirections)
            {
              var otherLocal = graphLocal + dir;

              if (IsOpenTile(otherLocal) && !ObstacleNodesTemp.ContainsKey(otherLocal))
              {
                var otherWorld = LocalToWorld(otherLocal);
                var otherSphere = new BoundingSphereD(otherWorld, 1);

                if (sphere.Contains(otherSphere) != ContainmentType.Disjoint)
                {
                  _tempKVPList.Add(new KeyValuePair<IMySlimBlock, Vector3I>(b, otherLocal));
                }
              }
            }
          }
        }

        AiSession.Instance.LineListPool?.Return(ref positionList);

        foreach (var kvp in _tempKVPList)
        {
          var node = kvp.Value;
          if (!ObstacleNodesTemp.ContainsKey(node))
            ObstacleNodesTemp[node] = new KeyValuePair<IMyCubeGrid, bool>(kvp.Key.CubeGrid, true);
        }
      }

      //AiSession.Instance.Logger.Log($"{this}.UpdateTempObstaclesAsync: Finished");
    }

    void UpdateTempObstaclesCallback(WorkData data)
    {
      if (AiSession.Instance?.Registered == true)
      {
        Interlocked.CompareExchange(ref ObstacleNodes, ObstacleNodesTemp, ObstacleNodes);

        var obstacleData = data as ObstacleWorkData;
        if (obstacleData?.Blocks != null)
        {
          AiSession.Instance.SlimListPool?.Return(ref obstacleData.Blocks);
          obstacleData.Blocks = null;
        }

        if (obstacleData?.Entities != null)
        {
          AiSession.Instance.EntListPool?.Return(ref obstacleData.Entities);
          obstacleData.Entities = null;
        }
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
        if (IsPositionUsable(bot, kvp.Key))
        {
          node = kvp.Value;

          if (node.IsGroundNode)
            return node;
          else if (backup == null)
            backup = node;
        }
      }

      foreach (var kvp in PlanetTileDictionary)
      {
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

    public Vector3D GetLastValidNodeOnLine(Vector3D start, Vector3D directionNormalized, double desiredDistance, bool ensureOpenTiles = true)
    {
      List<Vector3I> nodeList = AiSession.Instance.LineListPool.Get();

      Vector3D result = start;
      var end = start + directionNormalized * desiredDistance;

      MainGrid.RayCastCells(start, end, nodeList);
      Vector3I prevNode = WorldToLocal(start);

      for (int i = 0; i < nodeList.Count; i++)
      {
        var localPos = nodeList[i];
        var cube = GetBlockAtPosition(localPos);
        var def = cube?.BlockDefinition as MyCubeBlockDefinition;

        if (!ensureOpenTiles && def?.HasPhysics != true)
        {
          var world = LocalToWorld(localPos);
          if (!PointInsideVoxel(world, RootVoxel))
          {
            result = world;
            continue;
          }
        }

        Node node;
        if (BlockedDoors.ContainsKey(localPos) || TempBlockedNodes.ContainsKey(localPos) || ObstacleNodes.ContainsKey(localPos) || !TryGetNodeForPosition(localPos, out node))
          break;

        if (def?.HasPhysics == true)
        {
          if (node.IsBlocked(prevNode - localPos))
            break;

          if (TryGetNodeForPosition(prevNode, out node) && node.IsBlocked(localPos - prevNode))
            break;
        }

        result = LocalToWorld(localPos);
        prevNode = localPos;
      }

      nodeList.Clear();
      AiSession.Instance.LineListPool?.Return(ref nodeList);
      return result;
    }

    public override bool IsPositionAvailable(Vector3D position, BotBase bot)
    {
      var node = WorldToLocal(position);
      return IsPositionAvailable(node, bot);
    }

    public override bool IsPositionAvailable(Vector3I node, BotBase bot)
    {
      if (base.IsPositionAvailable(node, bot))
      {
        IMyDoor door;
        if (BlockedDoors.TryGetValue(node, out door) && door != null)
        {
          if (bot == null || bot.Target.Entity == null)
            return false;

          var ch = bot.Target.Entity as IMyCharacter;
          if (ch != null && ch == bot.Owner?.Character)
            return false;

          var grid = door.CubeGrid;
          long gridOwner;
          try
          {
            gridOwner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : door.OwnerId;
          }
          catch
          {
            gridOwner = door.OwnerId;
          }

          var botOwner = bot.Owner?.IdentityId ?? bot.BotIdentityId;

          var rel = MyIDModule.GetRelationPlayerPlayer(gridOwner, botOwner);
          if (rel != MyRelationsBetweenPlayers.Enemies)
            return false;
        }
      }

      return false;
    }

    public void RecalculateOBB()
    {
      var box = GetMapBoundingBoxLocal();
      box.Inflate(_boxExpansion);

      var worldCenter = MainGrid.GridIntegerToWorld(box.Center);
      var quat = Quaternion.CreateFromRotationMatrix(MainGrid.WorldMatrix);
      var halfVector = Vector3D.Half * CellSize;

      var newOBB = new MyOrientedBoundingBoxD(worldCenter, box.HalfExtents * CellSize + halfVector, quat);

      if (newOBB.HalfExtent.LengthSquared() > OBB.HalfExtent.LengthSquared())
      {
        Remake = true;
        Dirty = true;
      }
      else
      {
        box = BoundingBox;
        worldCenter = MainGrid.GridIntegerToWorld(box.Center);
        newOBB = new MyOrientedBoundingBoxD(worldCenter, box.HalfExtents * CellSize + halfVector, quat);
      }

      OBB = newOBB;

      box.Inflate(-_boxExpansion);
      UnbufferedOBB = new MyOrientedBoundingBoxD(worldCenter, box.HalfExtents * CellSize + halfVector, quat);
    }

    public void CheckPlanet()
    {
      if (MainGrid?.Physics == null || MainGrid.MarkedForClose || MainGrid.Physics.IsStatic)
        return;

      float _;
      var gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out _);
      if (gravity.LengthSquared() > 0)
      {
        var newPlanet = MyGamePruningStructure.GetClosestPlanet(OBB.Center);

        if (RootVoxel != null && newPlanet != RootVoxel)
          Planet_OnMarkForClose(RootVoxel);

        RootVoxel = newPlanet;

        if (RootVoxel != null)
        {
          RootVoxel.RangeChanged -= Planet_RangeChanged;
          RootVoxel.OnMarkForClose -= Planet_OnMarkForClose;

          RootVoxel.RangeChanged += Planet_RangeChanged;
          RootVoxel.OnMarkForClose += Planet_OnMarkForClose;
        }
      }
      else if (RootVoxel != null)
      {
        Planet_OnMarkForClose(RootVoxel);
        RootVoxel = null;
      }
    }

    public void AdjustWorldMatrix()
    {
      if (MainGrid?.Physics == null || MainGrid.Physics.IsStatic || MainGrid.WorldMatrix.EqualsFast(ref _lastMatrix))
        return;

      int numSeats, numFactories;
      bool matrixSet = false;
      if (MainGrid.HasMainCockpit())
      {
        WorldMatrix = MainGrid.MainCockpit.WorldMatrix;
        matrixSet = true;
      }
      else if (MainGrid.HasMainRemoteControl())
      {
        WorldMatrix = MainGrid.MainRemoteControl.WorldMatrix;
        matrixSet = true;
      }
      else if ((MainGrid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_Cockpit), out numSeats) && numSeats > 0)
        || (MainGrid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_RemoteControl), out numSeats) && numSeats > 0)
        || (MainGrid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_ConveyorSorter), out numFactories) && numFactories > 0))
      {
        foreach (var b in MainGrid.GetFatBlocks())
        {
          if (b is IMyShipController || (b is MyConveyorSorter && b.BlockDefinition.Id.SubtypeName == "RoboFactory"))
          {
            WorldMatrix = b.WorldMatrix;
            matrixSet = true;
            break;
          }
        }
      }

      if (!matrixSet)
      {
        float _;
        var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out _);
        var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(OBB.Center, 0);
        if (aGrav.LengthSquared() > 0)
        {
          var up = -Vector3D.Normalize(aGrav);
          var fwd = Vector3D.CalculatePerpendicularVector(up);

          var dir = MainGrid.WorldMatrix.GetClosestDirection(up);
          up = MainGrid.WorldMatrix.GetDirectionVector(dir);

          dir = MainGrid.WorldMatrix.GetClosestDirection(fwd);
          fwd = MainGrid.WorldMatrix.GetDirectionVector(dir);

          WorldMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
        }
        else if (nGrav.LengthSquared() > 0)
        {
          var up = -Vector3D.Normalize(nGrav);
          var fwd = Vector3D.CalculatePerpendicularVector(up);

          var dir = MainGrid.WorldMatrix.GetClosestDirection(up);
          up = MainGrid.WorldMatrix.GetDirectionVector(dir);

          dir = MainGrid.WorldMatrix.GetClosestDirection(fwd);
          fwd = MainGrid.WorldMatrix.GetDirectionVector(dir);

          WorldMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
        }
        else
          return;

        bool rotate = true;
        foreach (var block in MainGrid.GetFatBlocks())
        {
          rotate = false;
          break;
        }

        if (rotate)
        {
          var rotatedMatrix = MatrixD.CreateRotationY(MathHelper.PiOver2) * WorldMatrix;
          WorldMatrix = rotatedMatrix;
        }
      }

      _lastMatrix = MainGrid.WorldMatrix;
    }

    public override void ClearTempObstacles()
    {
      if (OpenTileDict != null)
      {
        foreach (var node in OpenTileDict.Values)
        {
          node.TempBlockedMask = 0;
        }
      }

      if (PlanetTileDictionary != null)
      {
        foreach (var node in PlanetTileDictionary.Values)
        {
          node.TempBlockedMask = 0;
        }
      }

      NeedsTempCleared = false;
    }

    public override bool IsPositionValid(Vector3D position)
    {
      return OBB.Contains(ref position);
    }

    public override bool TryGetNodeForPosition(Vector3I position, out Node node)
    {
      node = null;
      if (OpenTileDict?.TryGetValue(position, out node) == true)
        return node != null;

      if (PlanetTileDictionary?.TryGetValue(position, out node) == true)
        return node != null;

      return false;
    }

    public override bool IsOpenTile(Vector3I position)
    {
      return PlanetTileDictionary?.ContainsKey(position) == true || OpenTileDict?.ContainsKey(position) == true;
    }

    public override bool IsObstacle(Vector3I position, BotBase bot, bool includeTemp)
    {
      if (!IsValid)
        return false;

      bool result = bot?._pathCollection != null && bot._pathCollection.Obstacles.ContainsKey(position);

      if (!result && bot != null && !(bot is RepairBot) && (bot.Target.IsSlimBlock || bot.Target.IsCubeBlock))
      {
        var cube = bot.Target.Entity as IMyCubeBlock;
        var slim = (cube?.SlimBlock) ?? bot.Target.Entity as IMySlimBlock;

        if (slim?.CubeGrid != null)
        {
          List<KeyValuePair<IMySlimBlock, Vector3D>> blocks;
          if (bot._pathCollection.BlockObstacles.TryGetValue(slim.CubeGrid.EntityId, out blocks))
          {
            Vector3D slimWorld;
            if (cube != null)
              slimWorld = cube.GetPosition();
            else if (slim.FatBlock != null)
              slimWorld = slim.FatBlock.GetPosition();
            else
              slim.ComputeWorldCenter(out slimWorld);

            for (int i = 0; i < blocks.Count; i++)
            {
              var kvp = blocks[i];
              if (kvp.Key == slim)
              {
                if (Vector3D.IsZero(slimWorld - kvp.Value, 1))
                {
                  result = true;
                }
                else
                {
                  blocks.RemoveAtFast(i);
                }

                break;
              }
            }
          }
        }
      }

      if (!includeTemp)
        return result;

      IMyDoor door;
      if (!result && BlockedDoors.TryGetValue(position, out door) && door != null)
      {
        if (bot == null || bot.Target.Entity == null)
          return false;

        var ch = bot.Target.Entity as IMyCharacter;
        if (ch != null && ch == bot.Owner?.Character)
          return false;

        var grid = door.CubeGrid;
        long gridOwner;
        try
        {
          gridOwner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : door.OwnerId;
        }
        catch
        {
          gridOwner = door.OwnerId;
        }

        var botOwner = bot.Owner?.IdentityId ?? bot.BotIdentityId;

        var rel = MyIDModule.GetRelationPlayerPlayer(gridOwner, botOwner);
        if (rel != MyRelationsBetweenPlayers.Enemies)
          return false;
      }

      return result || ObstacleNodes.ContainsKey(position) || TempBlockedNodes.ContainsKey(position);
    }

    public override Node GetValueOrDefault(Vector3I position, Node defaultValue)
    {
      Node node = null;
      if (PlanetTileDictionary?.TryGetValue(position, out node) == true && node != null)
        return node;

      if (OpenTileDict?.TryGetValue(position, out node) == true && node != null)
        return node;

      return defaultValue;
    }

    public override IMySlimBlock GetBlockAtPosition(Vector3I mainGridPosition, bool checkOtherGrids = false)
    {
      var block = MainGrid?.GetCubeBlock(mainGridPosition) as IMySlimBlock;

      if (block == null && checkOtherGrids)
      {
        var worldPosition = MainGrid.GridIntegerToWorld(mainGridPosition);
        var sphere = new BoundingSphereD(worldPosition, 0.25);

        List<MyEntity> entList = AiSession.Instance.EntListPool.Get();

        MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entList);

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

        AiSession.Instance.EntListPool?.Return(ref entList);
      }

      if (block != null)
      {
        var def = block.BlockDefinition as MyCubeBlockDefinition;
        if (def == null || !def.HasPhysics || def.Id.SubtypeName.StartsWith("LargeWarningSign"))
          block = null;
      }

      return block;
    }

    public bool DoesBlockExist(Vector3I mainGridPosition, bool checkPhysics = true)
    {
      if (MainGrid == null || MainGrid.MarkedAsTrash)
        return false;

      IMySlimBlock slim = MainGrid.GetCubeBlock(mainGridPosition);
      if (slim != null)
      {
        return !checkPhysics || ((MyCubeBlockDefinition)slim.BlockDefinition).HasPhysics;
      }

      //var worldPosition = MainGrid.GridIntegerToWorld(mainGridPosition);

      //for (int i = 0; i < GridCollection.Count; i++)
      //{
      //  var grid = GridCollection[i] as MyCubeGrid;
      //  if (grid?.Physics == null || grid.MarkedForClose || grid.IsPreview || grid.EntityId == MainGrid?.EntityId)
      //    continue;

      //  var localPosition = grid.WorldToGridInteger(worldPosition);
      //  slim = grid.GetCubeBlock(localPosition);
      //  if (slim != null)
      //  {
      //    return !checkPhysics || ((MyCubeBlockDefinition)slim.BlockDefinition).HasPhysics;
      //  }
      //}

      return false;
    }

    public MyRelationsBetweenPlayers GetRelationshipTo(BotBase bot)
    {
      if (bot == null || bot.IsDead || MainGrid == null || MainGrid.MarkedForClose)
        return MyRelationsBetweenPlayers.Neutral;

      var botOwnerId = bot.Owner?.IdentityId ?? bot.BotIdentityId;
      long gridOwnerId;
      try
      {
        gridOwnerId = MainGrid.BigOwners.Count > 0 ? MainGrid.BigOwners[0] : MainGrid.SmallOwners.Count > 0 ? MainGrid.SmallOwners[0] : 0L;
      }
      catch
      {
        gridOwnerId = 0L;
      }

      return MyIDModule.GetRelationPlayerPlayer(botOwnerId, gridOwnerId, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
    }
  }
}