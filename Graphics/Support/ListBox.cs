using AiEnabled.API;
using AiEnabled.ConfigData;
using AiEnabled.Utilities;

using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage;
using VRage.Utils;

using VRageMath;

using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace AiEnabled.Graphics.Support
{
  public class ListBox
  {
    public Button ScrollBar, SelectedItem;
    public Box ScrollBarBox;
    public bool HasFocus;

    HudAPIv2.BillBoardHUDMessage _background, _border;
    HudAPIv2.HUDMessage _header, _footer;

    List<KeyValuePair<MyStringId, Button>> _routeList = new List<KeyValuePair<MyStringId, Button>>(10);
    Dictionary<MyStringId, Route> _patrolRoutes = new Dictionary<MyStringId, Route>(MyStringId.Comparer);
    Stack<Route> _routeStack = new Stack<Route>(10);
    Stack<Button> _btnStack;

    MyEntity3DSoundEmitter _emitter;
    MySoundPair _soundPair;
    Vector4 _mouseOverColor, _selectedColor;
    Vector2 _size;
    readonly int _maxVisibleRows;
    int _totalRows, _firstVisibleIndex, _lastVisibleIndex;
    float _buttonViewOffsetY;
    bool _wasWithinBounds;

    public ListBox(HudAPIv2.BillBoardHUDMessage bg, HudAPIv2.BillBoardHUDMessage border, Color buttonColor, MyEntity3DSoundEmitter emitter, MySoundPair mouseOverSound, double aspectRatio)
    {
      var btnColor = buttonColor.ToVector4(); //.ToLinearRGB();
      var borderColor = new Color(200, 200, 200, 250);
      _emitter = emitter;
      _soundPair = mouseOverSound;
      _selectedColor = (Color.LightCyan * 0.75f).ToVector4(); //.ToLinearRGB();
      _mouseOverColor = (Color.LightCyan * 0.5f).ToVector4(); //.ToLinearRGB();
      _background = bg;
      _border = border;

      var options = HudAPIv2.Options.Fixed | HudAPIv2.Options.FOVScale;
      var square = MyStringId.GetOrCompute("Square");

      _header = new HudAPIv2.HUDMessage(
        Message: new StringBuilder("~ Stored Routes ~"),
        Origin: bg.Origin + bg.Offset,
        Blend: BlendTypeEnum.PostPP);

      var length = _header.GetTextLength();

      var textHeight = (float)Math.Abs(length.Y) * 1.25f;
      _maxVisibleRows = (int)Math.Floor((_border.Height - 0.02) / textHeight);
      _btnStack = new Stack<Button>(_maxVisibleRows + 1);
      _size = new Vector2(_border.Width * 0.9f, textHeight);

      var heightAdjustment = _border.Height - (_maxVisibleRows * textHeight + 0.0225f);
      _border.Height -= heightAdjustment;
      _background.Height -= heightAdjustment;

      _header.Offset = new Vector2D(-_border.Width * aspectRatio * 0.25, _border.Height * 0.5) - new Vector2D(length.X * 0.5, length.Y * 1.25);
      _header.Options |= options;
      _header.Visible = false;

      _footer = new HudAPIv2.HUDMessage(
        Message: new StringBuilder("Route has 6 waypoints"),
        Origin: _header.Origin,
        Blend: BlendTypeEnum.PostPP);

      length = _footer.GetTextLength();
      _footer.Offset = new Vector2D(_border.Width * aspectRatio * 0.25, -_border.Height * 0.5) + new Vector2D(length.X * -0.5, length.Y * 0.25);
      _footer.Options |= options;
      _footer.Visible = false;

      var scrollBoxBg = new HudAPIv2.BillBoardHUDMessage(
        Material: square,
        Origin: _border.Origin + _border.Offset,
        BillBoardColor: Color.Transparent,
        Width: _border.Width * 0.09f,
        Height: _border.Height,
        Blend: BlendTypeEnum.PostPP);

      scrollBoxBg.Options |= options;
      scrollBoxBg.Offset = new Vector2D(_border.Width - scrollBoxBg.Width, 0) * 0.5 * aspectRatio;
      var val = false;

      ScrollBarBox = new Box(aspectRatio, borderColor, true, scrollBoxBg);
      ScrollBarBox.SetVisibility(ref val);

      var scrollBarBg = new HudAPIv2.BillBoardHUDMessage(
          Material: square,
          Origin: scrollBoxBg.Origin + scrollBoxBg.Offset,
          BillBoardColor: btnColor,
          Width: scrollBoxBg.Width - 0.01f,
          Height: scrollBoxBg.Height - 0.01f,
          Blend: BlendTypeEnum.PostPP);

      scrollBarBg.Options |= options;
      scrollBarBg.Visible = false;
      scrollBarBg.uvEnabled = true;

      ScrollBar = new Button(scrollBarBg, null, aspectRatio, btnColor, Color.Transparent, _mouseOverColor, _selectedColor, null, null, false);

      for (int i = 0; i < _maxVisibleRows + 1; i++)
      {
        var hudBB = new HudAPIv2.BillBoardHUDMessage(
          Material: square,
          Origin: _border.Origin + _border.Offset,
          BillBoardColor: Color.Transparent,
          Width: _size.X,
          Height: _size.Y,
          Blend: BlendTypeEnum.PostPP);

        var hudMsg = new HudAPIv2.HUDMessage(
          Message: new StringBuilder(),
          Scale: 0.9,
          Origin: hudBB.Origin,
          Blend: BlendTypeEnum.PostPP);

        hudBB.Visible = hudMsg.Visible = false;
        hudBB.Options = hudMsg.Options = options;
        hudBB.uvEnabled = true;
        var btn = new Button(hudBB, hudMsg, aspectRatio, Color.Transparent, Color.Transparent, _mouseOverColor, _selectedColor, _emitter, _soundPair, false);

        _btnStack.Push(btn);
      }
    }

    public void Update(double aspectRatio, out bool newCursorNeeded, bool fullRefresh = true)
    {
      newCursorNeeded = false;
      var firstOffset = new Vector2D((_border.Width * -0.05 + 0.0075) * aspectRatio, (_border.Height - _size.Y) * 0.5 - 0.01);
      var screenPx = HudAPIv2.APIinfo.ScreenPositionOnePX;
      var options = HudAPIv2.Options.Fixed | HudAPIv2.Options.FOVScale;
      var material = MyStringId.GetOrCompute("Square");
      var worldName = MyAPIGateway.Session.Name;

      for (int i = 0; i < _routeList.Count; i++)
      {
        var kvp = _routeList[i];
        var btn = kvp.Value;

        bool val = false;
        btn.Reset();
        btn.SetVisibility(ref val);
        _btnStack.Push(btn);
      }

      _routeList.Clear();

      foreach (var kvp in _patrolRoutes)
      {
        var rte = kvp.Value;

        if (rte.WorldName != worldName || DistanceSqdToRouteCenter(rte) > 2000 * 2000)
          continue;

        var routeName = kvp.Key;

        Button btn;
        if (_btnStack.Count > 0)
        {
          btn = _btnStack.Pop();
          btn.UpdateText(routeName.String, aspectRatio, false, false);
        }
        else
        {
          newCursorNeeded = true;

          var bg = new HudAPIv2.BillBoardHUDMessage(
            Material: material,
            Origin: _border.Origin + _border.Offset,
            BillBoardColor: Color.Transparent,
            Width: _size.X,
            Height: _size.Y,
            Blend: BlendTypeEnum.PostPP);

          var hm = new HudAPIv2.HUDMessage(
            Message: new StringBuilder(routeName.String),
            Scale: 0.9,
            Origin: bg.Origin,
            Blend: BlendTypeEnum.PostPP);

          bg.Visible = hm.Visible = false;
          bg.Options = hm.Options = options;
          bg.uvEnabled = true;
          btn = new Button(bg, hm, aspectRatio, Color.Transparent, Color.Transparent, _mouseOverColor, _selectedColor, _emitter, _soundPair, false);
        }

        _routeList.Add(new KeyValuePair<MyStringId, Button>(routeName, btn));
      }

      _totalRows = _routeList.Count;
      _firstVisibleIndex = 0;
      _lastVisibleIndex = -1;
      var checkButtons = _totalRows > _maxVisibleRows;
      var checkSelected = SelectedItem != null;
      var selectedFound = false;

      for (int i = 0; i < _routeList.Count; i++)
      {
        var kvp = _routeList[i];
        var btn = kvp.Value;
        var val = false;
        btn.SetVisibility(ref val);
        btn.Reset();

        var offset = firstOffset - new Vector2D(0, _size.Y * i);
        btn.SetPositionAligned(ref offset, ref aspectRatio, TextAlignment.Left);
        btn.Index = i;

        if (i < _maxVisibleRows)
        {
          val = true;
          btn.SetVisibility(ref val);
          _lastVisibleIndex = btn.Index;
        }

        if (checkSelected && SelectedItem.Index == btn.Index)
        {
          selectedFound = true;
          checkSelected = false;
          SelectedItem = btn;
          btn.SetSelected(true);
        }
      }

      if (checkSelected && !selectedFound)
        SelectedItem = null;

      var sBox = ScrollBarBox.Background;
      var sBar = ScrollBar.Background;

      if (checkButtons)
      {
        var actualRowHeight = _size.Y * _totalRows;
        sBar.Height = (sBox.Height - 0.01f) / actualRowHeight;
        var sBarOffsetY = (sBox.Height - sBar.Height) * 0.5;
        sBar.Offset = new Vector2D(sBar.Offset.X, sBarOffsetY);

        if (!fullRefresh && _buttonViewOffsetY > 0)
        {
          var offset = -_buttonViewOffsetY;
          UpdateScrollBar(ref offset, ref aspectRatio, false);
        }
      }
      else
      {
        _buttonViewOffsetY = 0;
        sBar.Offset = Vector2D.Zero;
        sBar.Height = sBox.Height - 0.01f;
      }
    }

    public double DistanceSqdToRouteCenter(Route rte)
    {
      var player = MyAPIGateway.Session?.Player?.Character;
      if (player != null && rte?.Waypoints?.Count > 0)
      {
        var vector = Vector3D.Zero;
        foreach (var pt in rte.Waypoints)
          vector += pt;

        vector /= rte.Waypoints.Count;
        return Vector3D.DistanceSquared(vector, player.WorldAABB.Center);
      }

      return double.MaxValue;
    }

    public void DeleteSelected(ref double aspectRatio)
    {
      if (SelectedItem != null)
      {
        var idx = SelectedItem.Index;

        if (_routeList?.IsValidIndex(idx) == true)
        {
          var kvp = _routeList[idx];
          var btn = kvp.Value;

          bool val = false;
          btn.Reset();
          btn.SetVisibility(ref val);

          Route route;
          if (_patrolRoutes.TryGetValue(kvp.Key, out route))
          {
            if (route?.Waypoints != null)
              _routeStack.Push(route);
  
            _patrolRoutes.Remove(kvp.Key);
          }

          _btnStack.Push(btn);
          _routeList.RemoveAt(idx);

          bool _;
          Update(aspectRatio, out _, false);
        }

        SelectedItem = null;
      }
    }

    public void ClearSelected()
    {
      SelectedItem = null;
      ScrollBar.Reset();

      for (int i = 0; i < _routeList.Count; i++)
        _routeList[i].Value?.Reset();
    }

    public void AddRoute(List<Vector3D> route, string routeName)
    {
      if (route?.Count > 0)
      {
        Route rte = _routeStack.Count > 0 ? _routeStack.Pop() : new Route();
        var name = $"{routeName} [{route.Count} Waypoints]";
        var stringId = MyStringId.GetOrCompute(name);

        int num = 0;
        while (_patrolRoutes.ContainsKey(stringId))
        {
          num++;
          stringId = MyStringId.GetOrCompute($"{name} ({num})");
        }

        rte.Set(route);
        _patrolRoutes[stringId] = rte;
      }
    }

    public void GetPatrolList(List<Vector3D> routeList)
    {
      routeList.Clear();

      if (SelectedItem != null)
      {
        var idx = SelectedItem.Index;

        if (_routeList?.IsValidIndex(idx) == true)
        {
          var kvp = _routeList[idx];

          Route rte;
          if (_patrolRoutes.TryGetValue(kvp.Key, out rte) && rte?.Waypoints?.Count > 0)
          {
            routeList.AddList(rte.Waypoints);
          }
        }
      }
    }

    public void SetStoredRoutes(List<SerializableRoute> routeList)
    {
      if (routeList?.Count > 0)
      {
        for (int i = 0; i < routeList.Count; i++)
        {
          var newRoute = routeList[i];
          if (newRoute?.Waypoints?.Count > 0)
          {
            Route rte = _routeStack.Count > 0 ? _routeStack.Pop() : new Route();

            var routeName = newRoute.Name;
            var stringId = MyStringId.GetOrCompute(routeName);

            int num = 0;
            while (_patrolRoutes.ContainsKey(stringId))
            {
              num++;
              stringId = MyStringId.GetOrCompute($"{routeName} ({num})");
            }

            rte.Set(newRoute.Waypoints, newRoute.World);
            _patrolRoutes[stringId] = rte;
          }
        }
      }
    }

    public void GetStoredRoutes(List<SerializableRoute> routeList)
    {
      if (routeList == null)
        routeList = new List<SerializableRoute>();
      else
        routeList.Clear();

      foreach (var kvp in _patrolRoutes)
      {
        var route = new SerializableRoute(kvp.Key.String, kvp.Value.Waypoints);
        routeList.Add(route);
      }
    }

    public int GetRouteIndex()
    {
      if (SelectedItem != null)
      {
        var idx = SelectedItem.Index;

        if (_routeList?.IsValidIndex(idx) == true)
        {
          return idx;
        }
      }

      return -1;
    }

    public void RenameRoute(ref int routeIndex, string name, ref double aspectRatio)
    {
      if (_routeList?.IsValidIndex(routeIndex) == true)
      {
        var kvp = _routeList[routeIndex];

        Route rte;
        if (_patrolRoutes.TryGetValue(kvp.Key, out rte))
        {
          var stringId = MyStringId.GetOrCompute(name);

          int num = 0;
          while (_patrolRoutes.ContainsKey(stringId))
          {
            num++;
            stringId = MyStringId.GetOrCompute($"{name} ({num})");
          }

          _routeList[routeIndex] = new KeyValuePair<MyStringId, Button>(stringId, kvp.Value);
          _patrolRoutes.Remove(kvp.Key);
          _patrolRoutes[stringId] = rte;

          bool _;
          Update(aspectRatio, out _, false);
        }
      }
    }

    public bool SetSelected()
    {
      if (!HasFocus)
        return false;

      bool newSelect = false;
      for (int i = 0; i < _routeList.Count; i++)
      {
        var kvp = _routeList[i];
        var btn = kvp.Value;
        btn.SetSelected(btn.IsMouseOver);

        if (btn.IsSelected && (SelectedItem == null || SelectedItem.Index != btn.Index))
        {
          newSelect = true;
          SelectedItem = btn;
        }
      }

      return newSelect;
    }

    public bool SetMouseOver(ref Vector2D cursorPosition, ref double aspectRatio, ref bool autoFalse)
    {
      bool isWithinBounds = cursorPosition.IsWithinBounds(_background, aspectRatio);
      bool any = false;

      if (!isWithinBounds && !_wasWithinBounds)
        return false;

      autoFalse |= !isWithinBounds;
      _wasWithinBounds = isWithinBounds;

      if (cursorPosition.IsWithinBounds(ScrollBarBox.Background, aspectRatio))
      {
        var inScroll = !autoFalse && cursorPosition.IsWithinBounds(ScrollBar.Background, aspectRatio);
        ScrollBar.SetMouseOver(inScroll);
        autoFalse |= inScroll;
      }
      else
      {
        ScrollBar.SetMouseOver(false);
      }

      for (int i = 0; i < _routeList.Count; i++)
      {
        var kvp = _routeList[i];
        var btn = kvp.Value;
        var within = !autoFalse && btn.IsVisible && cursorPosition.IsWithinBounds(btn.Background, aspectRatio);
        if (within)
        {
          autoFalse = true;
          any = true;
        }

        btn.SetMouseOver(within);
      }

      HasFocus = any | cursorPosition.IsWithinBounds(_background, aspectRatio);
      return HasFocus;
    }

    public void UpdateScrollBarMouseWheel(ref int delta, ref double aspectRatio, bool adjustViewOffset = true)
    {
      if (_routeList.Count <= _maxVisibleRows)
        return;

      var signDelta = Math.Sign(delta);
      var canGoUp = delta > 0 && _firstVisibleIndex > 0;
      var canGoDown = delta < 0 && _lastVisibleIndex < _routeList.Count - 1;

      if (canGoUp || canGoDown)
      {
        var whole = -signDelta;
        UpdateItemPositions(ref whole, ref aspectRatio);
      }

      var boxSize = ScrollBarBox.Background.Height - 0.01f;
      var barSize = ScrollBar.Background.Height;
      var yDelta = (1f / (_routeList.Count - _maxVisibleRows)) * (boxSize - barSize) * signDelta;

      var offset = ScrollBar.Background.Offset;
      var offsetY = offset.Y + yDelta;
      var halfSize = barSize * 0.5f;

      var boxCenter = ScrollBarBox.Background.Origin.Y + ScrollBarBox.Background.Offset.Y;
      var boxHalfSize = boxSize * 0.5f;
      var top = boxCenter + boxHalfSize - halfSize - ScrollBar.Background.Origin.Y;
      var bottom = boxCenter - boxHalfSize + halfSize - ScrollBar.Background.Origin.Y;

      if (offsetY < bottom)
      {
        _buttonViewOffsetY = (float)(top - bottom);
        offsetY = bottom;
      }
      else if (offsetY > top)
      {
        _buttonViewOffsetY = 0;
        offsetY = top;
      }

      var finalDelta = -(float)(offsetY - offset.Y);

      if (finalDelta != 0)
      {
        ScrollBar.Background.Offset = new Vector2D(offset.X, offsetY);

        if (adjustViewOffset)
        {
          _buttonViewOffsetY = Math.Max(0, (float)Math.Round(_buttonViewOffsetY + finalDelta, 4));
        }
      }
    }

    float _deltaAddUp;
    public void UpdateScrollBar(ref float yDelta, ref double aspectRatio, bool adjustViewOffset = true)
    {
      if (_routeList.Count <= _maxVisibleRows)
        return;

      var boxSize = ScrollBarBox.Background.Height - 0.01f;
      var barSize = ScrollBar.Background.Height;
      var offset = ScrollBar.Background.Offset;
      var offsetY = offset.Y + yDelta;

      var boxCenter = ScrollBarBox.Background.Origin.Y + ScrollBarBox.Background.Offset.Y;
      var boxHalfSize = boxSize * 0.5;
      var halfSize = barSize * 0.5;
      var top = boxCenter + boxHalfSize - halfSize - ScrollBar.Background.Origin.Y;
      var bottom = boxCenter - boxHalfSize + halfSize - ScrollBar.Background.Origin.Y;

      if (offsetY < bottom)
      {
        _buttonViewOffsetY = (float)(top - bottom);
        _deltaAddUp = 0;
        offsetY = bottom;
      }
      else if (offsetY > top)
      {
        _buttonViewOffsetY = 0;
        _deltaAddUp = 0;
        offsetY = top;
      }

      var finalDelta = -(float)(offsetY - offset.Y);

      if (finalDelta != 0)
      {
        ScrollBar.Background.Offset = new Vector2D(offset.X, offsetY);

        if (adjustViewOffset)
        {
          _buttonViewOffsetY = Math.Max(0, (float)Math.Round(_buttonViewOffsetY + finalDelta, 4));
        }

        var canGoUp = yDelta > 0 && _firstVisibleIndex > 0;
        var canGoDown = yDelta < 0 && _lastVisibleIndex < _routeList.Count - 1;

        if (canGoUp || canGoDown)
        {
          _deltaAddUp += finalDelta;
          var leftOver = boxSize - barSize;
          var percentBar = Math.Abs(_deltaAddUp / leftOver);
          var rowsMoved = percentBar * (_routeList.Count - _maxVisibleRows + 1);
          var whole = (int)rowsMoved * Math.Sign(_deltaAddUp);

          if (whole != 0)
          {
            _deltaAddUp -= ((Math.Abs(whole) * _deltaAddUp) / rowsMoved);
            UpdateItemPositions(ref whole, ref aspectRatio);
          }
        }
        else
        {
          _deltaAddUp = 0;
        }
      }
    }

    void UpdateItemPositions(ref int yDelta, ref double aspectRatio)
    {
      _firstVisibleIndex = Math.Max(0, _firstVisibleIndex + yDelta);
      _firstVisibleIndex = Math.Max(0, Math.Min(_routeList.Count - _maxVisibleRows, _firstVisibleIndex));
      var maxIndex = _firstVisibleIndex + _maxVisibleRows;
      var firstOffset = new Vector2D((_border.Width * -0.05 + 0.0075) * aspectRatio, (_border.Height - _size.Y) * 0.5 - 0.01);
      var checkSelected = SelectedItem != null;
      var selectedFound = false;

      int num = 0;
      for (int i = 0; i < _routeList.Count; i++)
      {
        var kvp = _routeList[i];
        var btn = kvp.Value;
        var val = false;
        btn.SetVisibility(ref val);
        btn.Reset();
        btn.Index = i;

        if (i >= _firstVisibleIndex && i < maxIndex)
        {
          var offset = firstOffset - new Vector2D(0, _size.Y * num);
          btn.SetPositionAligned(ref offset, ref aspectRatio, TextAlignment.Left);

          val = true;
          btn.SetVisibility(ref val);
          _lastVisibleIndex = btn.Index;
          num++;
        }

        if (checkSelected && SelectedItem.Index == btn.Index)
        {
          selectedFound = true;
          checkSelected = false;
          SelectedItem = btn;
          btn.SetSelected(true);
        }
      }

      if (checkSelected && !selectedFound)
        SelectedItem = null;
    }

    public void SetVisibility(ref bool enable)
    {
      ScrollBar.SetVisibility(ref enable);
      ScrollBarBox.SetVisibility(ref enable);

      _background.Visible = enable;
      _border.Visible = enable;
      _header.Visible = enable;
      //_footer.Visible = enable;

      for (int i = 0; i < _routeList.Count; i++)
      {
        var kvp = _routeList[i];

        if (!enable || i < _maxVisibleRows)
          kvp.Value.SetVisibility(ref enable);
      }

      if (!enable)
      {
        _deltaAddUp = 0;
        _buttonViewOffsetY = 0;
        _firstVisibleIndex = 0;
        _lastVisibleIndex = int.MinValue;
        SelectedItem = null;
      }
    }

    public void Close()
    {
      ScrollBar?.Close();
      ScrollBarBox?.Close();
      _background?.DeleteMessage();
      _border?.DeleteMessage();
      _header?.DeleteMessage();
      _footer?.DeleteMessage();
      _routeStack?.Clear();
      _patrolRoutes?.Clear();
      _emitter?.Cleanup();

      if (_btnStack != null)
      {
        while (_btnStack.Count > 0)
        {
          var btn = _btnStack.Pop();
          btn?.Close();
        }

        _btnStack = null;
      }

      if (_routeList != null)
      {
        for (int i = 0; i < _routeList.Count; i++)
        {
          var kvp = _routeList[i];
          kvp.Value?.Close();
        }

        _routeList.Clear();
        _routeList = null;
      }

      ScrollBar = null;
      ScrollBarBox = null;
      _emitter = null;
      _soundPair = null;
      _background = null;
      _border = null;
      _header = null;
      _footer = null;
      _routeStack = null;
      _patrolRoutes = null;
    }
  }
}
