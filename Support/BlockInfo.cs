using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Collections;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

using VRageMath;
using AiEnabled.Ai.Support;
using AiEnabled.Bots;
using Sandbox.Game.Entities;
using AiEnabled.Support;
using VRage.Game.Entity.UseObject;
using VRage.Game.Entity;
using AiEnabled.Particles;
using AiEnabled.GameLogic;
using Sandbox.ModAPI;
using Sandbox.Definitions;
using Sandbox.Common.ObjectBuilders;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;
using MyItemInfo = VRage.Game.ModAPI.Ingame.MyItemInfo;
using Direction = VRageMath.Base6Directions.DirectionFlags;
using VRage.Voxels;
using AiEnabled.API;
using AiEnabled.Parallel;
using VRage.Input;
using AiEnabled.ConfigData;
using VRage.Utils;
using VRage;
using System.Runtime.ConstrainedExecution;
using AiEnabled.Utilities;
using ObjectBuilders.SafeZone;
using SpaceEngineers.ObjectBuilders.ObjectBuilders;
using Havok;
using System.IO;
using ProtoBuf;

namespace AiEnabled
{
  [Serializable]
  [ProtoContract]
  public struct SerializableEntry
  {
    [ProtoMember(100)] public SerializableDefinitionId Id;
    [ProtoMember(200)] public SerializableVector3I Position;
    [ProtoMember(300)] public SerializableVector3 Offset;
    [ProtoMember(400)] public bool SpecialConsideration;
    [ProtoMember(500)] public bool IsGroundNode;
    [ProtoMember(600)] public byte Mask;

    public SerializableEntry(MyDefinitionId id, Vector3I position, Vector3 offsetDir, float offsetAmount, bool special, bool groundNode, byte mask)
    {
      Id = id;
      Position = position;
      Offset = offsetDir * offsetAmount;
      SpecialConsideration = special;
      Mask = mask;
      IsGroundNode = groundNode;
    }

    public SerializableEntry(MyDefinitionId id, Vector3I position, Vector3 offsetDir, float offsetAmount, bool special, bool groundNode, Direction mask)
    {
      Id = id;
      Position = position;
      Offset = offsetDir * offsetAmount;
      SpecialConsideration = special;
      IsGroundNode = groundNode;
      Mask = (byte)mask;
    }

    public MyTuple<MyDefinitionId, Vector3I> GetKey() => MyTuple.Create((MyDefinitionId)Id, (Vector3I)Position);
  }

  public class UsableEntry
  {
    public MyDefinitionId Id;
    public Vector3I Position;
    public Vector3 Offset;
    public Direction Mask;
    public bool SpecialConsideration;
    public bool IsGroundNode;

    public UsableEntry(SerializableEntry entry)
    {
      Id = entry.Id;
      Position = entry.Position;
      Offset = entry.Offset;
      SpecialConsideration = entry.SpecialConsideration;
      IsGroundNode = entry.IsGroundNode;
      Mask = (Direction)entry.Mask;
    }

    public MyTuple<MyDefinitionId, Vector3I> GetKey() => MyTuple.Create((MyDefinitionId)Id, (Vector3I)Position);

    public Vector3 GetOffset(IMySlimBlock block)
    {
      Vector3 offset = Vector3.Zero;
      var grid = block.CubeGrid;

      if (!SpecialConsideration && Offset != Vector3I.Zero && grid != null && !grid.MarkedForClose)
      {
        Matrix m;
        if (block.FatBlock != null)
        {
          m = block.FatBlock.WorldMatrix;
        }
        else
        {
          var quat = Quaternion.CreateFromRotationMatrix(grid.WorldMatrix);
          block.Orientation.GetMatrix(out m);
          Matrix.Transform(ref m, ref quat, out m);
        }

        var dirVec = m.Right * Offset.X + m.Up * Offset.Y + m.Backward * Offset.Z;
        offset = dirVec * grid.GridSize;
      }

      return offset;
    }

    /// <summary>
    /// Sets the offset for the cell above to be the same as the current block's waypoint. Used for slopes.
    /// </summary>
    /// <param name="thisBlock">the sloped block</param>
    /// <param name="gridPositionAbove">cell position above the sloped block</param>
    /// <returns>Vector3 offset for the cell above</returns>
    public Vector3 GetOffsetForCellAbove(IMySlimBlock thisBlock, Vector3I gridPositionAbove)
    {
      Vector3 offset = Vector3.Zero;
      var grid = thisBlock?.CubeGrid;

      if (!SpecialConsideration && grid != null && !grid.MarkedForClose)      
      {
        var thisOffset = GetOffset(thisBlock);
        var worldPosition = grid.GridIntegerToWorld(thisBlock.Position) + thisOffset;
        var worldPositionAbove = grid.GridIntegerToWorld(gridPositionAbove);

        offset = worldPosition - worldPositionAbove;
      }

      return offset;
    }
  }

  public partial class BlockInfo
  {
    /// <summary>
    /// Blocks that have at least one cell that be traversed. Key is a tuple of Definition,Cell. The entry holds directions that are *blocked* along with any positional offset for the waypoint.
    /// </summary>
    public Dictionary<MyTuple<MyDefinitionId, Vector3I>, UsableEntry> BlockDirInfo = new Dictionary<MyTuple<MyDefinitionId, Vector3I>, UsableEntry>(new MyTupleComparer<MyDefinitionId, Vector3I>());

    /// <summary>
    /// Blocks that have zero cells that can be traversed
    /// </summary>
    public HashSet<MyDefinitionId> NoPathBlocks = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);

    /// <summary>
    /// Blocks that require more than one waypoint per block cell (stackable stairs, catwalks with unusual railing patterns, etc)
    /// </summary>
    public HashSet<MyDefinitionId> SpecialHandling = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);

    Dictionary<MyDefinitionId, Dictionary<Vector3I, MyTuple<Direction[], Vector3, float, bool, bool>>> _invalidBlockDirInfo;
    HashSet<MyDefinitionId> _KnownBlocks = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    readonly Logger _logger;

    public BlockInfo(Logger log)
    {
      _logger = log;
    }

    public void Close()
    {
      _invalidBlockDirInfo?.Clear();
      _KnownBlocks?.Clear();
      BlockDirInfo?.Clear();
      NoPathBlocks?.Clear();
      SpecialHandling?.Clear();

      _invalidBlockDirInfo = null;
      _KnownBlocks = null;
      BlockDirInfo = null;
      NoPathBlocks = null;
      SpecialHandling = null;
    }

    public bool IsKnown(MyDefinitionId id) => _KnownBlocks?.Contains(id) ?? false;

    public void SerializeToDisk()
    {
      var entries = new List<SerializableEntry>(_invalidBlockDirInfo.Count * 2);

      foreach (var kvp in _invalidBlockDirInfo)
      {
        foreach (var item in kvp.Value)
        {
          var tup = item.Value;
          var maskArray = tup.Item1;
          var offsetDir = tup.Item2;
          var offsetAmt = tup.Item3;
          var special = tup.Item4;
          var groundNode = tup.Item5;

          Direction dir = 0;
          for (int i = 0; i < maskArray.Length; i++)
          {
            dir |= maskArray[i];
          }

          entries.Add(new SerializableEntry(kvp.Key, item.Key, offsetDir, offsetAmt, special, groundNode, dir));
        }
      }

      Config.WriteBinaryFileToWorldStorage("BlockInfo.dat", GetType(), entries, _logger);
    }

    public void DeserializeFromDisk(MyObjectBuilder_Checkpoint.ModItem modItem)
    {
      List<SerializableEntry> entries = null;
      List<SerializableDefinitionId> noPathBlocks = null;

      var path1 = Path.Combine("Data", "BlockInfo", "BlockInfo.dat");
      //_logger.Log($"Path1 = {Path.Combine(mod.GetPath(), path1)}");
      entries = Config.ReadBinaryFileFromModLocation<List<SerializableEntry>>(path1, modItem, _logger);

      var path2 = Path.Combine("Data", "BlockInfo", "NoPathBlocks.dat");
      //_logger.Log($"Path2 = {Path.Combine(mod.GetPath(), path2)}");
      noPathBlocks = Config.ReadBinaryFileFromModLocation<List<SerializableDefinitionId>>(path2, modItem, _logger);


      if (entries != null)
      {
        for (int i = 0; i < entries.Count;  i++)
        {
          var entry = entries[i];

          MyCubeBlockDefinition cubedef;
          if (MyDefinitionManager.Static.TryGetCubeBlockDefinition(entry.Id, out cubedef))
          {
            if (!cubedef.Context.IsBaseGame && cubedef.Context.ModName.StartsWith("MorePassages")) // TODO: Remove this once the mod is fixed (several blocks have collision on all sides)
            {
              NoPathBlocks.Add(entry.Id);
            }
            else
            {
              BlockDirInfo[entry.GetKey()] = new UsableEntry(entry);

              if (entry.SpecialConsideration)
                SpecialHandling.Add(entry.Id);
            }
          }
        }

        entries.Clear();
        _logger.Debug($"Read {BlockDirInfo.Count} block entries from disk!");
      }
      else
      {
        _logger?.Error($"Failed to read block info from disk");
      }

      if (noPathBlocks != null)
      {
        for (int i = 0; i < noPathBlocks.Count; i++)
        {
          var item = noPathBlocks[i];
          NoPathBlocks.Add(item);
        }

        _logger.Debug($"Read {NoPathBlocks.Count} no path entries from disk!");
      }
      else
      {
        _logger?.Error($"Failed to read no path info from disk");
      }
    }

    public void Deserialize_Debug()
    {
      List<SerializableEntry> entries = Config.ReadBinaryFileFromWorldStorage<List<SerializableEntry>>("BlockInfo.dat", GetType(), _logger);
      List<SerializableDefinitionId> noPathBlocks = Config.ReadBinaryFileFromWorldStorage<List<SerializableDefinitionId>>("NoPathBlocks.dat", GetType(), _logger);

      if (entries != null)
      {
        for (int i = 0; i < entries.Count; i++)
        {
          var entry = entries[i];

          MyCubeBlockDefinition cubedef;
          if (MyDefinitionManager.Static.TryGetCubeBlockDefinition(entry.Id, out cubedef))
          {
            if (!cubedef.Context.IsBaseGame && cubedef.Context.ModName.StartsWith("MorePassages")) // TODO: Remove this once the mod is fixed (several blocks have collision on all sides)
            {
              NoPathBlocks.Add(entry.Id);
            }
            else
            {
              BlockDirInfo[entry.GetKey()] = new UsableEntry(entry);

              if (entry.SpecialConsideration)
                SpecialHandling.Add(entry.Id);
            }
          }
        }

        entries.Clear();
        _logger.Debug($"Read {BlockDirInfo.Count} block entries from disk!");
      }
      else
      {
        _logger?.Error($"Failed to read block info from disk");
      }

      if (noPathBlocks != null)
      {
        for (int i = 0; i < noPathBlocks.Count; i++)
        {
          var item = noPathBlocks[i];
          NoPathBlocks.Add(item);
        }

        _logger.Debug($"Read {NoPathBlocks.Count} no path entries from disk!");
      }
      else
      {
        _logger?.Error($"Failed to read no path info from disk");
      }
    }

    public void GenerateMissingBlockList(AiSession mod)
    {
      var noPathBlocks = new HashSet<SerializableDefinitionId>();

      foreach (var def in MyDefinitionManager.Static.GetAllDefinitions())
      {
        var cubeDef = def as MyCubeBlockDefinition;
        if (cubeDef == null || cubeDef.CubeSize != MyCubeSize.Large || mod._ignoreTypes.ContainsItem(cubeDef.Id.TypeId) || _invalidBlockDirInfo.ContainsKey(cubeDef.Id))
          continue;

        noPathBlocks.Add(def.Id);
      }

      Config.WriteBinaryFileToWorldStorage("NoPathBlocks.dat", this.GetType(), noPathBlocks.ToList(), _logger);
    }

    public List<MyDefinitionId> GetSpecialsOnly()
    {
      var hash = new HashSet<MyDefinitionId>(50);

      foreach (var kvp in _invalidBlockDirInfo)
      {
        var def = kvp.Key;
        var dict = kvp.Value;

        foreach (var item in dict)
        {
          if (item.Value.Item4)
          {
            hash.Add(def);
            break;
          }
        }
      }

      return hash.ToList();
    }

    public void UpdateKnownBlocks()
    {
      _KnownBlocks.Clear();
      _KnownBlocks.UnionWith(NoPathBlocks);

      foreach (var kvp in BlockDirInfo)
      {
        _KnownBlocks.Add(kvp.Key.Item1);
      }
    }

    public void InitBlockInfo()
    {
      //InitBlockInfo_Debug();
    }
  }
}
