using AiEnabled.Input.Support;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage;

namespace AiEnabled.ConfigData
{
  public class PlayerData
  {
    public bool ShowHealthBars = true;
    public float MouseSensitivityModifier = 1f;
    public SerializableVector3? RepairBotIgnoreColorHSV = null;
    public List<SerializableKeybind> Keybinds = new List<SerializableKeybind>();
  }
}
