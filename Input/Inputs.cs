using ParallelTasks;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;
using Sandbox.Game;
using VRage;
using VRage.Input;
using VRage.Game.ModAPI;
using VRage.Utils;
using AiEnabled.Input.Support;
using AiEnabled.Utilities;

namespace AiEnabled.Input
{
  public class Inputs
  {
    AiSession _instance;
    List<Keybind> _keybinds;
    List<SerializableKeybind> _serialKeybinds;
    Dictionary<string, Action> _stringToAction;
    CurrentKeys _currentKeys = new CurrentKeys();

    bool _uiKeyPressed;
    //bool _mouseMoved;

    public Inputs(List<SerializableKeybind> keybinds)
    {
      _instance = AiSession.Instance;
      _keybinds = new List<Keybind>();
      _serialKeybinds = keybinds;

      _stringToAction = new Dictionary<string, Action>()
      {
        { "RecallBots_Used", _instance.PlayerMenu.RecallBots_Used }
      };

      Configure();
    }

    public void Configure()
    {
      if (_serialKeybinds == null)
      {
        _serialKeybinds = new List<SerializableKeybind>();
        return;
      }

      for (int i = 0; i < _serialKeybinds.Count; i++)
      {
        var bind = _serialKeybinds[i];
        Action method;
        var name = bind.Action;
        if (_stringToAction.TryGetValue(name, out method))
        {
          if (name.StartsWith("RecallBots"))
          {
            var text = $"Recall Bots: <color=green>{(bind.Ctrl ? "CTRL+" : "")}{(bind.Alt ? "ALT+" : "")}{(bind.Shift ? "SHIFT+" : "")}{bind.Key}";
            _instance.PlayerMenu.RecallBotsKeyBind.Text = text;
          }

          _keybinds.Add(new Keybind(bind.Key, bind.Shift, bind.Ctrl, bind.Alt, method));
        }
        else
          _instance.Logger.Log($"Inputs.Configure: Key '{bind.Action}' not found in action dictionary!", MessageType.WARNING);
      }
    }

    public void Close()
    {
      if (_keybinds != null)
      {
        for (int i = _keybinds.Count - 1; i >= 0; i--)
        {
          _keybinds[i].Close();
          _keybinds.RemoveAtFast(i);
        }
        _keybinds.Clear();
        _keybinds = null;
      }

      _stringToAction?.Clear();
      _serialKeybinds?.Clear();
      _serialKeybinds = null;
      _stringToAction = null;
    }

    void UpdateCurrentKeys()
    {
      _currentKeys.MousePositionDelta = MyAPIGateway.Input.GetCursorPositionDelta();
      _currentKeys.MouseScrollDelta = MyAPIGateway.Input.DeltaMouseScrollWheelValue();
      _currentKeys.NewLeftMousePressed = MyAPIGateway.Input.IsNewLeftMousePressed();
      _currentKeys.NewRightMousePressed = MyAPIGateway.Input.IsNewRightMousePressed();
      _currentKeys.AnyCtrlPressed = MyAPIGateway.Input.IsAnyCtrlKeyPressed();
      _currentKeys.AnyAltPressed = MyAPIGateway.Input.IsAnyAltKeyPressed();
      _currentKeys.AnyShiftPressed = MyAPIGateway.Input.IsAnyShiftKeyPressed();
    }

    public bool CheckKeys()
    {
      if (MyParticlesManager.Paused || MyAPIGateway.Gui.IsCursorVisible || MyAPIGateway.Gui.ChatEntryVisible || !MyAPIGateway.Input.IsAnyKeyPress())
      {
        if (_uiKeyPressed)
        {
          _uiKeyPressed = false;
          UpdateBlacklist(true);
        }

        return false;
      }

      UpdateCurrentKeys();

      bool updateUIKey = false;
      for (int i = 0; i < _keybinds.Count; i++)
      {
        var bind = _keybinds[i];
        if (bind.HasUIKey && bind.IsUIKeyPressed(_currentKeys))
        {
          updateUIKey = true;
          break;
        }
      }

      if (updateUIKey)
      {
        if (!_uiKeyPressed)
        {
          _uiKeyPressed = true;
          UpdateBlacklist(false);
        }
      }
      else if (_uiKeyPressed)
      {
        _uiKeyPressed = false;
        UpdateBlacklist(true);
      }

      for (int i = 0; i < _keybinds.Count; i++)
      {
        var bind = _keybinds[i];
        if (bind.IsPressed(_currentKeys))
        {
          bind.Invoke();
          return true;
        }
      }

      return false;
    }

    public void AddKeybind(MyTuple<MyKeys, bool, bool, bool> tuple, Action action)
    {
      for (int i = _keybinds.Count - 1; i >= 0; i--)
      {
        var bind = _keybinds[i];
        if (action.Method.Name == bind.Action.Method.Name)
        {
          if (IsSameTuple(bind.GetKeybind(), tuple))
            return;

          bind.Close();
          _keybinds.RemoveAtFast(i);
        }
      }

      var kb = new Keybind(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, action);
      _keybinds.Add(kb);

      _instance.PlayerData.Keybinds = CreateSerializableKeybinds();
      _instance.StartUpdateCounter();
    }

    bool IsSameTuple(MyTuple<MyKeys, bool, bool, bool> tuple1, MyTuple<MyKeys, bool, bool, bool> tuple2)
    {
      return 
        tuple1.Item1 == tuple2.Item1 &&
        tuple1.Item2 == tuple2.Item2 &&
        tuple1.Item3 == tuple2.Item3 &&
        tuple1.Item4 == tuple2.Item4;
    }

    void UpdateBlacklist(bool enable)
    {
      for (int i = 0; i < _keybinds.Count; i++)
      {
        var bind = _keybinds[i];
        var controlString = MyAPIGateway.Input.GetControl(bind.Key)?.GetGameControlEnum().String;
        if (controlString == null)
          continue;

        MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(controlString, MyAPIGateway.Session.Player.IdentityId, enable);
      }
    }

    List<SerializableKeybind> CreateSerializableKeybinds()
    {
      _serialKeybinds.Clear();

      for (int i = 0; i < _keybinds.Count; i++)
      {
        var bind = _keybinds[i];
        var name = bind.Action.Method.Name;
        _serialKeybinds.Add(new SerializableKeybind(bind.Key, bind.Shift, bind.Ctrl, bind.Alt, name));
      }

      return _serialKeybinds;
    }
  }
}
