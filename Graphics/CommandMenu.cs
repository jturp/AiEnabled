using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.ModAPI;
using AiEnabled.Utilities;
using AiEnabled.Ai.Support;
using AiEnabled.Ai;
using AiEnabled.Networking;
using AiEnabled.Support;
using VRageMath;
using VRage.Game.Entity.UseObject;
using VRage.Game.Entity;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;
using Task = ParallelTasks.Task;
using VRage.Utils;
using VRage.ModAPI;
using Sandbox.Common.ObjectBuilders;
using VRage.Game;
using Sandbox.Game.Components;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using VRage.ObjectBuilders;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Bots.Roles.Helpers;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using AiEnabled.Graphics.Support;
using VRage.Input;
using VRage;
using AiEnabled.Bots;
using VRage.Voxels;
using AiEnabled.API;
using AiEnabled.ConfigData;

namespace AiEnabled.Graphics
{
  public class CommandMenu
  {
    public enum Quadrant { None, Top, TopLeft, TopRight, Bottom, BottomLeft, BottomRight }

    public bool ShowInventory { get; private set; }
    public bool ShowPatrol { get; private set; }
    public bool SendTo { get; private set; }
    public bool PatrolTo { get; private set; }
    public bool Registered { get; private set; }
    public bool RadialVisible { get; private set; }
    public bool InteractVisible { get; private set; }
    public bool PatrolVisible { get; private set; }
    public double AspectRatio { get; private set; }
    public IMyControl UseControl { get; private set; }
    public Quadrant SelectedQuadrant { get; private set; } = Quadrant.None;

    //Vector2D _screenPX;
    Vector2 _cursorPosition = new Vector2(0, 50);

    HudAPIv2.MenuTextInput _nameInput, _renameInput;
    HudAPIv2.BillBoardHUDMessage _interactBB, _radialArrow, _invBackground, _invBgBorder, _cursor;
    HudAPIv2.BillBoardHUDMessage _radialBracket1, _radialBracket2;
    HudAPIv2.BillBoardHUDMessage _radialBBTop, _radialBBBottom, _radialBBTopLeft, _radialBBTopRight, _radialBBBottomLeft, _radialBBBottomRight;
    HudAPIv2.HUDMessage _interactMsg;
    HudAPIv2.HUDMessage _radialMsgTop, _radialMsgTopLeft, _radialMsgTopRight, _radialMsgBottom, _radialMsgBottomLeft, _radialMsgBottomRight;
    Button _invAddToBot, _invAddToPlayer, _invClose, _moveButton, _equipWeaponBtn;
    TextBox _logoBox;
    PatrolMenu _patrolMenu;
    MyEntity3DSoundEmitter _emitter;
    MySoundPair _mouseOverSoundPair, _hudClickSoundPair, _errorSoundPair, _moveItemSoundPair;
    List<MyMouseButtonsEnum> _mouseButtonList;
    string _lastControlString;
    MyStringId _material_square = MyStringId.GetOrCompute("Square");
    MyStringId _material_cursor = MyStringId.GetOrCompute("AiEnabled_Cursor");

    Vector4 _billboardColor, _highlightColor, _radialColor;
    Vector2D _minCursorPosition, _maxCursorPosition;
    List<Vector3D> _patrolList = new List<Vector3D>(10);

    public CommandMenu()
    {
      _emitter = new MyEntity3DSoundEmitter(null);
      _mouseOverSoundPair = new MySoundPair("HudMouseOver");
      _hudClickSoundPair = new MySoundPair("HudClick");
      _errorSoundPair = new MySoundPair("HudUnable");
      _moveItemSoundPair = new MySoundPair("PlayDropItem");

      _mouseButtonList = new List<MyMouseButtonsEnum>(3)
      {
        MyMouseButtonsEnum.Left,
        MyMouseButtonsEnum.Middle,
        MyMouseButtonsEnum.Right,
      };
    }

    void OnRouteName_Submitted(string name)
    {
      if (!Registered)
        return;

      var pkt = new CommandPacket(ActiveBot.EntityId, patrol: true, patrolList: _patrolList);
      AiSession.Instance.Network.SendToServer(pkt);
      AiSession.Instance.ShowMessage($"Patrol starting with {_patrolList.Count} waypoints", MyFontEnum.Debug);

      _patrolMenu.RouteBox.AddRoute(_patrolList, name);
      AiSession.Instance.UpdateConfig(true);
    }

    void OnRouteRename_Submitted(string name)
    {
      if (!Registered)
        return;

      var ratio = AspectRatio;
      _patrolMenu.RenameRoute(name, ref ratio);
      AiSession.Instance.UpdateConfig(true);
    }

    public void AddPatrolRoutes(List<SerializableRoute> routeList)
    {
      _patrolMenu.RouteBox.SetStoredRoutes(routeList);
    }

    public void GetPatrolRoutes(List<SerializableRoute> routeList)
    {
      _patrolMenu.RouteBox.GetStoredRoutes(routeList);
    }

    public void DrawRadialMenu()
    {
      try
      {
        if (AiSession.Instance?.Registered != true || !AiSession.Instance.HudAPI.Heartbeat)
          return;

        if (!Registered)
        {
          if (!Init())
            return;

          Registered = true;
        }

        if (!RadialVisible)
        {
          if (!UpdateBlacklist(false))
          {
            UpdateBlacklist(true);
            return;
          }

          SetRadialVisibility(true);
          RadialVisible = true;
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CommandMenu.DrawRadialMenu: {ex.Message}\n{ex.StackTrace}");
      }
    }

    public void DrawInteractMessage(IMyCharacter bot)
    {
      try
      {
        if (AiSession.Instance?.Registered != true || !AiSession.Instance.HudAPI.Heartbeat)
          return;

        if (!Registered)
        {
          if (!Init())
            return;

          Registered = true;
        }

        if (bot?.MarkedForClose != false || bot.IsDead)
        {
          if (InteractVisible)
          {
            _interactBB.Visible = false;
            _interactMsg.Visible = false;
            InteractVisible = false;
          }

          return;
        }

        if (!InteractVisible)
        {
          var control = MyAPIGateway.Input.GetGameControl(MyControlsSpace.USE);
          string controlString;

          var key = control.GetKeyboardControl();
          if (key != MyKeys.None)
          {
            controlString = key.ToString();
          }
          else
          {
            key = control.GetSecondKeyboardControl();
            if (key != MyKeys.None)
            {
              controlString = key.ToString();
            }
            else
            {
              var btn = control.GetMouseControl();
              switch (btn)
              {
                case MyMouseButtonsEnum.Left:
                  controlString = "LMB";
                  break;
                case MyMouseButtonsEnum.Right:
                  controlString = "RMB";
                  break;
                case MyMouseButtonsEnum.Middle:
                  controlString = "MMB";
                  break;
                case MyMouseButtonsEnum.XButton1:
                  controlString = "MXB1";
                  break;
                case MyMouseButtonsEnum.XButton2:
                  controlString = "MXB2";
                  break;
                default:
                  controlString = "F";
                  break;
              }
            }
          }

          if (_lastControlString != controlString)
          {
            UseControl = control;
            _lastControlString = controlString;
            _interactMsg.Message.Clear().Append($"Press '{controlString}'\nto interact");

            var length = _interactMsg.GetTextLength();
            _interactMsg.Offset = length * 0.5;
            _interactBB.Width = (float)(length.X / AspectRatio * 1.25);
            _interactBB.Height = (float)(length.Y * 1.25);
            _interactBB.Offset = _interactMsg.Offset + _interactMsg.GetTextLength() * 0.5;
          }

          InteractVisible = true;
          _interactBB.Visible = true;
          _interactMsg.Visible = true;
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CommandMenu.DrawInteractMessage: {ex.Message}\n{ex.StackTrace}");
      }
    }

    public void PlayHudClick() => PlaySound(_hudClickSoundPair);

    void PlaySound(MySoundPair sp)
    {
      var obj = MyAPIGateway.Session?.ControlledObject?.Entity as MyEntity;
      if (obj == null || _emitter == null)
        return;

      _emitter.Entity = obj;
      _emitter.PlaySound(sp);
    }

    bool _allKeysAllows = true;
    public bool UpdateBlacklist(bool enable)
    {
      try
      {
        if (!Registered || MyAPIGateway.Session?.Player == null || _allKeysAllows == enable)
          return false;

        var identityId = MyAPIGateway.Session.Player.IdentityId;
        var controlString = MyControlsSpace.USE.String;

        if (controlString != null)
          MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(controlString, identityId, enable);

        controlString = MyControlsSpace.PRIMARY_TOOL_ACTION.String;

        if (controlString != null)
          MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(controlString, identityId, enable);

        controlString = MyControlsSpace.SECONDARY_TOOL_ACTION.String;

        if (controlString != null)
          MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(controlString, identityId, enable);

        foreach (var btn in _mouseButtonList)
        {
          controlString = MyAPIGateway.Input?.GetControl(btn)?.GetGameControlEnum().String;

          if (controlString != null)
            MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(controlString, identityId, enable);
        }

        for (int i = 48; i < 58; i++)
        {
          var key = (MyKeys)i;
          controlString = MyAPIGateway.Input?.GetControl(key)?.GetGameControlEnum().String;

          if (controlString != null)
            MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(controlString, identityId, enable);
        }

        controlString = MyAPIGateway.Input?.GetControl(MyKeys.OemTilde)?.GetGameControlEnum().String;

        if (controlString != null)
          MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(controlString, identityId, enable);

        _allKeysAllows = enable;
        return true;
      }
      catch (Exception e)
      {
        AiSession.Instance.Logger.Log($"Exception in UpdateBlackList: {e.Message}\n{e.StackTrace}", MessageType.ERROR);
        return false;
      }
    }

    public void Close()
    {
      try
      {
        if (!Registered)
          return;

        SetRadialVisibility(false);
        SetInventoryScreenVisibility(false);

        Registered = false;

        if (_interactBB != null)
          _interactBB.Visible = false;

        if (_interactMsg != null)
          _interactMsg.Visible = false;

        if (_nameInput != null)
        {
          _nameInput.Text = null;
          _nameInput.OnSubmitAction = null;
          _nameInput.BackingObject = null;
          _nameInput = null;
        }

        if (_renameInput != null)
        {
          _renameInput.Text = null;
          _renameInput.OnSubmitAction = null;
          _renameInput.BackingObject = null;
          _renameInput = null;
        }

        _patrolList?.Clear();
        _emitter?.Cleanup();
        _mouseButtonList?.Clear();
        _invItems?.Clear();
        _radialArrow?.DeleteMessage();
        _interactBB?.DeleteMessage();
        _radialBracket1?.DeleteMessage();
        _radialBracket2?.DeleteMessage();
        _radialBBBottom?.DeleteMessage();
        _radialBBBottomLeft?.DeleteMessage();
        _radialBBBottomRight?.DeleteMessage();
        _radialBBTop?.DeleteMessage();
        _radialBBTopLeft?.DeleteMessage();
        _radialBBTopRight?.DeleteMessage();
        _radialMsgBottom?.DeleteMessage();
        _radialMsgBottomLeft?.DeleteMessage();
        _radialMsgBottomRight?.DeleteMessage();
        _radialMsgTop?.DeleteMessage();
        _radialMsgTopLeft?.DeleteMessage();
        _radialMsgTopRight?.DeleteMessage();
        
        _logoBox?.Close();
        _invAddToBot?.Close();
        _invAddToPlayer?.Close();
        _invClose?.Close();
        _playerInvBox?.Close();
        _botInvBox?.Close();
        _equipWeaponBtn?.Close();

        _patrolList = null;
        _hudClickSoundPair = null;
        _mouseOverSoundPair = null;
        _errorSoundPair = null;
        _moveItemSoundPair = null;
        _invItems = null;
        _emitter = null;
        _mouseButtonList = null;

        AiSession.Instance.HudAPI.OnScreenDimensionsChanged = null;
        MyAPIGateway.Gui.GuiControlRemoved -= GuiControlRemoved;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CommandMenu.Close: {ex.Message}\n{ex.StackTrace}");
      }
    }

    public void CloseMenu()
    {
      try
      {
        if (!Registered)
          return;

        SelectedQuadrant = Quadrant.None;
        _radialBBTop.BillBoardColor = _radialColor;
        _radialBBTopLeft.BillBoardColor = _radialColor;
        _radialBBTopRight.BillBoardColor = _radialColor;
        _radialBBBottom.BillBoardColor = _radialColor;
        _radialBBBottomLeft.BillBoardColor = _radialColor;
        _radialBBBottomRight.BillBoardColor = _radialColor;
        _radialMsgTop.InitialColor = Color.White;
        _radialMsgTopLeft.InitialColor = Color.White;
        _radialMsgTopRight.InitialColor = Color.White;
        _radialMsgBottom.InitialColor = Color.White;
        _radialMsgBottomLeft.InitialColor = Color.White;
        _radialMsgBottomRight.InitialColor = Color.White;

        SetRadialVisibility(false);

        if (!ShowInventory && !ShowPatrol && !SendTo && !PatrolTo)
        {
          UpdateBlacklist(true);
        }

        RadialVisible = false;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CommandMenu.CloseMenu: {ex.Message}\n{ex.StackTrace}");
      }
    }

    public void CloseInteractMessage()
    {
      try
      {
        if (!Registered)
          return;

        _interactBB.Visible = false;
        _interactMsg.Visible = false;
        InteractVisible = false;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CommandMenu.CloseInteractMessage: {ex.Message}\n{ex.StackTrace}");
      }
    }

    public void ActivatePatrol()
    {
      if (ActiveBot?.MarkedForClose != false || ActiveBot.IsDead)
      {
        SetPatrolMenuVisibility(false);
        return;
      }

      _patrolList.Clear();
      var ratio = AspectRatio;
      bool newPatrol, closeMenu, renamePatrol;
      _patrolMenu.TryActivate(ref ratio, _patrolList, out newPatrol, out closeMenu, out renamePatrol);

      if (closeMenu || newPatrol)
      {
        ShowPatrol = false;
        PatrolTo = newPatrol;
        SetPatrolMenuVisibility(false);
      }
      else if (renamePatrol)
      {
        // TODO: Uncomment when Text Hud API is updated
        //_renameInput?.OpenDialog();
      }
      else if (_patrolList.Count > 0)
      {
        var pkt = new CommandPacket(ActiveBot.EntityId, patrol: true, patrolList: _patrolList);
        AiSession.Instance.Network.SendToServer(pkt);

        PatrolTo = false;
        SetPatrolMenuVisibility(false);
        AiSession.Instance.ShowMessage($"Patrol starting with {_patrolList.Count} waypoints", MyFontEnum.Debug);
      }
    }

    public void DrawPatrolMenu()
    {
      if (ActiveBot?.MarkedForClose != false || ActiveBot.IsDead)
      {
        SetPatrolMenuVisibility(false);
        return;
      }

      if (!PatrolVisible)
      {
        SetPatrolMenuVisibility(true);
        PatrolVisible = true;
      }
    }

    public void SetPatrolMenuVisibility(bool show)
    {
      ShowPatrol = show;

      if (_cursor != null)
        _cursor.Visible = show;

      _patrolMenu?.SetVisibility(show);

      if (!show)
      {
        UpdateBlacklist(true);
      }
      else
      {
        var ratio = AspectRatio;
        bool newCursorNeeded;
        _patrolMenu.RouteBox.Update(ratio, out newCursorNeeded);

        if (newCursorNeeded)
        {
          var position = _cursor.Origin;
          var offset = _cursor.Offset;
          _cursor?.DeleteMessage();

          _cursor = new HudAPIv2.BillBoardHUDMessage(
            Material: _material_cursor,
            Origin: position,
            Offset: offset,
            BillBoardColor: Color.White,
            Width: 0.09f,
            Height: 0.09f,
            Blend: BlendTypeEnum.PostPP);

          _cursor.Options |= HudAPIv2.Options.Fixed | HudAPIv2.Options.FOVScale;
          _cursor.Visible = true;
        }
      }
    }

    void SetRadialVisibility(bool show)
    {
      _radialBracket1.Visible = show;
      _radialBracket2.Visible = show;
      _radialArrow.Visible = show;
      _radialBBBottom.Visible = show;
      _radialBBBottomLeft.Visible = show;
      _radialBBBottomRight.Visible = show;
      _radialBBTop.Visible = show;
      _radialBBTopLeft.Visible = show;
      _radialBBTopRight.Visible = show;
      _radialMsgBottom.Visible = show;
      _radialMsgBottomLeft.Visible = show;
      _radialMsgBottomRight.Visible = show;
      _radialMsgTop.Visible = show;
      _radialMsgTopLeft.Visible = show;
      _radialMsgTopRight.Visible = show;

      if (show)
      {
        ActiveBot = null;
        _worldPosition = null;
      }
    }

    public void UpdateCursorDirection(Vector2 movement)
    {
      _cursorPosition = Vector2.ClampToSphere(_cursorPosition + movement, 50);

      var vector = new Vector3D(_cursorPosition.X, -_cursorPosition.Y, 0);
      var angle = (float)VectorUtils.GetAngleBetween(Vector3D.Up, vector);
      if (vector.X != 0)
        angle *= Math.Sign(vector.X);

      var angleDegrees = MathHelper.ToDegrees(angle);

      if (angleDegrees >= -30 && angleDegrees < 30)
      {
        if (SelectedQuadrant != Quadrant.Top)
        {
          SelectedQuadrant = Quadrant.Top;
          PlaySound(_mouseOverSoundPair);

          // set colors
          _radialBBTop.BillBoardColor = _highlightColor;
          _radialMsgTop.InitialColor = Color.Black;

          _radialBBTopLeft.BillBoardColor = _radialColor;
          _radialMsgTopLeft.InitialColor = Color.White;

          _radialBBTopRight.BillBoardColor = _radialColor;
          _radialMsgTopRight.InitialColor = Color.White;

          _radialBBBottom.BillBoardColor = _radialColor;
          _radialMsgBottom.InitialColor = Color.White;

          _radialBBBottomLeft.BillBoardColor = _radialColor;
          _radialMsgBottomLeft.InitialColor = Color.White;

          _radialBBBottomRight.BillBoardColor = _radialColor;
          _radialMsgBottomRight.InitialColor = Color.White;
        }
      }
      else if (angleDegrees >= 30 && angleDegrees < 90)
      {
        if (SelectedQuadrant != Quadrant.TopRight)
        {
          SelectedQuadrant = Quadrant.TopRight;
          PlaySound(_mouseOverSoundPair);

          // set colors
          _radialBBTop.BillBoardColor = _radialColor;
          _radialMsgTop.InitialColor = Color.White;

          _radialBBTopLeft.BillBoardColor = _radialColor;
          _radialMsgTopLeft.InitialColor = Color.White;

          _radialBBTopRight.BillBoardColor = _highlightColor;
          _radialMsgTopRight.InitialColor = Color.Black;

          _radialBBBottom.BillBoardColor = _radialColor;
          _radialMsgBottom.InitialColor = Color.White;

          _radialBBBottomLeft.BillBoardColor = _radialColor;
          _radialMsgBottomLeft.InitialColor = Color.White;

          _radialBBBottomRight.BillBoardColor = _radialColor;
          _radialMsgBottomRight.InitialColor = Color.White;
        }
      }
      else if (angleDegrees >= 90 && angleDegrees < 150)
      {
        if (SelectedQuadrant != Quadrant.BottomRight)
        {
          SelectedQuadrant = Quadrant.BottomRight;
          PlaySound(_mouseOverSoundPair);

          // set colors
          _radialBBTop.BillBoardColor = _radialColor;
          _radialMsgTop.InitialColor = Color.White;

          _radialBBTopLeft.BillBoardColor = _radialColor;
          _radialMsgTopLeft.InitialColor = Color.White;

          _radialBBTopRight.BillBoardColor = _radialColor;
          _radialMsgTopRight.InitialColor = Color.White;

          _radialBBBottom.BillBoardColor = _radialColor;
          _radialMsgBottom.InitialColor = Color.White;

          _radialBBBottomLeft.BillBoardColor = _radialColor;
          _radialMsgBottomLeft.InitialColor = Color.White;

          _radialBBBottomRight.BillBoardColor = _highlightColor;
          _radialMsgBottomRight.InitialColor = Color.Black;
        }
      }
      else if (angleDegrees >= 150 || angleDegrees < -150)
      {
        if (SelectedQuadrant != Quadrant.Bottom)
        {
          SelectedQuadrant = Quadrant.Bottom;
          PlaySound(_mouseOverSoundPair);

          // set colors
          _radialBBTop.BillBoardColor = _radialColor;
          _radialMsgTop.InitialColor = Color.White;

          _radialBBTopLeft.BillBoardColor = _radialColor;
          _radialMsgTopLeft.InitialColor = Color.White;

          _radialBBTopRight.BillBoardColor = _radialColor;
          _radialMsgTopRight.InitialColor = Color.White;

          _radialBBBottom.BillBoardColor = _highlightColor;
          _radialMsgBottom.InitialColor = Color.Black;

          _radialBBBottomLeft.BillBoardColor = _radialColor;
          _radialMsgBottomLeft.InitialColor = Color.White;

          _radialBBBottomRight.BillBoardColor = _radialColor;
          _radialMsgBottomRight.InitialColor = Color.White;
        }
      }
      else if (angleDegrees >= -150 && angleDegrees < -90)
      {
        if (SelectedQuadrant != Quadrant.BottomLeft)
        {
          SelectedQuadrant = Quadrant.BottomLeft;
          PlaySound(_mouseOverSoundPair);

          // set colors
          _radialBBTop.BillBoardColor = _radialColor;
          _radialMsgTop.InitialColor = Color.White;

          _radialBBTopLeft.BillBoardColor = _radialColor;
          _radialMsgTopLeft.InitialColor = Color.White;

          _radialBBTopRight.BillBoardColor = _radialColor;
          _radialMsgTopRight.InitialColor = Color.White;

          _radialBBBottom.BillBoardColor = _radialColor;
          _radialMsgBottom.InitialColor = Color.White;

          _radialBBBottomLeft.BillBoardColor = _highlightColor;
          _radialMsgBottomLeft.InitialColor = Color.Black;

          _radialBBBottomRight.BillBoardColor = _radialColor;
          _radialMsgBottomRight.InitialColor = Color.White;
        }
      }
      else if (SelectedQuadrant != Quadrant.TopLeft) // if (angleDegrees >= -90 && angleDegrees < -30)
      {
        SelectedQuadrant = Quadrant.TopLeft;
        PlaySound(_mouseOverSoundPair);

        // set colors
        _radialBBTop.BillBoardColor = _radialColor;
        _radialMsgTop.InitialColor = Color.White;

        _radialBBTopLeft.BillBoardColor = _highlightColor;
        _radialMsgTopLeft.InitialColor = Color.Black;

        _radialBBTopRight.BillBoardColor = _radialColor;
        _radialMsgTopRight.InitialColor = Color.White;

        _radialBBBottom.BillBoardColor = _radialColor;
        _radialMsgBottom.InitialColor = Color.White;

        _radialBBBottomLeft.BillBoardColor = _radialColor;
        _radialMsgBottomLeft.InitialColor = Color.White;

        _radialBBBottomRight.BillBoardColor = _radialColor;
        _radialMsgBottomRight.InitialColor = Color.White;
      }

      var vec = new Vector2D(vector.X, vector.Y);
      Vector2D dir = Vector2D.Normalize(vec - _radialArrow.Origin) * _radialArrow.Height * 0.25;

      _radialArrow.Rotation = angle;
      _radialArrow.Offset = dir * new Vector2D(AspectRatio, 1);
    }

    public void ResetCommands()
    {
      if (ShowInventory)
      {
        SetInventoryScreenVisibility(false);
      }

      if (ShowPatrol)
      {
        SetPatrolMenuVisibility(false);
      }

      SendTo = false;
      PatrolTo = false;
      ActiveBot = null;
      SelectedQuadrant = Quadrant.None;
      _worldPosition = null;
      _radialArrow.Offset = Vector2D.Zero;

      UpdateBlacklist(true);
    }

    public void Activate(IMyCharacter bot, bool patrolFinished = false)
    {
      CommandPacket pkt;

      if (SendTo)
      {
        if (_worldPosition.HasValue && ActiveBot != null)
        {
          PlaySound(_hudClickSoundPair);
          pkt = new CommandPacket(ActiveBot.EntityId, goTo: _worldPosition);
          AiSession.Instance.Network.SendToServer(pkt);
        }
        else
        {
          PlaySound(_errorSoundPair);
          return;
        }

        SendTo = false;
        ActiveBot = null;
        _worldPosition = null;

        UpdateBlacklist(true);
        return;
      }
      else if (PatrolTo)
      {
        MyAPIGateway.Utilities.ShowNotification($"Press [LMB] to add waypoints, [RMB] to finish route.", 32);

        if (patrolFinished)
        {
          if (_patrolList.Count > 0 && ActiveBot != null)
          {
            PlaySound(_hudClickSoundPair);
            var patrolName = $"Route for {ActiveBot.Name}";

            pkt = new CommandPacket(ActiveBot.EntityId, patrol: true, patrolList: _patrolList);
            AiSession.Instance.Network.SendToServer(pkt);
            AiSession.Instance.ShowMessage($"Patrol starting with {_patrolList.Count} waypoints", MyFontEnum.Debug);

            _patrolMenu.RouteBox.AddRoute(_patrolList, patrolName);
            AiSession.Instance.UpdateConfig(true);

            // TODO: show Name Route pop up -- waiting for Draygo to update API to allow this
            // Remove packet and AddRoute call when done
            //_nameInput.OpenDialog;
          }
          else
          {
            PlaySound(_errorSoundPair);
          }

          PatrolTo = false;
          ActiveBot = null;
          _patrolList.Clear();

          UpdateBlacklist(true);
        }
        else if (_worldPosition.HasValue && ActiveBot != null)
        {
          _patrolList.Add(_worldPosition.Value);
          PlaySound(_hudClickSoundPair);
          AiSession.Instance.ShowMessage($"Waypoint {_patrolList.Count} added to patrol queue", MyFontEnum.Debug);
        }
        else
        {
          PlaySound(_errorSoundPair);
          return;
        }

        return;
      }

      if (bot?.MarkedForClose != false || bot.IsDead)
      {
        SelectedQuadrant = Quadrant.None;
      }

      switch (SelectedQuadrant)
      {
        case Quadrant.None:
          ActiveBot = null;
          ShowInventory = false;
          ShowPatrol = false;
          SendTo = false;
          PatrolTo = false;
          _worldPosition = null;
          return;
        case Quadrant.Top:
          SendTo = false;
          PatrolTo = false;
          ShowInventory = true;
          ShowPatrol = false;
          ActiveBot = bot;
          _invRetrieved = false;
          break;
        case Quadrant.TopRight:
          ActiveBot = bot;
          ShowInventory = false;
          ShowPatrol = false;
          PatrolTo = false;
          SendTo = true;
          break;
        case Quadrant.BottomRight:
          ActiveBot = null;
          ShowInventory = false;
          ShowPatrol = false;
          PatrolTo = false;
          SendTo = false;
          pkt = new CommandPacket(bot.EntityId, stay: true);
          AiSession.Instance.Network.SendToServer(pkt);
          break;
        case Quadrant.BottomLeft:
          ActiveBot = null;
          ShowInventory = false;
          ShowPatrol = false;
          PatrolTo = false;
          SendTo = false;
          pkt = new CommandPacket(bot.EntityId, follow: true);
          AiSession.Instance.Network.SendToServer(pkt);
          break;
        case Quadrant.TopLeft:
          ActiveBot = bot;
          ShowInventory = false;
          ShowPatrol = true;
          PatrolTo = false;
          SendTo = false;
          _invRetrieved = false;
          PatrolVisible = false;
          _patrolList.Clear();
          break;
        case Quadrant.Bottom:
          ActiveBot = null;
          ShowInventory = false;
          ShowPatrol = false;
          PatrolTo = false;
          SendTo = false;
          pkt = new CommandPacket(bot.EntityId, resume: true);
          AiSession.Instance.Network.SendToServer(pkt);
          break;
        default:
          return;
      }

      PlaySound(_hudClickSoundPair);
    }

    public void ActivateInventory(ref bool LMBPressed, ref bool RMBPressed, ref bool LMBReleased, ref bool RMBReleased, ref bool LMBDoubleClick)
    {
      try
      {
        var botInv = ActiveBot?.GetInventory() as MyInventory;
        var playerInv = MyAPIGateway.Session?.Player?.Character?.GetInventory() as MyInventory;

        if (botInv == null || playerInv == null)
          return;

        if (_moveButton != null && (LMBReleased || (RMBReleased && !_equipWeaponBtn.IsVisible)))
        {
          bool revert = true;

          if (_changesComplete)
          {
            var cursorPos = _cursor.Origin + _cursor.Offset;
            if (_botInvBox.SelectedItem?.InventoryItem != null && cursorPos.IsWithinBounds(_playerInvBox.Border, AspectRatio))
            {
              // add to player
              MoveItem(botInv, playerInv, _botInvBox.SelectedItem.Value.InventoryItem);
              revert = false;
            }
            else if (_playerInvBox.SelectedItem?.InventoryItem != null && cursorPos.IsWithinBounds(_botInvBox.Border, AspectRatio))
            {
              // add to bot
              MoveItem(playerInv, botInv, _playerInvBox.SelectedItem.Value.InventoryItem);
              revert = false;
            }
          }
            
          if (revert)
          {
            // revert to original position and return;
            var box = _botInvBox.SelectedItem != null ? _botInvBox : _playerInvBox;
            box.ResetButtonPosition(_moveButton, AspectRatio);
            _moveButton = null;
            return;
          }

          PlaySound(_moveItemSoundPair);
          _invRetrieved = false;
          _moveButton = null;
          return;
        }

        if (!LMBPressed && !RMBPressed && (!_equipWeaponBtn.IsVisible || !_equipWeaponBtn.IsMouseOver))
        {
          _moveButton = null;
          return;
        }

        var newLMBPressed = MyAPIGateway.Input.IsNewLeftMousePressed();
        var newRMBPressed = !newLMBPressed && MyAPIGateway.Input.IsNewRightMousePressed();
        bool altPressed = MyAPIGateway.Input.IsAnyAltKeyPressed();
        bool ctrlPressed = MyAPIGateway.Input.IsAnyCtrlKeyPressed();
        bool shiftPressed = MyAPIGateway.Input.IsAnyShiftKeyPressed();
        bool modifierPressed = altPressed || ctrlPressed || shiftPressed;

        if (newLMBPressed && _equipWeaponBtn.IsVisible)
        {
          if (_equipWeaponBtn.IsMouseOver)
          {
            if (botInv != null)
            {
              if (_botInvBox.SelectedItem?.InventoryItem != null)
              {
                var pkt = new EquipWeaponPacket(botInv.Owner.EntityId, _botInvBox.SelectedItem.Value.InventoryItem.Content.GetId());
                AiSession.Instance.Network.SendToServer(pkt);

                _invRetrieved = false;
              }
              else if (_playerInvBox.SelectedItem?.InventoryItem != null)
              {
                MoveItem(playerInv, botInv, _playerInvBox.SelectedItem.Value.InventoryItem, 1, true);
                PlaySound(_moveItemSoundPair);
              }
            }
          }

          var val = false;
          _equipWeaponBtn.SetVisibility(ref val);
          _moveButton = null;
        }

        if (_moveButton == null && (newLMBPressed || newRMBPressed))
        {
          if (_playerInvBox.HasFocus)
          {
            PlaySound(_hudClickSoundPair);
            _botInvBox.ClearSelected();
            _moveButton = _playerInvBox.SetSelected();

            if (_moveButton != null)
            {
              if ((modifierPressed || LMBDoubleClick) && newLMBPressed && _changesComplete)
              {
                var selectedItem = _playerInvBox.SelectedItem;
                if (selectedItem?.InventoryItem != null)
                {
                  // add to bot
                  var item = selectedItem.Value.InventoryItem;

                  float num;
                  if (modifierPressed)
                  {
                    num = 1;

                    if (ctrlPressed)
                      num *= 10;

                    if (shiftPressed)
                      num *= 100;
                  }
                  else // double-click
                  {
                    PlaySound(_moveItemSoundPair);
                    num = (float)item.Amount;
                  }

                  num = Math.Min(num, (float)item.Amount);
                  MoveItem(playerInv, botInv, item, num);
                }

                _moveButton = null;
              }
              else if (newRMBPressed)
              {
                var selectedItem = _playerInvBox.SelectedItem;
                if (selectedItem?.InventoryItem != null)
                {
                  var content = selectedItem.Value.InventoryItem.Content;
                  if (content is MyObjectBuilder_PhysicalGunObject)
                  {
                    // give and equip 
                    _equipWeaponBtn.UpdateText("Give and Equip", AspectRatio, false, true);
                    var length = _equipWeaponBtn.Text.GetTextLength();
                    var topRt = _moveButton.Position + new Vector2D((_moveButton.Background.Width) * AspectRatio, _moveButton.Background.Height) * 0.5;
                    var pos = topRt + new Vector2D(0.005 * AspectRatio, length.Y * 1.25 * 0.5);
                    var ratio = AspectRatio;
                    var val = true;
                    _equipWeaponBtn.SetAbsolutePosition(ref pos, ref ratio, false);
                    _equipWeaponBtn.SetVisibility(ref val);
                  }
                  else if (content is MyObjectBuilder_ConsumableItem)
                  {
                    // give and use 
                    _equipWeaponBtn.UpdateText("Give and Use", AspectRatio, false, true);
                    var length = _equipWeaponBtn.Text.GetTextLength();
                    var topRt = _moveButton.Position + new Vector2D((_moveButton.Background.Width) * AspectRatio, _moveButton.Background.Height) * 0.5;
                    var pos = topRt + new Vector2D(0.005 * AspectRatio, length.Y * 1.25 * 0.5);
                    var ratio = AspectRatio;
                    var val = true;
                    _equipWeaponBtn.SetAbsolutePosition(ref pos, ref ratio, false);
                    _equipWeaponBtn.SetVisibility(ref val);
                  }
                }
              }
            }
          }
          else if (_botInvBox.HasFocus)
          {
            PlaySound(_hudClickSoundPair);
            _playerInvBox.ClearSelected();
            _moveButton = _botInvBox.SetSelected();

            if (_moveButton != null)
            {
              if ((modifierPressed || LMBDoubleClick) && newLMBPressed && _changesComplete)
              {
                var selectedItem = _botInvBox.SelectedItem;
                if (selectedItem?.InventoryItem != null)
                {
                  // add to player
                  var item = selectedItem.Value.InventoryItem;

                  float num;
                  if (modifierPressed)
                  {
                    num = 1;

                    if (ctrlPressed)
                      num *= 10;

                    if (shiftPressed)
                      num *= 100;
                  }
                  else // double-click
                  {
                    PlaySound(_moveItemSoundPair);
                    num = (float)item.Amount;
                  }

                  num = Math.Min(num, (float)item.Amount);
                  MoveItem(botInv, playerInv, item, num);
                }

                _moveButton = null;
              }
              else if (newRMBPressed)
              {
                var selectedItem = _botInvBox.SelectedItem;
                if (selectedItem?.InventoryItem != null)
                {
                  var content = selectedItem.Value.InventoryItem.Content;
                  if (content is MyObjectBuilder_PhysicalGunObject)
                  {
                    // just equip
                    _equipWeaponBtn.UpdateText("Equip", AspectRatio, false, true);
                    var length = _equipWeaponBtn.Text.GetTextLength();
                    var topRt = _moveButton.Position + new Vector2D((_moveButton.Background.Width) * AspectRatio, _moveButton.Background.Height) * 0.5;
                    var pos = topRt + new Vector2D(0.005 * AspectRatio, length.Y * 1.25 * 0.5);
                    var ratio = AspectRatio;
                    var val = true;
                    _equipWeaponBtn.SetAbsolutePosition(ref pos, ref ratio, false);
                    _equipWeaponBtn.SetVisibility(ref val);
                  }
                  else if (content is MyObjectBuilder_ConsumableItem)
                  {
                    // just use
                    _equipWeaponBtn.UpdateText("Use", AspectRatio, false, true);
                    var length = _equipWeaponBtn.Text.GetTextLength();
                    var topRt = _moveButton.Position + new Vector2D((_moveButton.Background.Width) * AspectRatio, _moveButton.Background.Height) * 0.5;
                    var pos = topRt + new Vector2D(0.005 * AspectRatio, length.Y * 1.25 * 0.5);
                    var ratio = AspectRatio;
                    var val = true;
                    _equipWeaponBtn.SetAbsolutePosition(ref pos, ref ratio, false);
                    _equipWeaponBtn.SetVisibility(ref val);
                  }
                }
              }
            }
          }
          else if (_invClose.IsMouseOver)
          {
            _moveButton = null;

            PlaySound(_hudClickSoundPair);
            SetInventoryScreenVisibility(false);
          }
          else if (_invAddToBot.IsMouseOver || _invAddToPlayer.IsMouseOver)
          {
            if (!_changesComplete)
              return;

            if (_invAddToBot.IsMouseOver)
            {
              var selected = _playerInvBox.SelectedItem;
              if (selected?.InventoryItem != null)
              {
                PlaySound(_moveItemSoundPair);
                MoveItem(playerInv, botInv, selected.Value.InventoryItem);
                _playerInvBox.SelectedItem = null;
              }
              else
                PlaySound(_errorSoundPair);
            }
            else // add to player
            {
              var selected = _botInvBox.SelectedItem;
              if (selected?.InventoryItem != null)
              {
                PlaySound(_moveItemSoundPair);
                MoveItem(botInv, playerInv, selected.Value.InventoryItem);
                _botInvBox.SelectedItem = null;
              }
              else
                PlaySound(_errorSoundPair);
            }
          }
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CommandMenu.ActivateInventory: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    void MoveItem(MyInventory fromInv, MyInventory toInv, IMyInventoryItem invItem, float numToGive = -1, bool equip = false)
    {
      var itemDef = invItem.Content.GetId();
      var amountThatFits = toInv.ComputeAmountThatFits(itemDef);
      var amount = amountThatFits;

      if (amount == 0)
      {
        PlaySound(_errorSoundPair);
        return;
      }

      if (numToGive > 0)
        amount = MyFixedPoint.Min(amount, (MyFixedPoint)numToGive);

      var item = fromInv.GetItemByID(invItem.ItemId);
      if (item != null)
      {
        amount = MyFixedPoint.Min(amount, invItem.Amount);

        if (amount > 0)
        {
          if (amount > 1 && (amountThatFits - amount) <= 1)
          {
            var physicalDef = MyDefinitionManager.Static.GetDefinition(itemDef) as MyPhysicalItemDefinition;
            if (physicalDef != null && (physicalDef.IsIngot || physicalDef.IsOre))
            {
              amount = MyFixedPoint.Floor(amount);
            }
            else if (amount - 1 > 1)
            {
              amount -= 1;
            }
          }

          _changesComplete = false;
          _changesNeeded = (equip && itemDef.TypeId == typeof(MyObjectBuilder_ConsumableItem)) ? 3 : 2;
          fromInv.InventoryContentChanged += OnInventoryContentChanged;
          toInv.InventoryContentChanged += OnInventoryContentChanged;
          var pkt = new InventoryUpdatePacket(fromInv.Owner.EntityId, toInv.Owner.EntityId, (double)amount, invItem.ItemId, equip);
          AiSession.Instance.Network.SendToServer(pkt);
        }
      }
    }

    public void ResetChanges()
    {
      _changesNeeded = 2;
      _changesReceived = 0;
      _changesComplete = true;
      _invRetrieved = false;
    }

    bool _changesComplete = true;
    int _changesReceived, _changesNeeded = 2;
    internal void OnInventoryContentChanged(MyInventoryBase inventory, MyPhysicalInventoryItem item, MyFixedPoint amount)
    {
      var inventoryOwner = inventory?.Entity;
      if (_changesNeeded == 2 || _changesReceived == 2 || inventoryOwner == null || inventoryOwner.EntityId != ActiveBot?.EntityId)
        inventory.InventoryContentChanged -= OnInventoryContentChanged;

      ++_changesReceived;
      if (_changesReceived >= _changesNeeded)
      {
        _changesReceived = 0;
        _changesComplete = true;
        _invRetrieved = false;
      }
    }

    Vector4 _invalidColor = new Color(255, 69, 0, 200).ToVector4();
    Vector4 _validColor = new Color(154, 205, 50, 200).ToVector4();
    Vector3D? _worldPosition;
    public IMyCharacter ActiveBot { get; private set; }
    public void DrawSendTo(IMyCharacter ch)
    {
      if (ch?.IsDead != false || ActiveBot?.MarkedForClose != false || ActiveBot.IsDead)
      {
        ActiveBot = null;
        SendTo = false;
        PatrolTo = false;
        return;
      }

      var headMatrix = ch.GetHeadMatrix(true);
      var from = headMatrix.Translation + ch.WorldMatrix.Forward * 0.25 + ch.WorldMatrix.Down * 0.25;
      var to = from + headMatrix.Forward * 20;

      IHitInfo hit;
      MyAPIGateway.Physics.CastRay(from, to, out hit, CollisionLayers.CharacterCollisionLayer);
      var color = _invalidColor;

      if (hit?.HitEntity != null)
      {
        to = hit.Position;

        var voxel = (hit.HitEntity as MyVoxelBase)?.RootVoxel;
        if (voxel != null)
        {
          Vector3D upVec = hit.Normal;
          Vector3D fwdVec = Vector3D.CalculatePerpendicularVector(upVec);

          bool onGround;
          var surfacePosition = GridBase.GetClosestSurfacePointFast(hit.Position, upVec, voxel, out onGround);

          if (onGround)
            surfacePosition += upVec * 0.3;
          else
            surfacePosition -= upVec * 1.5;

          to = surfacePosition;

          var matrix = MatrixD.CreateWorld(surfacePosition, fwdVec, upVec);
          _worldPosition = surfacePosition;

          color = _validColor;
          MySimpleObjectDraw.DrawTransparentCylinder(ref matrix, 1, 0.75f, 0.25f, ref color, true, 25, 0.01f, _material_square);

          matrix.Translation -= upVec * 0.125;
          DrawAxis(ref matrix, 0.75f, color, color, _material_square);
        }
        else
        {
          var grid = hit.HitEntity as MyCubeGrid;
          if (grid != null && grid.GridSize > 1)
          {
            var upDir = grid.WorldMatrix.GetClosestDirection(ActiveBot.WorldMatrix.Up);
            var pos = hit.Position + hit.Normal * grid.GridSize * 0.2f;
            var localPos = grid.WorldToGridInteger(pos);
            var cube = grid.GetCubeBlock(localPos) as IMySlimBlock;

            if (cube != null)
            {
              var upVector = grid.WorldMatrix.GetDirectionVector(upDir);
              var fwdVec = Vector3D.CalculatePerpendicularVector(upVector);
              var matrix = MatrixD.CreateWorld(to, fwdVec, upVector);

              _worldPosition = grid.GridIntegerToWorld(localPos);
              to = _worldPosition.Value + matrix.Down * grid.GridSize * 0.36;
              matrix.Translation = to;

              color = _validColor;
              MySimpleObjectDraw.DrawTransparentCylinder(ref matrix, 1, 0.75f, 0.25f, ref color, true, 25, 0.01f, _material_square);

              matrix.Translation += matrix.Down * 0.125;
              DrawAxis(ref matrix, 0.75f, color, color, _material_square);
            }
            else
            {
              var upVec = Base6Directions.GetIntVector(upDir);
              var posBelow = localPos - upVec;
              cube = grid.GetCubeBlock(posBelow);

              if (cube != null || (voxel != null && GridBase.PointInsideVoxel(grid.GridIntegerToWorld(posBelow), voxel)))
              {
                MatrixD matrix;
                Vector3D upVector;
                if (cube != null)
                {
                  upVector = grid.WorldMatrix.GetDirectionVector(upDir);
                }
                else if (voxel != null)
                {
                  upVector = hit.Normal;
                }
                else
                {
                  upVector = ActiveBot.WorldMatrix.Up;
                }

                Vector3D fwdVec = Vector3D.CalculatePerpendicularVector(upVector);
                matrix = MatrixD.CreateWorld(to, fwdVec, upVector);

                _worldPosition = grid.GridIntegerToWorld(localPos);
                to = _worldPosition.Value + matrix.Down * grid.GridSize * 0.36;
                matrix.Translation = to;

                color = _validColor;
                MySimpleObjectDraw.DrawTransparentCylinder(ref matrix, 1, 0.75f, 0.25f, ref color, true, 25, 0.01f, _material_square);

                matrix.Translation += matrix.Down * 0.125;
                DrawAxis(ref matrix, 0.75f, color, color, _material_square);
              }
              else
                _worldPosition = null;
            }
          }
          else
            _worldPosition = null;
        }
      }
      else
        _worldPosition = null;

      MySimpleObjectDraw.DrawLine(from, to, _material_square, ref color, 0.01f);
    }

    public void UpdateCursorPosition(Vector2 mouseDelta) // TODO: need to include aspect ratio in delta calculation ???
    {
      var delta = new Vector2D(mouseDelta.X, -mouseDelta.Y) * 0.001675 * AiSession.Instance.PlayerData.MouseSensitivityModifier;
      var offset = Vector2D.Clamp(_cursor.Offset + delta, _minCursorPosition, _maxCursorPosition);
      var lmbPressed = MyAPIGateway.Input.IsLeftMousePressed();

      delta = offset - _cursor.Offset;
      _cursor.Offset = offset;

      if (ShowInventory)
      {
        if (lmbPressed)
        {
          var deltaY = (float)delta.Y;
          var aspectRatio = AspectRatio;

          if (_playerInvBox.ScrollBar.IsMouseOver)
          {
            if (deltaY != 0)
              _playerInvBox.UpdateScrollBar(ref deltaY, ref aspectRatio);

            return;
          }
          else if (_botInvBox.ScrollBar.IsMouseOver)
          {
            if (deltaY != 0)
              _botInvBox.UpdateScrollBar(ref deltaY, ref aspectRatio);

            return;
          }
        }

        var ratio = AspectRatio;
        bool equipVisible = _equipWeaponBtn.IsVisible;
        if (equipVisible)
        {
          //_playerInvBox.HideToolTip();
          //_botInvBox.HideToolTip();

          _equipWeaponBtn.SetMouseOver(_cursor.IsWithinButton(_equipWeaponBtn, AspectRatio));
        }
        else if (_moveButton != null)
        {
          //_playerInvBox.HideToolTip();
          //_botInvBox.HideToolTip();

          var min = _minCursorPosition - _moveButton.Background.Origin;
          var max = _maxCursorPosition - _moveButton.Background.Origin;

          offset = Vector2D.Clamp(_moveButton.Background.Offset + delta, min, max);

          _moveButton.SetRelativePosition(ref offset, ref ratio);
          _moveButton.SetTextBottomLeft(AspectRatio);
          return;
        }

        var position = _cursor.Origin + _cursor.Offset;
        bool inPlayerBox = _playerInvBox.SetMouseOver(position, ratio, equipVisible);
        bool inBotBox = _botInvBox.SetMouseOver(position, ratio, equipVisible);

        bool autoFalse = equipVisible || inPlayerBox || inBotBox;
        bool overPlrBtn = !autoFalse && _cursor.IsWithinButton(_invAddToPlayer, AspectRatio);
        bool overBotBtn = !autoFalse && !overPlrBtn && _cursor.IsWithinButton(_invAddToBot, AspectRatio);
        bool overClsBtn = !autoFalse && !overBotBtn && _cursor.IsWithinButton(_invClose, AspectRatio);

        _invAddToPlayer.SetMouseOver(overPlrBtn);
        _invAddToBot.SetMouseOver(overBotBtn);
        _invClose.SetMouseOver(overClsBtn);
      }
      else if (ShowPatrol)
      {
        if (lmbPressed && _patrolMenu.RouteBox.ScrollBar.IsMouseOver)
        {
          var deltaY = (float)delta.Y;
          var aspectRatio = AspectRatio;

          if (deltaY != 0)
            _patrolMenu.RouteBox.UpdateScrollBar(ref deltaY, ref aspectRatio);

          return;
        }

        var position = _cursor.Origin + _cursor.Offset;
        var ratio = AspectRatio;
        _patrolMenu.SetMouseOver(ref position, ref ratio);
      }
    }

    public void ApplyMouseWheelMovement(float movement)
    {
      var cursorPosition = _cursor.Origin + _cursor.Offset;
      var aspectRatio = AspectRatio;

      if (ShowInventory)
      {
        if (cursorPosition.IsWithinBounds(_playerInvBox.Border, aspectRatio))
        {
          movement *= 0.02f;
          _playerInvBox.UpdateScrollBar(ref movement, ref aspectRatio);
          UpdateCursorPosition(Vector2.Zero);
        }
        else if (cursorPosition.IsWithinBounds(_botInvBox.Border, aspectRatio))
        {
          movement *= 0.02f;
          _botInvBox.UpdateScrollBar(ref movement, ref aspectRatio);
          UpdateCursorPosition(Vector2.Zero);
        }
      }
      else if (ShowPatrol && (_patrolMenu.RouteBox.HasFocus || cursorPosition.IsWithinBounds(_patrolMenu.RouteBox.ScrollBarBox.Background, aspectRatio)))
      {
        var delta = Math.Sign(movement);
        _patrolMenu.RouteBox.UpdateScrollBarMouseWheel(ref delta, ref aspectRatio);
        UpdateCursorPosition(Vector2.Zero);
      }
    }

    bool _invRetrieved, _invRetrievedPreviously;
    MyInventory _playerInventory, _botInventory;
    List<InventoryMapItem> _invItems = new List<InventoryMapItem>();
    InventoryBox _playerInvBox, _botInvBox;

    public void DrawInventoryScreen(IMyCharacter ch)
    {
      if (ch?.IsDead != false || ActiveBot?.MarkedForClose != false || ActiveBot.IsDead)
      {
        SetInventoryScreenVisibility(false);
        return;
      }

      if (!_invRetrieved)
      {
        _playerInventory = ch.GetInventory() as MyInventory;
        _botInventory = ActiveBot.GetInventory() as MyInventory;

        _invItems.Clear();
        var itemList = _playerInventory.GetItems();

        for (int i = 0; i < itemList.Count; i++)
        {
          var item = itemList[i];
          var invItem = new InventoryMapItem(item);
          _invItems.Add(invItem);
        }

        bool needNewEquipMsg1;
        bool fullRefresh = !_invRetrievedPreviously;
        _playerInvBox.UpdateInventory(_invItems, AspectRatio, ch.DisplayName, _playerInventory, out needNewEquipMsg1, fullRefresh);

        _invItems.Clear();
        itemList = _botInventory.GetItems();
        for (int i = 0; i < itemList.Count; i++)
        {
          var item = itemList[i];
          var invItem = new InventoryMapItem(item);
          _invItems.Add(invItem);
        }

        bool needNewEquipMsg2;
        _botInvBox.UpdateInventory(_invItems, AspectRatio, ActiveBot.Name, _botInventory, out needNewEquipMsg2, fullRefresh);

        if (needNewEquipMsg1 || needNewEquipMsg2)
        {
          var height = _equipWeaponBtn.Background.Height;
          var options = HudAPIv2.Options.Fixed | HudAPIv2.Options.FOVScale;
          var position = _cursor.Origin;
          var offset = _cursor.Offset;

          _equipWeaponBtn?.Close();
          _cursor?.DeleteMessage();

          var equipBB = new HudAPIv2.BillBoardHUDMessage(
            Material: _material_square,
            Origin: _invBackground.Origin + _invBackground.Offset,
            Width: height,
            Height: height,
            BillBoardColor: Color.LightCyan * 0.9f,
            Blend: BlendTypeEnum.PostPP);

          equipBB.Options |= options;
          equipBB.Visible = false;

          var equipMsg = new HudAPIv2.HUDMessage(new StringBuilder("Equip"), equipBB.Origin, Scale: 0.7f, Blend: BlendTypeEnum.PostPP);
          equipMsg.Offset = equipBB.Offset - equipMsg.GetTextLength() * 0.5;
          equipMsg.Options |= options;
          equipMsg.Visible = false;
          equipMsg.InitialColor = Color.Black;

          _equipWeaponBtn = new Button(equipBB, equipMsg, AspectRatio, equipBB.BillBoardColor, Color.Transparent, Color.LightCyan, Color.LightCyan, _emitter, _mouseOverSoundPair, false);

          _cursor = new HudAPIv2.BillBoardHUDMessage(
            Material: _material_cursor,
            Origin: position,
            Offset: offset,
            BillBoardColor: Color.White,
            Width: 0.09f,
            Height: 0.09f,
            Blend: BlendTypeEnum.PostPP);

          _cursor.Options |= options;
          _cursor.Visible = true;
        }

        SetInventoryScreenVisibility(true);
        UpdateCursorPosition(Vector2.Zero);
      }
    }

    public void SetInventoryScreenVisibility(bool enable)
    {
      try
      {
        ShowInventory = enable;

        if (_invBackground != null)
        {
          _invBackground.Visible = enable;

          if (_invBgBorder != null)
            _invBgBorder.Visible = enable;
        }

        if (_cursor != null)
          _cursor.Visible = enable;

        var screenPx = HudAPIv2.APIinfo.ScreenPositionOnePX;
        var aspectRatio = AspectRatio;
        var val = false;
        _playerInvBox?.SetVisibility(enable, ref aspectRatio, ref screenPx);
        _botInvBox?.SetVisibility(enable, ref aspectRatio, ref screenPx);

        _equipWeaponBtn.SetVisibility(ref val);
        _logoBox?.SetVisibility(ref enable);
        _invAddToBot?.SetVisibility(ref enable);
        _invAddToPlayer.SetVisibility(ref enable);
        _invClose?.SetVisibility(ref enable);
        _invRetrieved = enable;
        _invRetrievedPreviously = enable;

        if (!enable)
        {
          var botInv = ActiveBot?.GetInventory() as MyInventory;
          var plrInv = MyAPIGateway.Session.Player?.Character?.GetInventory() as MyInventory;

          if (botInv != null)
            botInv.InventoryContentChanged -= OnInventoryContentChanged;

          if (plrInv != null)
            plrInv.InventoryContentChanged -= OnInventoryContentChanged;

          UpdateBlacklist(true);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CommandMenu.HideInventoryScreen: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    float _uiOpacityMultiplier = 0.8f;
    public void Register()
    {
      MyAPIGateway.Gui.GuiControlRemoved += GuiControlRemoved;
      _uiOpacityMultiplier = MyAPIGateway.Session?.Config?.UIBkOpacity ?? 0.8f;

      Registered = Init();

      AiSession.Instance.Logger.Log($"{GetType().Name} Registered Successfully = {Registered}");
    }

    private void GuiControlRemoved(object obj)
    {
      try
      {
        if (obj.ToString().EndsWith("ScreenOptionsSpace"))
        {
          _uiOpacityMultiplier = MyAPIGateway.Session?.Config?.UIBkOpacity ?? 0.8f;

          // TODO: redo the colors for all billboards
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CommandMenu.GuiControlRemoved: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    bool Init()
    {
      try
      {
        AiSession.Instance.HudAPI.OnScreenDimensionsChanged = OnScreenDimensionsChanged;

        // TODO: When Text Hud API is updated, uncomment this ->-> Nope, going to draw it myself
        //_nameInput = new HudAPIv2.MenuTextInput("Route Name", null, "Enter a route name", OnRouteName_Submitted);
        //_renameInput = new HudAPIv2.MenuTextInput("Route Name", null, "Enter new route name", OnRouteRename_Submitted);
        //_screenPX = HudAPIv2.APIinfo.ScreenPositionOnePX;

        Vector2? viewport = MyAPIGateway.Session?.Camera.ViewportSize;
        AspectRatio = viewport == null ? 0.5625 : viewport.Value.Y / viewport.Value.X;

        var options = HudAPIv2.Options.Fixed | HudAPIv2.Options.FOVScale;

        var radialBrackets = MyStringId.GetOrCompute("AiEnabled_RadialBrackets");
        var radialSector = MyStringId.GetOrCompute("AiEnabled_RadialSector2");
        var arrowButton = MyStringId.GetOrCompute("AiEnabled_ArrowButton");
        var square2 = MyStringId.GetOrCompute("AiEnabled_Square2");
        var squareOuter = MyStringId.GetOrCompute("AiEnabled_SquareOuter");

        var offsetX = new Vector2D(0.005 * AspectRatio, 0);
        var offsetY = new Vector2D(0, 0.005);
        var origin = new Vector2D(0, -0.25);

        _billboardColor = new Color(41, 54, 62, 150);
        _highlightColor = Color.LightCyan * 0.9f;
        _radialColor = new Color(41, 54, 62, 225);

        var borderColor = new Color(200, 200, 200, 250);
        var mouseOverColor = Color.LightCyan * 0.5f;
        var cursorColor = new Color(112, 128, 144, 225); // Color.SlateGray with less alpha;
        var logoColor = new Color(255, 255, 255, 225);

        _interactBB = new HudAPIv2.BillBoardHUDMessage(
          Material: squareOuter,
          Origin: Vector2D.Zero,
          BillBoardColor: _radialColor * 0.8f,
          Blend: BlendTypeEnum.PostPP);

        _interactBB.Options |= options;
        _interactBB.Visible = false;

        UseControl = MyAPIGateway.Input.GetGameControl(MyControlsSpace.USE);
        string controlString;

        var key = UseControl.GetKeyboardControl();
        if (key != MyKeys.None)
        {
          controlString = key.ToString();
        }
        else
        {
          key = UseControl.GetSecondKeyboardControl();
          if (key != MyKeys.None)
          {
            controlString = key.ToString();
          }
          else
          {
            var btn = UseControl.GetMouseControl();
            switch (btn)
            {
              case MyMouseButtonsEnum.Left:
                controlString = "LMB";
                break;
              case MyMouseButtonsEnum.Right:
                controlString = "RMB";
                break;
              case MyMouseButtonsEnum.Middle:
                controlString = "MMB";
                break;
              case MyMouseButtonsEnum.XButton1:
                controlString = "MXB1";
                break;
              case MyMouseButtonsEnum.XButton2:
                controlString = "MXB2";
                break;
              default:
                controlString = "F";
                break;
            }
          }
        }

        _lastControlString = controlString;
        _interactMsg = new HudAPIv2.HUDMessage(new StringBuilder($"Press '{controlString}'\nto interact"), Vector2D.Zero, Blend: BlendTypeEnum.PostPP);
        _interactMsg.Options |= options;
        _interactMsg.Visible = false;

        var length = _interactMsg.GetTextLength();
        _interactMsg.Offset = length * 0.5;
        _interactBB.Width = (float)(length.X / AspectRatio * 1.25);
        _interactBB.Height = (float)(length.Y * 1.25);
        _interactBB.Offset = _interactMsg.Offset + length * 0.5;

        _radialBracket1 = new HudAPIv2.BillBoardHUDMessage(
          Material: radialBrackets,
          Origin: origin,
          BillBoardColor: _radialColor,
          Width: 0.55f,
          Height: 0.55f,
          Blend: BlendTypeEnum.PostPP);

        _radialBracket1.Options |= options;
        _radialBracket1.Visible = false;

        _radialBracket2 = new HudAPIv2.BillBoardHUDMessage(
          Material: radialBrackets,
          Origin: origin,
          BillBoardColor: _radialColor,
          Width: 0.55f,
          Height: 0.55f,
          Blend: BlendTypeEnum.PostPP);

        _radialBracket2.Options |= options;
        _radialBracket2.Visible = false;
        _radialBracket2.Rotation = MathHelper.PiOver2;

        var offsetY2 = offsetY * 0.5;

        _radialBBTop = new HudAPIv2.BillBoardHUDMessage(
          Material: radialSector,
          Origin: origin,
          BillBoardColor: _radialColor,
          Offset: offsetY2,
          Width: 0.5f,
          Height: 0.5f,
          Blend: BlendTypeEnum.PostPP);

        _radialBBTop.Options |= options;
        _radialBBTop.Visible = false;

        _radialMsgTop = new HudAPIv2.HUDMessage(new StringBuilder("Inventory"), origin, _radialBBTop.Offset, Blend: BlendTypeEnum.PostPP);
        _radialMsgTop.Options |= options;
        _radialMsgTop.Visible = false;

        var textSize = _radialMsgTop.GetTextLength();
        _radialMsgTop.Offset = _radialBBTop.Offset - textSize * 0.5 + new Vector2D(0, _radialBBTop.Height * 0.325);

        _radialBBTopLeft = new HudAPIv2.BillBoardHUDMessage(
          Material: radialSector,
          Origin: origin,
          BillBoardColor: _radialColor,
          Offset: -offsetX + offsetY2,
          Width: 0.5f,
          Height: 0.5f,
          Blend: BlendTypeEnum.PostPP);

        _radialBBTopLeft.Rotation = (float)-VectorUtils.PiOver3;
        _radialBBTopLeft.Options |= options;
        _radialBBTopLeft.Visible = false;

        _radialMsgTopLeft = new HudAPIv2.HUDMessage(new StringBuilder("Patrol"), origin, _radialBBTopLeft.Offset, Blend: BlendTypeEnum.PostPP);
        _radialMsgTopLeft.Options |= options;
        _radialMsgTopLeft.Visible = false;

        textSize = _radialMsgTopLeft.GetTextLength();
        _radialMsgTopLeft.Offset = _radialBBTopLeft.Offset - textSize * 0.5 + new Vector2D(-_radialBBTopLeft.Width * AspectRatio * 0.25, _radialBBTopLeft.Height * 0.125);

        _radialBBTopRight = new HudAPIv2.BillBoardHUDMessage(
          Material: radialSector,
          Origin: origin,
          BillBoardColor: _radialColor,
          Offset: offsetX + offsetY2,
          Width: 0.5f,
          Height: 0.5f,
          Blend: BlendTypeEnum.PostPP);

        _radialBBTopRight.Rotation = (float)VectorUtils.PiOver3;
        _radialBBTopRight.Options |= options;
        _radialBBTopRight.Visible = false;

        _radialMsgTopRight = new HudAPIv2.HUDMessage(new StringBuilder("Go To"), origin, _radialBBTopRight.Offset, Blend: BlendTypeEnum.PostPP);
        _radialMsgTopRight.Options |= options;
        _radialMsgTopRight.Visible = false;

        textSize = _radialMsgTopRight.GetTextLength();
        _radialMsgTopRight.Offset = _radialBBTopRight.Offset - textSize * 0.5 + new Vector2D(_radialBBTopRight.Width * AspectRatio * 0.25, _radialBBTopRight.Height * 0.125);

        _radialBBBottom = new HudAPIv2.BillBoardHUDMessage(
          Material: radialSector,
          Origin: origin,
          BillBoardColor: _radialColor,
          Offset: -offsetY2,
          Width: 0.5f,
          Height: 0.5f,
          Blend: BlendTypeEnum.PostPP);

        _radialBBBottom.Rotation = MathHelper.Pi;
        _radialBBBottom.Options |= options;
        _radialBBBottom.Visible = false;

        _radialMsgBottom = new HudAPIv2.HUDMessage(new StringBuilder("Resume"), origin, _radialBBBottom.Offset, Blend: BlendTypeEnum.PostPP);
        _radialMsgBottom.Options |= options;
        _radialMsgBottom.Visible = false;

        textSize = _radialMsgBottom.GetTextLength();
        _radialMsgBottom.Offset = _radialBBBottom.Offset - textSize * 0.5 - new Vector2D(0, _radialBBBottom.Height * 0.325);

        _radialBBBottomLeft = new HudAPIv2.BillBoardHUDMessage(
          Material: radialSector,
          Origin: origin,
          BillBoardColor: _radialColor,
          Offset: -offsetX - offsetY2,
          Width: 0.5f,
          Height: 0.5f,
          Blend: BlendTypeEnum.PostPP);

        _radialBBBottomLeft.Rotation = MathHelper.Pi + (float)VectorUtils.PiOver3;
        _radialBBBottomLeft.Options |= options;
        _radialBBBottomLeft.Visible = false;

        _radialMsgBottomLeft = new HudAPIv2.HUDMessage(new StringBuilder("Follow"), origin, _radialBBBottomLeft.Offset, Blend: BlendTypeEnum.PostPP);
        _radialMsgBottomLeft.Options |= options;
        _radialMsgBottomLeft.Visible = false;

        textSize = _radialMsgBottomLeft.GetTextLength();
        _radialMsgBottomLeft.Offset = _radialBBBottomLeft.Offset - textSize * 0.5 - new Vector2D(_radialBBBottomLeft.Width * AspectRatio * 0.25, _radialBBBottomLeft.Height * 0.125);

        _radialBBBottomRight = new HudAPIv2.BillBoardHUDMessage(
          Material: radialSector,
          Origin: origin,
          BillBoardColor: _radialColor,
          Offset: offsetX - offsetY2,
          Width: 0.5f,
          Height: 0.5f,
          Blend: BlendTypeEnum.PostPP);

        _radialBBBottomRight.Rotation = MathHelper.Pi - (float)VectorUtils.PiOver3;
        _radialBBBottomRight.Options |= options;
        _radialBBBottomRight.Visible = false;

        _radialMsgBottomRight = new HudAPIv2.HUDMessage(new StringBuilder("Stay"), origin, _radialBBBottomRight.Offset, Blend: BlendTypeEnum.PostPP);
        _radialMsgBottomRight.Options |= options;
        _radialMsgBottomRight.Visible = false;

        textSize = _radialMsgBottomRight.GetTextLength();
        _radialMsgBottomRight.Offset = _radialBBBottomRight.Offset - textSize * 0.5 + new Vector2D(_radialBBBottomRight.Width * AspectRatio * 0.25, -_radialBBBottomRight.Height * 0.125);

        _radialArrow = new HudAPIv2.BillBoardHUDMessage(
          Material: MyStringId.GetOrCompute("Arrow"),
          Origin: origin,
          BillBoardColor: cursorColor,
          Width: 0.05f,
          Height: 0.1f,
          Blend: BlendTypeEnum.PostPP);

        _radialArrow.Options |= options;
        _radialArrow.Visible = false;

        _invBackground = new HudAPIv2.BillBoardHUDMessage(
          Material: squareOuter,
          Origin: new Vector2D(0, 0.2),
          BillBoardColor: _billboardColor,
          Width: 2f,
          Height: 1.25f,
          Blend: BlendTypeEnum.Standard);

        _invBackground.Options |= options;
        _invBackground.Visible = false;

        _invBgBorder = new HudAPIv2.BillBoardHUDMessage(
          Material: MyStringId.GetOrCompute("AiEnabled_SquareOuterBorder"),
          Origin: _invBackground.Origin + _invBackground.Offset,
          BillBoardColor: borderColor,
          Width: _invBackground.Width,
          Height: _invBackground.Height,
          Blend: BlendTypeEnum.PostPP);

        _invBgBorder.Options |= options;
        _invBgBorder.Visible = false;

        var half = new Vector2D(_invBackground.Width * AspectRatio, _invBackground.Height) * 0.5;
        _minCursorPosition = _invBackground.Origin - half;
        _maxCursorPosition = _invBackground.Origin + half;

        var logoSize = _invBackground.Height * 0.15f;
        var logoBg = new HudAPIv2.BillBoardHUDMessage(
          Material: _material_square,
          Origin: _invBackground.Origin + _invBackground.Offset,
          Offset: new Vector2D(0, _invBackground.Height * 0.35),
          BillBoardColor: Color.Transparent,
          Width: logoSize,
          Height: logoSize,
          Blend: BlendTypeEnum.PostPP);

        logoBg.Options |= options;
        logoBg.Visible = false;
        
        var logoIcon = new HudAPIv2.BillBoardHUDMessage(
          Material: MyStringId.GetOrCompute("AiEnabled_Logo"),
          Origin: _invBackground.Origin + _invBackground.Offset,
          Offset: logoBg.Offset,
          BillBoardColor: logoColor,
          Width: logoSize,
          Height: logoSize,
          Blend: BlendTypeEnum.PostPP);

        logoIcon.Options |= options;
        logoIcon.Visible = false;

        var logoMsg = new HudAPIv2.HUDMessage(new StringBuilder("~ Bot Inventory Manager ~"), logoBg.Origin, Scale: 0.9, Blend: BlendTypeEnum.PostPP);
        length = logoMsg.GetTextLength();
        logoMsg.Options |= options;
        logoMsg.Visible = false;
        logoMsg.Offset = logoBg.Offset - new Vector2D(length.X * 0.5, logoBg.Height * 0.5 + length.Y * -0.5);

        _logoBox = new TextBox(logoBg, logoMsg, AspectRatio, Color.Transparent, logoIcon, useBorder: false);

        var leftOver = _invBackground.Width * 0.5 - length.X * 0.75;
        var width = (_invBackground.Width - leftOver) * 0.5 * AspectRatio;

        var lBoxBB1 = new HudAPIv2.BillBoardHUDMessage(
          Material: square2,
          Origin: _invBackground.Origin + _invBackground.Offset,
          Offset: new Vector2D(-width, 0),
          BillBoardColor: _billboardColor,
          Width: 0.75f,
          Height: _invBackground.Height * 0.85f,
          Blend: BlendTypeEnum.Standard);

        lBoxBB1.Options |= options;
        lBoxBB1.Visible = false;

        var lBoxBB2 = new HudAPIv2.BillBoardHUDMessage(
          Material: square2,
          Origin: _invBackground.Origin + _invBackground.Offset,
          Offset: new Vector2D(width, 0),
          BillBoardColor: _billboardColor,
          Width: 0.75f,
          Height: _invBackground.Height * 0.85f,
          Blend: BlendTypeEnum.Standard);

        lBoxBB2.Options |= options;
        lBoxBB2.Visible = false;

        _playerInvBox = new InventoryBox(lBoxBB1, AspectRatio, _billboardColor, _emitter, _mouseOverSoundPair);
        _botInvBox = new InventoryBox(lBoxBB2, AspectRatio, _billboardColor, _emitter, _mouseOverSoundPair);

        var invCloseBB = new HudAPIv2.BillBoardHUDMessage(
          Material: _material_square,
          Origin: _invBackground.Origin + _invBackground.Offset,
          BillBoardColor: _billboardColor,
          Blend: BlendTypeEnum.PostPP);

        invCloseBB.Options |= options;
        invCloseBB.Visible = false;

        var invCloseMsg = new HudAPIv2.HUDMessage(new StringBuilder("~ Close ~"), invCloseBB.Origin, Scale: 0.9, Blend: BlendTypeEnum.PostPP);
        invCloseMsg.Options |= options;
        invCloseMsg.Visible = false;

        length = invCloseMsg.GetTextLength();
        width = length.X * 1.25f / AspectRatio;
        var height = length.Y * -2;

        invCloseBB.Width = (float)width;
        invCloseBB.Height = (float)height;
        invCloseBB.Offset = new Vector2D(0, height * -1.75 * 4.5);
        invCloseMsg.Offset = invCloseBB.Offset - length * 0.5;

        _invClose = new Button(invCloseBB, invCloseMsg, AspectRatio, _billboardColor, borderColor, mouseOverColor, mouseOverColor, _emitter, _mouseOverSoundPair);

        height *= 1.25;
        var iconHeight = (float)height * 0.6f;

        var addToPlayerBB = new HudAPIv2.BillBoardHUDMessage(
          Material: _material_square,
          Origin: _invBackground.Origin + _invBackground.Offset,
          Offset: Vector2D.Zero,
          Width: (float)height,
          Height: (float)height,
          BillBoardColor: _billboardColor,
          Blend: BlendTypeEnum.PostPP);

        addToPlayerBB.Options |= options;
        addToPlayerBB.Visible = false;

        var addtoPlayerIcon = new HudAPIv2.BillBoardHUDMessage(
          Material: arrowButton,
          Origin: _invBackground.Origin + _invBackground.Offset,
          Offset: addToPlayerBB.Offset,
          Width: iconHeight,
          Height: iconHeight,
          BillBoardColor: Color.White,
          Blend: BlendTypeEnum.PostPP);

        addtoPlayerIcon.Options |= options;
        addtoPlayerIcon.Visible = false;

        _invAddToPlayer = new Button(addToPlayerBB, null, AspectRatio, _billboardColor, borderColor, mouseOverColor, mouseOverColor, _emitter, _mouseOverSoundPair);
        _invAddToPlayer.Icon = addtoPlayerIcon;

        var addToBotBB = new HudAPIv2.BillBoardHUDMessage(
          Material: _material_square,
          Origin: _invBackground.Origin + _invBackground.Offset,
          Offset: new Vector2D(0, height * 1.75),
          Width: (float)height,
          Height: (float)height,
          BillBoardColor: _billboardColor,
          Blend: BlendTypeEnum.PostPP);

        addToBotBB.Options |= options;
        addToBotBB.Visible = false;

        var addtoBotIcon = new HudAPIv2.BillBoardHUDMessage(
          Material: arrowButton,
          Origin: _invBackground.Origin + _invBackground.Offset,
          Offset: addToBotBB.Offset,
          Width: iconHeight,
          Height: iconHeight,
          BillBoardColor: Color.White,
          Blend: BlendTypeEnum.PostPP);

        addtoBotIcon.Rotation = MathHelper.Pi;
        addtoBotIcon.Options |= options;
        addtoBotIcon.Visible = false;

        _invAddToBot = new Button(addToBotBB, null, AspectRatio, _billboardColor, borderColor, mouseOverColor, mouseOverColor, _emitter, _mouseOverSoundPair);
        _invAddToBot.Icon = addtoBotIcon;

        var equipBB = new HudAPIv2.BillBoardHUDMessage(
          Material: _material_square,
          Origin: _invBackground.Origin + _invBackground.Offset,
          Width: (float)height,
          Height: (float)height,
          BillBoardColor: Color.LightCyan * 0.9f,
          Blend: BlendTypeEnum.PostPP);

        equipBB.Options |= options;
        equipBB.Visible = false;

        var equipMsg = new HudAPIv2.HUDMessage(new StringBuilder("Equip"), equipBB.Origin, Scale: 0.7f, Blend: BlendTypeEnum.PostPP);
        equipMsg.Offset = equipBB.Offset - equipMsg.GetTextLength() * 0.5;
        equipMsg.Options |= options;
        equipMsg.Visible = false;
        equipMsg.InitialColor = Color.Black;

        _equipWeaponBtn = new Button(equipBB, equipMsg, AspectRatio, equipBB.BillBoardColor, Color.Transparent, Color.LightCyan, Color.LightCyan, _emitter, _mouseOverSoundPair, false);

        _patrolMenu = new PatrolMenu(AspectRatio, _billboardColor, _emitter, _mouseOverSoundPair, _hudClickSoundPair, _errorSoundPair);

        _cursor = new HudAPIv2.BillBoardHUDMessage(
          Material: _material_cursor,
          Origin: Vector2D.Zero,
          BillBoardColor: Color.White,
          Width: 0.09f,
          Height: 0.09f,
          Blend: BlendTypeEnum.PostPP);

        _cursor.Options |= options;
        _cursor.Visible = false;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CommandMenu.Init: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
        return false;
      }

      return true;
    }

    private void OnScreenDimensionsChanged()
    {
      if (MyAPIGateway.Utilities.IsDedicated)
        return;

      Vector2? viewport = MyAPIGateway.Session?.Camera.ViewportSize;
      AspectRatio = viewport == null ? 0.5625 : viewport.Value.Y / viewport.Value.X;
      //_screenPX = HudAPIv2.APIinfo.ScreenPositionOnePX;

      _cursor.Origin = _invBackground.Origin + _invBackground.Offset;
      var half = new Vector2D(_invBackground.Width * 0.5 * AspectRatio, _invBackground.Height * 0.5);
      _minCursorPosition = _cursor.Origin - half;
      _maxCursorPosition = _cursor.Origin + half;
    }

    // From Digi - God bless his soul <3
    private void DrawAxis(ref MatrixD worldMatrix, float radius, Vector4 faceColor, Vector4 lineColor,
      MyStringId? faceMaterial = null, MyStringId? lineMaterial = null, float lineThickness = 0.01f,
      BlendTypeEnum blendType = BlendTypeEnum.PostPP)
    {
      const int wireDivRatio = 25;

      Vector3D center = worldMatrix.Translation;
      Vector3 normal = (Vector3)worldMatrix.Forward;

      double startRad = 0;
      double endRad = MathHelper.TwoPi;
      if (startRad > endRad)
        startRad -= MathHelperD.TwoPi;

      Vector3D current = Vector3D.Zero;
      Vector3D previous = Vector3D.Zero;
      double angleRad = startRad;

      double stepRad = MathHelperD.TwoPi / wireDivRatio;
      bool first = true;

      Vector2 uv0 = new Vector2(0, 0.5f);
      Vector2 uv1 = new Vector2(1, 0);
      Vector2 uv2 = new Vector2(1, 1);

      while (true)
      {
        bool exit = false;
        if (angleRad > endRad)
        {
          angleRad = endRad;
          exit = true;
        }

        double x = radius * Math.Cos(angleRad);
        double z = radius * Math.Sin(angleRad);
        current.X = worldMatrix.M41 + x * worldMatrix.M11 + z * worldMatrix.M31; // inlined Transform() without scale
        current.Y = worldMatrix.M42 + x * worldMatrix.M12 + z * worldMatrix.M32;
        current.Z = worldMatrix.M43 + x * worldMatrix.M13 + z * worldMatrix.M33;

        var dirNorm = (Vector3)(current - center);

        if ((first || exit) && lineMaterial.HasValue)
        {
          MyTransparentGeometry.AddLineBillboard(lineMaterial.Value, lineColor, center, dirNorm, 1f, lineThickness, blendType);
        }

        if (!first && faceMaterial.HasValue)
        {
          MyTransparentGeometry.AddTriangleBillboard(center, current, previous, normal, normal, normal, uv0, uv1, uv2, faceMaterial.Value, 0, center, faceColor, blendType);
        }

        if (exit)
        {
          break;
        }

        if (first)
        {
          angleRad = -MathHelperD.TwoPi;
          while (angleRad < startRad)
            angleRad += stepRad;
        }
        else
        {
          angleRad += stepRad;
        }

        first = false;
        previous = current;
      }
    }
  }
}
