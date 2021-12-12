using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ParallelTasks;

using VRageMath;

namespace AiEnabled.Parallel
{
  public class GraphWorkData : WorkData
  {
    public bool Force;
    public Vector3D TargetPosition;

    public GraphWorkData() { }

    public GraphWorkData(Vector3D tgtPosition, bool force = false)
    {
      TargetPosition = tgtPosition;
      Force = force;
    }
  }
}
