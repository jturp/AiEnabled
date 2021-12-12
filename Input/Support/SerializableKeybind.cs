using VRage.Input;

namespace AiEnabled.Input.Support
{
  public struct SerializableKeybind
  {
    public MyKeys Key;
    public bool Shift, Ctrl, Alt;
    public string Action;

    public SerializableKeybind(MyKeys key, bool shift, bool ctrl, bool alt, string action)
    {
      Action = action;
      Key = key;
      Shift = shift;
      Ctrl = ctrl;
      Alt = alt;
    }
  }
}