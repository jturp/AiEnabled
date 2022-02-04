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
    public float MouseSensitivityModifier = 1f;
    public SerializableVector3? RepairBotIgnoreColorHSV = null;
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

    [XmlElement("Waypoint", typeof(SerializableVector3D))]
    public List<SerializableVector3D> Waypoints;

    public SerializableRoute() { }

    public SerializableRoute(string name, List<Vector3D> points)
    {
      World = MyAPIGateway.Session.Name;
      Name = name;
      Waypoints = new List<SerializableVector3D>();

      for (int i = 0; i < points.Count; i++)
        Waypoints.Add(points[i]);
    }

    public SerializableRoute(string name, List<SerializableVector3D> points)
    {
      World = MyAPIGateway.Session.Name;
      Name = name;
      Waypoints = new List<SerializableVector3D>(points);
    }
  }
}
