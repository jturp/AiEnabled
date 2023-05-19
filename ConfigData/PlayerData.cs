using AiEnabled.Graphics;
using AiEnabled.Input.Support;

using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

using VRage;

using VRageMath;

namespace AiEnabled.ConfigData
{
  public class PlayerData
  {
    public bool ShowHealthBars = true;
    public bool ShowHealthWhenFull = false;
    public bool ShowHelperGPS = true;
    public bool HighlightHelpersOnMouseOver = true;
    public bool ShowMapIconFriendly = true;
    public bool ShowMapIconNonFriendly = false;
    public bool NotifyOnHelperDeath = true;
    public float MouseSensitivityModifier = 1f;
    public float RepairBotSearchRadius = 0f;
    public float FollowDistance = 7.5f;
    public SerializableVector3? RepairBotIgnoreColorHSV = null;
    public SerializableVector3? RepairBotGrindColorHSV = null;
    public SerializableVector3I HelperGpsColorRGB = new Vector3I(117, 201, 241);
    public List<SerializableKeybind> Keybinds = new List<SerializableKeybind>();
    public List<SerializableRoute> PatrolRoutes = new List<SerializableRoute>();
  }

  [XmlType("Route")]
  public class SerializableRoute
  {
    [XmlAttribute("World")]
    public string World;

    [XmlAttribute("Name")]
    public string Name;

    public long? GridEntityId;

    [XmlElement("VoxelWaypoint", typeof(SerializableVector3D))]
    public List<SerializableVector3D> WaypointsWorld;

    [XmlElement("GridWaypoint", typeof(SerializableVector3I))]
    public List<SerializableVector3I> WaypointsLocal;

    public SerializableRoute() { }

    public SerializableRoute(string name, List<Vector3D> pointsWorld, List<Vector3I> pointsLocal, long? gridId = null)
    {
      World = MyAPIGateway.Session.Name;
      Name = name;
      WaypointsWorld = new List<SerializableVector3D>();
      WaypointsLocal = new List<SerializableVector3I>();

      if (pointsWorld?.Count > 0)
      {
        for (int i = 0; i < pointsWorld.Count; i++)
          WaypointsWorld.Add(pointsWorld[i]);
      }
      else if (pointsLocal?.Count > 0)
      {
        GridEntityId = gridId;

        for (int i = 0; i < pointsLocal.Count; i++)
          WaypointsLocal.Add(pointsLocal[i]);
      }
    }

    public SerializableRoute(string name, List<SerializableVector3D> pointsWorld, List<SerializableVector3I> pointsLocal)
    {
      World = MyAPIGateway.Session.Name;
      Name = name;
      WaypointsWorld = new List<SerializableVector3D>(pointsWorld);
      WaypointsLocal = new List<SerializableVector3I>(pointsLocal);
    }
  }
}
