using AiEnabled.API;
using AiEnabled.Bots;
using AiEnabled.ConfigData;

using Sandbox.Game.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage;
using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Support
{
  public struct FutureBot
  {
    public HelperInfo HelperInfo;
    public long OwnerId;

    public FutureBot(HelperInfo info, long ownerId)
    {
      HelperInfo = info;
      OwnerId = ownerId;
    }
  }

  public class FutureBotAPI
  {
    public RemoteBotAPI.SpawnData SpawnData;
    public MyCubeGrid Grid;
    public long? Owner;
    public long SpawnId;
    public MyPositionAndOrientation PositionAndOrientation;
    public SerializableVector3I? LocalPosition;
    public Action<IMyCharacter, long> CallBackActionWithId;
    public Action<IMyCharacter> CallBackAction;
    bool _restack;

    public FutureBotAPI() { }

    public void SetInfo(MyPositionAndOrientation positionAndOrientation, RemoteBotAPI.SpawnData spawnData, long spawnId, MyCubeGrid grid = null, long? owner = null, Action<IMyCharacter> callBack = null)
    {
      SpawnData = spawnData;
      PositionAndOrientation = positionAndOrientation;
      Grid = grid;
      Owner = owner;
      SpawnId = spawnId;
      CallBackAction = callBack;
      LocalPosition = Grid?.WorldToGridInteger(positionAndOrientation.Position);
    }

    public void SetInfo(string subType, string displayName, MyPositionAndOrientation positionAndOrientation, long spawnId, MyCubeGrid grid = null, string role = null, long? owner = null, Color? color = null, Action<IMyCharacter> callback = null)
    {
      _restack = true;

      SpawnData = AiSession.Instance.SpawnDataStack.Count > 0 ? AiSession.Instance.SpawnDataStack.Pop() : new RemoteBotAPI.SpawnData();
      SpawnData.BotSubtype = subType;
      SpawnData.DisplayName = displayName;
      SpawnData.BotRole = role;
      SpawnData.Color = color;

      PositionAndOrientation = positionAndOrientation;
      Grid = grid;
      Owner = owner;
      SpawnId = spawnId;
      CallBackAction = callback;
      LocalPosition = Grid?.WorldToGridInteger(positionAndOrientation.Position);
    }
    public void SetInfo(MyPositionAndOrientation positionAndOrientation, RemoteBotAPI.SpawnData spawnData, long spawnId, MyCubeGrid grid = null, long? owner = null, Action<IMyCharacter, long> callBack = null)
    {
      SpawnData = spawnData;
      PositionAndOrientation = positionAndOrientation;
      Grid = grid;
      Owner = owner;
      SpawnId = spawnId;
      CallBackActionWithId = callBack;
      LocalPosition = Grid?.WorldToGridInteger(positionAndOrientation.Position);
    }

    public void SetInfo(string subType, string displayName, MyPositionAndOrientation positionAndOrientation, long spawnId, MyCubeGrid grid = null, string role = null, long? owner = null, Color? color = null, Action<IMyCharacter, long> callback = null)
    {
      _restack = true;

      SpawnData = AiSession.Instance.SpawnDataStack.Count > 0 ? AiSession.Instance.SpawnDataStack.Pop() : new RemoteBotAPI.SpawnData();
      SpawnData.BotSubtype = subType;
      SpawnData.DisplayName = displayName;
      SpawnData.BotRole = role;
      SpawnData.Color = color;

      PositionAndOrientation = positionAndOrientation;
      Grid = grid;
      Owner = owner;
      SpawnId = spawnId;
      CallBackActionWithId = callback;
      LocalPosition = Grid?.WorldToGridInteger(positionAndOrientation.Position);
    }

    public void Spawn()
    {
      IMyCharacter bot = null;

      if (SpawnData != null)
      {
        if (Grid != null && LocalPosition.HasValue)
        {
          PositionAndOrientation.Position = Grid.GridIntegerToWorld(LocalPosition.Value) - (Vector3)PositionAndOrientation.Up;
        }

        bot = BotFactory.SpawnBotFromAPI(PositionAndOrientation, SpawnData, Grid, Owner);

        if (_restack)
          AiSession.Instance.SpawnDataStack.Push(SpawnData);
      }

      CallBackActionWithId?.Invoke(bot, SpawnId);
      CallBackAction?.Invoke(bot);
      SpawnData = null;
      AiSession.Instance.FutureBotAPIStack.Push(this);
    }
  }
}
