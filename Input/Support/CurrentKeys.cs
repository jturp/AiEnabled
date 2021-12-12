using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRageMath;

namespace AiEnabled.Input.Support
{
  public class CurrentKeys
  {
    public bool NewRightMousePressed;
    public bool NewLeftMousePressed;
    public bool AnyCtrlPressed;
    public bool AnyAltPressed;
    public bool AnyShiftPressed;

    public Vector2 MousePositionDelta;
    public int MouseScrollDelta;
  }
}
