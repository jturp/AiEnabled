using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRageMath;

namespace AiEnabled.Support
{
  public class RepairDelay
  {
    readonly int _maxTicks;
    ConcurrentDictionary<long, ConcurrentDictionary<Vector3I, int>> _delays = new ConcurrentDictionary<long, ConcurrentDictionary<Vector3I, int>>();

    public RepairDelay(int maxTicks = 300)
    {
      _maxTicks = maxTicks;
    }

    public void Close()
    {
      _delays?.Clear();
      _delays = null;
    }

    public void AddDelay(long gridId, Vector3I blockPosition)
    {
      ConcurrentDictionary<Vector3I, int> gridDict;
      if (!_delays.TryGetValue(gridId, out gridDict))
      {
        gridDict = new ConcurrentDictionary<Vector3I, int>(Vector3I.Comparer);
        _delays[gridId] = gridDict;
      }

      gridDict[blockPosition] = 0;
    }

    public void Update()
    {
      foreach (var gridDict in _delays.Values)
      {
        foreach (var kvp in gridDict)
        {
          var newTick = kvp.Value + 1;

          if (newTick > _maxTicks)
            gridDict.TryRemove(kvp.Key, out newTick);
          else
            gridDict[kvp.Key] = newTick;
        }
      }
    }

    public bool Contains(long gridId, Vector3I blockPosition)
    {
      ConcurrentDictionary<Vector3I, int> gridDict;
      return _delays.TryGetValue(gridId, out gridDict) && gridDict?.ContainsKey(blockPosition) == true;
    }
  }
}
