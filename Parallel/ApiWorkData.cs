using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ParallelTasks;

using Sandbox.Game.Entities;

using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Parallel
{
  public class ApiWorkData : WorkData
  {
    public MyCubeGrid Grid;
    public int EnclosureRating;
    public List<Vector3I> NodeList;
    public bool AirtightNodesOnly;
    public bool AllowAirNodes;
    public Action CallBack;

    public ApiWorkData() { }

    public ApiWorkData(MyCubeGrid grid, List<Vector3I> nodeList, int enclosureRating, bool airtightOnly, bool allowAirNodes, Action callback)
    {
      Grid = grid;
      NodeList = nodeList;
      EnclosureRating = enclosureRating;
      AirtightNodesOnly = airtightOnly;
      AllowAirNodes = allowAirNodes;
      CallBack = callback;
    }
  }
}
