using AiEnabled.Bots;
using AiEnabled.Networking.Packets;

using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.TSS
{
  [MyTextSurfaceScript("TSS_BotStatus", "[AiEnabled] Helper Status")]
  public class TSS_BotStatus : MyTSSCommon
  {
    public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update100;
    readonly float _scale;
    readonly string _font = "Debug";
    bool _setup, _shouldDraw, _resetDraw, _firstRun = true;
    Vector2 _sizePX;
    Vector2 _center;
    Vector2 _startPos;
    Vector2 _surfaceSize;
    List<MySprite> _sprites = new List<MySprite>(10);
    int _spriteIndex;

    public TSS_BotStatus(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
    {
      _scale = (block is IMyTextPanel) ? 0.6f : 0.5f;

      var builder = new StringBuilder("M");
      _sizePX = Surface.MeasureStringInPixels(builder, _font, _scale);
      _center = Surface.TextureSize * 0.5f;
      _surfaceSize = surface.SurfaceSize - 4;
      _startPos = _center - (_surfaceSize * 0.5f);

      Surface.BackgroundColor = Color.Black;
      Surface.ScriptBackgroundColor = Color.Black;

      UpdateStats(null);
      builder.Clear();
    }

    public override void Dispose()
    {
      try
      {
        _setup = false;

        _sprites?.Clear();
        _sprites = null;

        if (AiSession.Instance != null)
          AiSession.Instance.OnBotStatsUpdate -= UpdateStats;
      }
      catch (Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Exception in {GetType().FullName}.Dispose: {ex.Message}\n{ex.StackTrace}");
      }
      finally
      {
        base.Dispose();
      }
    }

    public override void Run()
    {
      try
      {
        base.Run();
        if (AiSession.Instance == null || !AiSession.Instance.Registered)
          return;

        if (!_setup)
        {
          AiSession.Instance.OnBotStatsUpdate += UpdateStats;
          _setup = true;
        }

        _shouldDraw = ShouldDisplay();

        if (_shouldDraw)
        {
          int count;
          AiSession.Instance.SendBotStatusRequest(out count);

          if (count > 0)
          {
            _resetDraw = false;
          }
          else if (count == 0 && !_resetDraw)
          {
            _resetDraw = true;
            UpdateStats(null);
          }
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Exception in {this.GetType().FullName}.Run: {ex.Message}\n{ex.StackTrace}");
      }
    }

    bool ShouldDisplay()
    {
      var player = MyAPIGateway.Session?.Player?.Character;
      if (player == null)
        return false;

      var cpit = Block as IMyCockpit;
      if (cpit != null && cpit.Pilot?.EntityId != player.EntityId)
        return false;

      return Vector3D.DistanceSquared(player.GetPosition(), Block.GetPosition()) < 32 * 32;
    }

    void UpdateStats(List<BotStatus> stats)
    {
      try
      {
        if (!_firstRun && !_shouldDraw)
          return;

        _firstRun = false;

        using (var frame = Surface.DrawFrame())
        {
          var header = $"Helper Status - {DateTime.Now:HH:mm:ss}";
          var color = Surface.ScriptForegroundColor;
          color.A = 125;

          var position = _startPos;
          var sprite = MySprite.CreateText(header, _font, color, _scale);
          sprite.Position = new Vector2(_center.X, position.Y);
          frame.Add(sprite);

          position += new Vector2(0, _sizePX.Y);
          sprite = MySprite.CreateSprite("SquareSimple", new Vector2(_center.X, position.Y + 1), new Vector2(_surfaceSize.X, 1));
          sprite.Color = color;
          frame.Add(sprite);
          position += new Vector2(0, 2);

          _sprites.Clear();

          if (stats?.Count > 0)
          {
            for (int i = 0; i < stats.Count; i++)
            {
              var stat = stats[i];
              var icon = MySprite.CreateSprite("Circle", Vector2.Zero, new Vector2(_sizePX.Y * 0.25f));
              icon.Color = color;
              _sprites.Add(icon);

              sprite = MySprite.CreateText($"[{i + 1}/{stats.Count}] {stat.BotName}", _font, color, _scale, TextAlignment.LEFT);
              _sprites.Add(sprite);

              sprite = MySprite.CreateText($"Action: {stat.Action}", _font, color, _scale, TextAlignment.LEFT);
              _sprites.Add(sprite);

              if (stat.NeededItem != null)
              {
                sprite = MySprite.CreateText($"Missing: {stat.NeededItem}", _font, color, _scale, TextAlignment.LEFT);
                _sprites.Add(sprite);
              }

              if (stat.TargetPosition.HasValue && stat.Action != "Idle" && !(Block is IMyCockpit))
              {
                Vector3D pos = Vector3D.Round(stat.TargetPosition.Value, 2);
                sprite = MySprite.CreateText($"Tgt Pos: {pos}", _font, color, _scale, TextAlignment.LEFT);
                _sprites.Add(sprite);
              }
            }

            var totalSizeY = _sprites.Count * _sizePX.Y + _sizePX.Y + 2;
            if (totalSizeY > _surfaceSize.Y)
            {
              bool skipTwo = false;
              for (int i = 0; i < _sprites.Count; i++)
              {
                var item = _sprites[(i + _spriteIndex) % _sprites.Count];
                if (item.Type == SpriteType.TEXTURE)
                {
                  if (i == 0)
                    skipTwo = true;

                  item.Position = position + new Vector2(_sizePX.Y * 0.25f, _sizePX.Y * 0.5f);
                }
                else
                {
                  item.Position = position + new Vector2(_sizePX.Y * 0.5f, 0);
                  position += new Vector2(0, _sizePX.Y);
                }

                frame.Add(item);

                if (position.Y - _startPos.Y + _sizePX.Y > _surfaceSize.Y)
                  break;
              }

              ++_spriteIndex;

              if (skipTwo)
                ++_spriteIndex;
            }
            else
            {
              _spriteIndex = 0;

              for (int i = 0; i < _sprites.Count; i++)
              {
                var item = _sprites[i];
                if (item.Type == SpriteType.TEXTURE)
                {
                  item.Position = position + new Vector2(_sizePX.Y * 0.25f, _sizePX.Y * 0.5f);
                }
                else
                {
                  item.Position = position + new Vector2(_sizePX.Y * 0.5f, 0);
                  position += new Vector2(0, _sizePX.Y);
                }

                frame.Add(item);
              }
            }
          }
          else
          {
            var icon = MySprite.CreateSprite("Circle", position + new Vector2(_sizePX.Y * 0.25f, _sizePX.Y * 0.5f), new Vector2(_sizePX.Y * 0.25f));
            icon.Color = color;
            frame.Add(icon);

            sprite = MySprite.CreateText("Waiting for active helpers...", _font, color, _scale, TextAlignment.LEFT);
            sprite.Position = position + new Vector2(_sizePX.Y * 0.5f, 0);
            frame.Add(sprite);
          }
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Exception in {this.GetType().FullName}.UpdateStats: {ex.Message}\n{ex.StackTrace}");
      }
    }
  }
}
