using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ParallelTasks;

using VRageMath;

namespace AiEnabled.Parallel
{
  public class PathWorkData : WorkData
  {
    public Vector3I PathStart;
    public Vector3I PathEnd;
    public bool IsIntendedGoal;

    public PathWorkData() { }

    public PathWorkData(Vector3I start, Vector3I end, bool isIntended)
    {
      PathStart = start;
      PathEnd = end;
      IsIntendedGoal = isIntended;
    }
  }
}
