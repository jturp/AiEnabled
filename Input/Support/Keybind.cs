using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Input;

namespace AiEnabled.Input.Support
{
  public struct Keybind
  {
    public MyKeys Key;
    public bool Ctrl, Alt, Shift;
    public bool HasUIKey;
    public Action Action;
    public void Invoke() => this.Action?.Invoke();

    public Keybind(MyKeys key, bool shift, bool ctrl, bool alt, Action action)
    {
      this.Action = action;
      Key = key;
      Ctrl = ctrl;
      Alt = alt;
      Shift = shift;

      HasUIKey = ctrl || alt || shift;
    }

    public void Close()
    {
      Action = null;
    }

    public bool IsPressed(CurrentKeys keys)
    {
      if (Key == MyKeys.None)
        return false;

      return MyAPIGateway.Input.IsNewKeyPressed(Key)
        && Ctrl == keys.AnyCtrlPressed
        && Alt == keys.AnyAltPressed
        && Shift == keys.AnyShiftPressed;
    }

    public bool IsUIKeyPressed(CurrentKeys keys)
    {
      return Ctrl == keys.AnyCtrlPressed
        && Alt == keys.AnyAltPressed
        && Shift == keys.AnyShiftPressed;
    }

    public MyTuple<MyKeys, bool, bool, bool> GetKeybind()
    {
      return MyTuple.Create(Key, Ctrl, Alt, Shift);
    }
  }
}