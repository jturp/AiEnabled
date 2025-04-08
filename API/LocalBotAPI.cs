using AiEnabled.Ai.Support;
using AiEnabled.Bots;
using AiEnabled.Bots.Roles;
using AiEnabled.Bots.Roles.Helpers;
using AiEnabled.Networking;
using AiEnabled.Parallel;
using AiEnabled.Support;
using AiEnabled.Utilities;

using ParallelTasks;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using SpaceEngineers.Game.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.API
{
  public class LocalBotAPI
  {
    private const long _botControllerModChannel = 2408831996; //This is the channel this object will send API methods to. Receiver should also use this.

    //Create Instance of this object in your SessionComponent BeforeStart() method.
    public LocalBotAPI()
    {
      var dict = BuildMethodDictionary();
      MyAPIGateway.Utilities.SendModMessage(_botControllerModChannel, dict);
    }

    public Dictionary<string, Delegate> BuildMethodDictionary()
    {

      var dict = new Dictionary<string, Delegate>
      {
        { "SpawnBot", new Func<string, string, MyPositionAndOrientation, MyCubeGrid, string, long?, Color?, IMyCharacter>(SpawnBot) },
        { "SpawnBotCustom", new Func<MyPositionAndOrientation, byte[], MyCubeGrid, long?, IMyCharacter>(SpawnBot)},
        { "SpawnBotQueued", new Action<string, string, MyPositionAndOrientation, MyCubeGrid, string, long?, Color?, Action<IMyCharacter>>(SpawnBotQueued)},
        { "SpawnBotCustomQueued", new Action<MyPositionAndOrientation, byte[], MyCubeGrid, long?, Action<IMyCharacter>>(SpawnBotQueued)},

        { "SpawnBotQueuedWithId", new Func<string, string, MyPositionAndOrientation, MyCubeGrid, string, long?, Color?, Action<IMyCharacter, long>, long>(SpawnBotQueuedWithId)},
        { "SpawnBotCustomQueuedWithId", new Func<MyPositionAndOrientation, byte[], MyCubeGrid, long?, Action<IMyCharacter, long>, long>(SpawnBotQueuedWithId)},
        { "GetFriendlyRoles", new Func<string[]>(GetFriendlyBotRoles) },
        { "GetNPCRoles", new Func<string[]>(GetNPCBotRoles) },
        { "GetNeutralRoles", new Func<string[]>(GetNeutralBotRoles) },
        { "GetBotSubtypes", new Func<string[]>(GetBotSubtypes) },
        { "CanSpawn", new Func<bool>(CanSpawn) },
        { "GetBotOverride", new Func<long, Vector3D?>(GetBotOverride) },
        { "SetBotOverride", new Func<long, Vector3D, bool>(SetBotOverride) },
        { "SetBotTarget", new Func<long, object, bool>(SetBotTarget) },
        { "ResetBotTargeting", new Func<long, bool>(ResetBotTargeting) },
        { "SetTargetAction", new Func<long, Action<long>, bool>(SetTargetAction) },
        { "SetOverrideAction", new Func<long, Action<long, bool>, bool>(SetOverrideAction) },
        { "TrySeatBot", new Func<long, IMyCockpit, bool>(TrySeatBot) },
        { "TrySeatBotOnGrid", new Func<long, IMyCubeGrid, bool>(TrySeatBotOnGrid) },
        { "TryRemoveBotFromSeat", new Func<long, bool>(TryRemoveBotFromSeat) },
        { "GetAvailableGridNodes", new Action<MyCubeGrid, int, List<Vector3D>, Vector3D?, bool>(GetAvailableGridNodes) },
        { "GetClosestValidNode", new Func<long, MyCubeGrid, Vector3I, Vector3D?, Vector3D?>(GetClosestValidNode) },
        { "GetClosestValidNodeNew", new Func<long, MyCubeGrid, Vector3D, Vector3D?, bool, Vector3D?>(GetClosestValidNode) },
        { "CreateGridMap", new Func<MyCubeGrid, MatrixD?, bool>(CreateGridMap) },
        { "IsGridMapReady", new Func<MyCubeGrid, bool>(IsGridMapReady) },
        { "RemoveBot", new Func<long, bool>(CloseBot) },
        { "Speak", new Action<long, string>(Speak) },
        { "Perform", new Action<long, string>(Perform) },
        { "IsBot", new Func<long, bool>(IsBot) },
        { "GetRelationshipBetween", new Func<long, long, MyRelationsBetweenPlayerAndBlock>(GetRelationshipBetween) },
        { "GetBotAndRelationTo", new Func<long, long, MyRelationsBetweenPlayerAndBlock?>(CheckBotRelationTo) },
        { "SetBotPatrol", new Func<long, List<Vector3D>, bool>(SetBotPatrol) },
        { "SetBotPatrolLocal", new Func<long, List<Vector3I>, bool>(SetBotPatrol) },
        { "CanRoleUseTool", new Func<string, string, bool>(CanRoleUseTool) },
        { "SwitchBotRole", new Func<long, byte[], bool>(SwitchBotRole) },
        { "SwitchBotRoleSlim", new Func<long, string, List<string>, bool>(SwitchBotRole) },
        { "GetBotOwnerId", new Func<long, long>(GetBotOwnerId) },
        { "SwitchBotWeapon", new Func<long, string, bool>(SwitchBotWeapon) },
        { "GetBots", new Action<Dictionary<long, IMyCharacter>, bool, bool, bool>(GetBots) },
        { "GetInteriorNodes", new Action<MyCubeGrid, List<Vector3I>, int, bool, bool, Action<IMyCubeGrid, List<Vector3I>>>(GetInteriorNodes) },
        { "IsValidForPathfinding", new Func<IMyCubeGrid, bool>(IsValidForPathfinding) },
        { "ReSyncBotCharacters", new Action<long, Action<List<IMyCharacter>>>(ReSyncBotCharacters) },
        { "ThrowGrenade", new Action<long>(ThrowGrenade) },
        { "GetLocalPositionForGrid", new Func<long, Vector3D, Vector3I?>(GetLocalPositionForGrid) },
        { "GetMainMapGrid", new Func<long, IMyCubeGrid>(GetMainMapGrid) },
        { "SetToolsEnabled", new Func<long, bool, bool>(SetToolsEnabled) },
        { "GetClosestSurfacePoint", new Func<Vector3D, MyVoxelBase, Vector3D?>(GetClosestSurfacePoint)},
        { "UpdateBotSpawnData", new Func<long, byte[], bool>(UpdateBotSpawnData) },
        { "GetGridMapMatrix", new Func<MyCubeGrid, bool, MatrixD?>(GetGridMapMatrix) },
        { "AssignToPlayer", new Func<long, long, bool>(AssignToPlayer) },
        { "FollowPlayer", new Func<long, long, bool>(FollowPlayer) },

      };

      return dict;
    }

    /// <summary>
    /// Event to trigger whenever bots deal damage.
    /// </summary>
    public event Action<long, long, float> OnDamageDealt;

    /// <summary>
    /// Triggers the event.
    /// </summary>
    /// <param name="attackerId">Bot Entity Id</param>
    /// <param name="targetId">Target Entity Id</param>
    /// <param name="damage">Damage amount</param>
    public void TriggerOnDamageDealt(long attackerId, long targetId, float damage) => OnDamageDealt?.Invoke(attackerId, targetId, damage);

    /// <summary>
    /// Check this BEFORE attempting to spawn a bot to ensure the mod is ready
    /// </summary>
    public bool CanSpawn() => AiSession.Instance?.CanSpawn ?? false;

    /// <summary>
    /// Retrieves the current set of available bot subtypes the mod will recognize.
    /// This allocates so grab it once and cache it!
    /// </summary>
    public string[] GetBotSubtypes() => AiSession.Instance?.RobotSubtypes?.ToArray() ?? null;

    /// <summary>
    /// Retrieves the current set of available friendly bot roles.
    /// This allocates so grab it once and cache it!
    /// </summary>
    public string[] GetFriendlyBotRoles() => Enum.GetNames(typeof(BotFactory.BotRoleFriendly));

    /// <summary>
    /// Retrieves the current set of available non-friendly bot roles.
    /// This allocates so grab it once and cache it!
    /// </summary>
    public string[] GetNPCBotRoles() => Enum.GetNames(typeof(BotFactory.BotRoleEnemy));

    /// <summary>
    /// Retrieves the current set of available neutral bot roles.
    /// This allocates so grab it once and cache it!
    /// </summary>
    public string[] GetNeutralBotRoles() => Enum.GetNames(typeof(BotFactory.BotRoleNeutral));

    /// <summary>
    /// Determines if the entity or player id belongs to an AiEnabled bot
    /// </summary>
    /// <param name="id">The EntityId or PlayerId of the Character</param>
    /// <returns>true if the Id belongs to a bot, otherwise false</returns>
    public bool IsBot(long id)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      if (AiSession.Instance.Bots.ContainsKey(id))
        return true;

      if (AiSession.Instance.Players.ContainsKey(id))
        return false;

      foreach (var kvp in AiSession.Instance.Bots)
      {
        var bot = kvp.Value;
        if (bot?.Character?.ControllerInfo?.ControllingIdentityId == id)
          return true;
      }

      return false;
    }

    /// <summary>
    /// Determins if the bot role is allowed to use a given tool type
    /// </summary>
    /// <param name="role">the BotRole to check (ie SoldierBot, GrinderBot, etc)</param>
    /// <param name="toolSubtype">the SubtypeId of the weapon or tool</param>
    /// <returns>true if the role is allowed to use the item, otherwise false</returns>
    public bool CanRoleUseTool(string role, string toolSubtype) => AiSession.Instance?.IsBotAllowedToUse(role, toolSubtype) ?? false;

    /// <summary>
    /// Retrieves the relationship between an AiEnabled bot and an identity
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="otherIdentityId">The IdentityId to check against</param>
    /// <returns>MyRelationsBetweenPlayerAndBlock, default is NoOwnership</returns>
    public MyRelationsBetweenPlayerAndBlock GetRelationshipBetween(long botEntityId, long otherIdentityId)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return MyRelationsBetweenPlayerAndBlock.NoOwnership;

      BotBase bot;
      if (AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) && bot?.Character != null && !bot.IsDead)
      {
        if (otherIdentityId == bot.Owner?.IdentityId)
          return MyRelationsBetweenPlayerAndBlock.Friends;

        var botOwnerId = bot.BotIdentityId;
        var shareMode = bot.Owner != null ? MyOwnershipShareModeEnum.Faction : MyOwnershipShareModeEnum.None;
        return MyIDModule.GetRelationPlayerBlock(botOwnerId, otherIdentityId, shareMode);
      }

      return MyRelationsBetweenPlayerAndBlock.NoOwnership;
    }

    /// <summary>
    /// Determines if an entity id belongs to an AiEnabled bot, and retrieves its relationship to an identity if so
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="otherIdentityId">The IdentityId to check against</param>
    /// <returns>the relation found if it's an AiEnabled bot, otherwise null</returns>
    public MyRelationsBetweenPlayerAndBlock? CheckBotRelationTo(long botEntityId, long otherIdentityId)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return null;

      BotBase bot;
      if (AiSession.Instance.Bots.TryGetValue(botEntityId, out bot))
      {
        var relationBetween = MyRelationsBetweenPlayerAndBlock.NoOwnership;

        if (bot?.Character != null && !bot.IsDead)
        {
          if (otherIdentityId == bot.Owner?.IdentityId)
          {
            relationBetween = MyRelationsBetweenPlayerAndBlock.Friends;
          }
          else
          {
            var botOwnerId = bot.BotIdentityId;
            var shareMode = bot.Owner != null ? MyOwnershipShareModeEnum.Faction : MyOwnershipShareModeEnum.None;
            relationBetween = MyIDModule.GetRelationPlayerBlock(botOwnerId, otherIdentityId, shareMode);
          }
        }

        return relationBetween;
      }

      return null;
    }

    /// <summary>
    /// Attempts to assign ownership of the bot to the player
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="playerIdentityId">The IdentityId of the Player to take ownership</param>
    /// <returns>true if the change is successful, otherwise false</returns>
    public bool AssignToPlayer(long botEntityId, long playerIdentityId)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot.IsDead)
        return false;

      BotFactory.ResetBotTargeting(bot);
      var player = AiSession.Instance.Players?.GetValueOrDefault(playerIdentityId, null);
      var curOwner = bot.Owner;
      bot.Owner = player;

      List<BotBase> helpers;
      if (player != null)
      {
        bot.FollowMode = true;

        if (!AiSession.Instance.PlayerToHelperDict.TryGetValue(playerIdentityId, out helpers))
        {
          helpers = new List<BotBase>();
          AiSession.Instance.PlayerToHelperDict[playerIdentityId] = helpers;
        }

        helpers.Add(bot);

        var packet = new SpawnPacketClient(bot.Character.EntityId, false);
        AiSession.Instance.Network.SendToPlayer(packet, player.SteamUserId);
      }
      else if (curOwner != null && AiSession.Instance.PlayerToHelperDict.TryGetValue(curOwner.IdentityId, out helpers))
      {
        for (int i = helpers.Count - 1; i >= 0;)
        {
          if (helpers[i]?.BotIdentityId == bot.BotIdentityId)
          {
            helpers.RemoveAtFast(i);
            var packet = new SpawnPacketClient(bot.Character.EntityId, true);
            AiSession.Instance.Network.SendToPlayer(packet, player.SteamUserId);
            break;
          }
        }
      }

      return player == null ? curOwner != null : curOwner == null;
    }

    /// <summary>
    /// Adds a callback method whenever bots deal damage. AiEnabled will unregister all delegates when it unloads. 
    /// This will fire *after* all SE Damage Handlers are finished, and only if the damage amount > 0.
    /// </summary>
    /// <param name="methodToCall">The method the event will invoke. Param 1 = Bot Entity Id (attacker), Param 2 = Target Entity Id, Param 3 = damage amount</param>
    /// <returns>true if the event registration succeeds, otherwise false</returns>
    public bool RegisterDamageHandler(Action<long, long, float> methodToCall)
    {
      OnDamageDealt += methodToCall;
      return true;
    }

    /// <summary>
    /// Unregister all damage handlers.
    /// </summary>
    public void UnregisterDamageHandlers()
    {
      try
      {
        if (OnDamageDealt != null)
        {
          List<Action<long, long, float>> actions = new List<Action<long, long, float>>();

          foreach (var action in OnDamageDealt.GetInvocationList())
          {
            actions.Add((Action<long, long, float>)action);
          }

          foreach (var action in actions)
          {
            OnDamageDealt -= action;
          }

          actions = null;
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance?.Logger?.Log(ex.ToString());
      }
    }

    /// <summary>
    /// Attempts to have the bot follow the player
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="playerIdentityId">The IdentityId of the Player to follow</param>
    /// <returns>true if the change is successful, otherwise false</returns>
    public bool FollowPlayer(long botEntityId, long playerIdentityId)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot.IsDead)
        return false;

      BotFactory.ResetBotTargeting(bot);

      var player = playerIdentityId > 0 ? AiSession.Instance.Players?.GetValueOrDefault(playerIdentityId, null) : null;
      var curOwner = bot.Owner;
      bot.Owner = player;

      List<BotBase> helpers;

      if (curOwner != null && AiSession.Instance.PlayerToHelperDict.TryGetValue(curOwner.IdentityId, out helpers))
      {
        for (int i = helpers.Count - 1; i >= 0;)
        {
          if (helpers[i]?.BotIdentityId == bot.BotIdentityId)
          {
            helpers.RemoveAtFast(i);
            break;
          }
        }
      }

      if (player != null)
      {
        bot.FollowMode = true;

        if (!AiSession.Instance.PlayerToHelperDict.TryGetValue(playerIdentityId, out helpers))
        {
          helpers = new List<BotBase>();
          AiSession.Instance.PlayerToHelperDict[playerIdentityId] = helpers;
        }

        helpers.Add(bot);
      }

      return player == null ? curOwner != null : curOwner == null;
    }

    /// <summary>
    /// Attempts to have a bot speak some phrase
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="phrase">The name of the phrase (leave null to use a random phrase)</param>
    public void Speak(long botEntityId, string phrase = null)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return;

      BotBase bot;
      if (AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) && bot?.Behavior != null && !bot.IsDead)
      {
        bot.Behavior.Speak(phrase);
      }
    }

    /// <summary>
    /// Attempts to have a bot perform some action
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="action">The name of the action to perform (leave null to use a random action)</param>
    public void Perform(long botEntityId, string action = null)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return;

      BotBase bot;
      if (AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) && bot?.Behavior != null && !bot.IsDead)
      {
        bot.Behavior.Perform(action);
      }
    }

    /// <summary>
    /// Determines if a grid map is ready to be used. Note that when blocks are added or removed, the grid is reprocessed!
    /// </summary>
    /// <param name="grid">The grid to check</param>
    /// <returns>true if a grid is ready to use, otherwise false</returns>
    public bool IsGridMapReady(MyCubeGrid grid)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose)
        return false;

      CubeGridMap gridMap;
      return AiSession.Instance.GridGraphDict.TryGetValue(grid.EntityId, out gridMap) && gridMap != null/* && gridMap.Ready*/;
    }

    /// <summary>
    /// Used to generate new characters in order to relieve the invisible bot issue. USE ONLY IN MULTIPLAYER!
    /// </summary>
    /// <param name="gridEntityId">the EntityId of the grid to work on</param>
    /// <param name="callBackAction">this method will be called at some point in the future, after all bots are created and seated. The list will contain all new bot characters.</param>
    public void ReSyncBotCharacters(long gridEntityId, Action<List<IMyCharacter>> callBackAction)
    {
      if (!MyAPIGateway.Multiplayer.MultiplayerActive)
        return;

      var grid = MyEntities.GetEntityById(gridEntityId) as MyCubeGrid;

      if (grid == null || grid.MarkedForClose)
        return;

      List<IMyCharacter> newBotList = AiSession.Instance.CharacterListPool.Get();

      var occupiedSeats = grid.OccupiedBlocks.ToList();

      for (int i = 0; i < occupiedSeats.Count; i++)
      {
        var block = occupiedSeats[i];
        var pilot = block?.Pilot as IMyCharacter;

        if (pilot == null || pilot.IsDead)
        {
          continue;
        }

        BotBase bot;
        if (AiSession.Instance.Bots.TryGetValue(pilot.EntityId, out bot) && bot != null && !bot.IsDead)
        {
          var newBot = BotFactory.SwitchBotCharacter(bot);
          if (newBot != null && newBotList != null)
            newBotList.Add(newBot);
        }
      }

      AiSession.Instance.Scheduler.Schedule(() => ExecuteCallBack(newBotList, callBackAction), 10);
    }

    void ExecuteCallBack(List<IMyCharacter> newBotList, Action<List<IMyCharacter>> callBack)
    {
      if (newBotList?.Count > 0)
      {
        for (int i = newBotList.Count - 1; i >= 0; i--)
        {
          var ch = newBotList[i];
          if (ch == null || ch.IsDead || ch.Parent == null)
            newBotList.RemoveAtFast(i);
        }
      }

      callBack?.Invoke(newBotList);

      if (newBotList != null)
      {
        AiSession.Instance.CharacterListPool?.Return(ref newBotList);
      }
    }

    /// <summary>
    /// Attempts to retrieve the world orientation for the main grid that would be used for pathing. This may not be the same grid that is passed into the method.
    /// HINT: Use this as the orientation for bots spawned on this grid!
    /// </summary>
    /// <param name="grid">the grid that you want to get the map orientation for</param>
    /// <param name="checkForMainGrid">if true, check for other grids that may be more suitable as the map's main grid</param>
    /// <returns>The world orientation the grid's map will use, or null if no suitable main grid is found</returns>
    public MatrixD? GetGridMapMatrix(MyCubeGrid grid, bool checkForMainGrid = true)
    {
      if (AiSession.Instance == null || !AiSession.Instance.Registered || grid == null)
        return null;

      if (checkForMainGrid)
        grid = GetMainMapGrid(grid.EntityId) as MyCubeGrid;

      if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose)
        return null;

      CubeGridMap gridMap;
      if (AiSession.Instance.GridGraphDict.TryGetValue(grid.EntityId, out gridMap) && gridMap.IsValid)
        return gridMap.WorldMatrix;

      Vector3D upVector = grid.WorldMatrix.Up;

      int numSeats;
      if (grid.HasMainCockpit())
      {
        upVector = grid.MainCockpit.WorldMatrix.Up;
      }
      else if (grid.HasMainRemoteControl())
      {
        upVector = grid.MainRemoteControl.WorldMatrix.Up;
      }
      else if (grid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_Cockpit), out numSeats) && numSeats > 0
        || grid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_ShipController), out numSeats) && numSeats > 0)
      {
        foreach (var b in grid.GetFatBlocks())
        {
          if (b is IMyShipController || b is IMyCockpit)
          {
            upVector = b.WorldMatrix.Up;
            break;
          }
        }
      }
      else
      {
        float _;
        var gridPos = grid.PositionComp.WorldAABB.Center;
        var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(gridPos, out _);
        var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(gridPos, 0);

        if (aGrav.LengthSquared() > 0)
        {
          upVector = Vector3D.Normalize(-aGrav);
        }
        else if (nGrav.LengthSquared() > 0)
        {
          upVector = Vector3D.Normalize(-nGrav);
        }
        else
        {
          upVector = grid.WorldMatrix.Up;
        }
      }

      var rotationMatrix = MatrixD.CreateWorld(grid.PositionComp.WorldAABB.Center, Vector3D.CalculatePerpendicularVector(upVector), upVector);
      return rotationMatrix;
    }


    /// <summary>
    /// Starts processing a grid to be used as a grid map, if it doesn't already exist
    /// </summary>
    /// <param name="grid">The grid to process</param>
    /// <param name="rotationMatrix">The matrix to use to determine proper orientation for the grid map</param>
    /// <returns>true if a map exists or if able to create one, otherwise false</returns>
    public bool CreateGridMap(MyCubeGrid grid, MatrixD? rotationMatrix = null)
    {
      if (AiSession.Instance == null | !AiSession.Instance.Registered)
        return false;

      if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose)
        return false;

      if (rotationMatrix == null)
      {
        rotationMatrix = GetGridMapMatrix(grid, false);
      }

      var map = AiSession.Instance.GetGridGraph(grid, rotationMatrix.Value);
      return map != null;
    }

    /// <summary>
    /// Attempts to get the closest valid node to a given grid position
    /// </summary>
    /// <param name="grid">The grid the position is on</param>
    /// <param name="startPosition">The position you want to get a nearby node for</param>
    /// <param name="upVec">If supplied, the returned node will be confined to nodes on the same level as the start position</param>
    /// <returns>the valid world position if found, otherwise null</returns>
    [Obsolete("Use the overload that takes in a Vector3D for startPosition to avoid subgrid issues")]
    public Vector3D? GetClosestValidNode(long botEntityId, MyCubeGrid grid, Vector3I startPosition, Vector3D? upVec)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return null;

      if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose)
        return null;

      CubeGridMap gridMap;
      if (!AiSession.Instance.GridGraphDict.TryGetValue(grid.EntityId, out gridMap) || gridMap?.MainGrid == null || !gridMap.Ready)
        return null;

      bool isSlim = gridMap.DoesBlockExist(startPosition); // gridMap.MainGrid.GetCubeBlock(startPosition) != null;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot))
        return null;

      Vector3I node;
      if (gridMap.GetClosestValidNode(bot, startPosition, out node, upVec, isSlim, currentIsDenied: false))
      {
        var validWorldPosition = gridMap.LocalToWorld(node);
        return validWorldPosition;
      }

      return null;
    }

    /// <summary>
    /// Attempts to get the closest valid node to a given grid position
    /// </summary>
    /// <param name="grid">The grid the position is on</param>
    /// <param name="startPosition">The world position you want to get a nearby node for</param>
    /// <param name="upVec">If supplied, the returned node will be confined to nodes on the same level as the start position</param>
    /// <returns>the valid world position if found, otherwise null</returns>
    public Vector3D? GetClosestValidNode(long botEntityId, MyCubeGrid grid, Vector3D startPosition, Vector3D? upVec, bool allowAirNodes = true)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return null;

      if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose)
        return null;

      CubeGridMap gridMap;
      if (!AiSession.Instance.GridGraphDict.TryGetValue(grid.EntityId, out gridMap) || gridMap?.MainGrid == null || !gridMap.Ready)
        return null;

      var localStart = gridMap.WorldToLocal(startPosition);
      bool isSlim = gridMap.DoesBlockExist(localStart); // gridMap.MainGrid.GetCubeBlock(startPosition) != null;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot))
        return null;

      Vector3I node;
      if (gridMap.GetClosestValidNode(bot, localStart, out node, upVec, isSlim, currentIsDenied: false, allowAirNodes: allowAirNodes))
      {
        var validWorldPosition = gridMap.LocalToWorld(node);
        return validWorldPosition;
      }

      return null;
    }

    /// <summary>
    /// Updates the bot's data associated with <see cref="SpawnData"/>. 
    /// Does NOT change data associated with the character itself (color, subtype, etc), nor does it change the bot's Role.
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="spawnData">The serialized <see cref="RemoteBotAPI.SpawnData"/> object</param>
    /// <returns>true if the change is successful, otherwise false</returns>
    public bool UpdateBotSpawnData(long botEntityId, byte[] spawnData) => SwitchBotRole(botEntityId, spawnData);

    /// <summary>
    /// Attempts to find the closest surface point to the provided world position by checking up and down from that point. 
    /// Does nothing if the position is not near a voxel body (planet or asteroid).
    /// </summary>
    /// <param name="startPosition">the position to check from</param>
    /// <param name="voxel">a planet or asteroid</param>\
    /// <returns>the surface position if it exists, otherwise null</returns>
    public Vector3D? GetClosestSurfacePoint(Vector3D startPosition, MyVoxelBase voxel)
    {
      if (AiSession.Instance == null || !AiSession.Instance.Registered)
      {
        AiSession.Instance?.Logger?.Log($"AiEnabled API received UpdateBotSpawnData before mod is ready", MessageType.WARNING);
        return null;
      }

      return GridBase.GetClosestSurfacePointAboveGround(ref startPosition, voxel: voxel);
    }

    /// <summary>
    /// This method will spawn a Bot with Custom Behavior
    /// </summary>
    /// <param name="subType">The SubtypeId of the Bot you want to Spawn (see <see cref="GetBotSubtypes"/>)</param>
    /// <param name="displayName">The DisplayName of the Bot</param>
    /// <param name="role">Bot Role: see <see cref="GetFriendlyBotRoles"/>, <see cref="GetNPCBotRoles"/>, or <see cref="GetNeutralBotRoles"/>. If not supplied, it will be determined by the subType's default usage</param>
    /// <param name="positionAndOrientation">Position and Orientation</param>
    /// <param name="grid">If supplied, the Bot will start with a Cubegrid Map for pathfinding, otherwise a Voxel Map</param>
    /// <param name="owner">Owner / Identity of the Bot (if a HelperBot)</param>
    /// <param name="color">The color for the bot in RGB format</param>
    /// <returns>The IMyCharacter created for the Bot, or null if unsuccessful</returns>
    [Obsolete("This method can cause lag, use SpawnBotQueued instead.")]
    public IMyCharacter SpawnBot(string subType, string displayName, MyPositionAndOrientation positionAndOrientation, MyCubeGrid grid = null, string role = null, long? owner = null, Color? color = null)
    {
      if (AiSession.Instance?.CanSpawn != true)
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command before mod was ready to spawn bots.");
        return null;
      }

      if (grid != null && (grid.Physics == null || grid.IsPreview))
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command with grid that has null physics.", MessageType.WARNING);
        return null;
      }

      AiSession.Instance.Logger.Log($"AiEnabled API received obsolete SpawnBot command. This method can cause lag and should be replaced with SpawnBotQueued.", MessageType.WARNING);
      return BotFactory.SpawnBotFromAPI(subType, displayName, positionAndOrientation, grid, role, owner, color);
    }

    /// <summary>
    /// This method will spawn a Bot with Custom Behavior
    /// </summary>
    /// <param name="displayName">The DisplayName of the Bot</param>
    /// <param name="positionAndOrientation">Position and Orientation</param>
    /// <param name="spawnData">The serialized <see cref="RemoteBotAPI.SpawnData"/> object</param>
    /// <param name="grid">If supplied, the Bot will start with a Cubegrid Map for pathfinding, otherwise a Voxel Map</param>
    /// <param name="owner">Owner's IdentityId for the Bot (if a HelperBot)</param>
    /// <returns>The IMyCharacter created for the Bot, or null if unsuccessful</returns>
    [Obsolete("This method can cause lag, use SpawnBotQueued instead.")]
    public IMyCharacter SpawnBot(MyPositionAndOrientation positionAndOrientation, byte[] spawnData, MyCubeGrid grid = null, long? owner = null)
    {
      if (AiSession.Instance?.CanSpawn != true)
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command before mod was ready to spawn bots.", MessageType.WARNING);
        return null;
      }

      if (grid != null && (grid.Physics == null || grid.IsPreview))
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command with grid that has null physics.", MessageType.WARNING);
        return null;
      }

      var data = MyAPIGateway.Utilities.SerializeFromBinary<RemoteBotAPI.SpawnData>(spawnData);
      if (data == null)
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command with malformed SpawnData object.", MessageType.WARNING);
        return null;
      }

      AiSession.Instance.Logger.Log($"AiEnabled API received obsolete SpawnBot command. This method can cause lag and should be replaced with SpawnBotQueued.", MessageType.WARNING);
      return BotFactory.SpawnBotFromAPI(positionAndOrientation, data, grid, owner);
    }

    /// <summary>
    /// This method will queue a Bot to be spawned with custom behavior
    /// </summary>
    /// <param name="subType">The SubtypeId of the Bot you want to Spawn (see <see cref="GetBotSubtypes"/>)</param>
    /// <param name="displayName">The DisplayName of the Bot</param>
    /// <param name="role">Bot Role: see <see cref="GetFriendlyBotRoles"/>, <see cref="GetNPCBotRoles"/>, or <see cref="GetNeutralBotRoles"/>. If not supplied, it will be determined by the subType's default usage</param>
    /// <param name="positionAndOrientation">Position and Orientation</param>
    /// <param name="grid">If supplied, the Bot will start with a Cubegrid Map for pathfinding, otherwise a Voxel Map</param>
    /// <param name="owner">Owner / Identity of the Bot (if a HelperBot)</param>
    /// <param name="color">The color for the bot in RGB format</param>
    /// <param name="callBack">The callback method to invoke when the bot is spawned</param>
    /// <returns>The IMyCharacter created for the Bot, or null if unsuccessful, in a callback method</returns>
    public void SpawnBotQueued(string subType, string displayName, MyPositionAndOrientation positionAndOrientation, MyCubeGrid grid = null, string role = null, long? owner = null, Color? color = null, Action<IMyCharacter> callBack = null)
    {
      if (AiSession.Instance?.CanSpawn != true)
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command before mod was ready to spawn bots.", MessageType.WARNING);
        return;
      }

      if (grid != null && (grid.Physics == null || grid.IsPreview))
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command with grid that has null physics.", MessageType.WARNING);
        return;
      }

      var spawnId = AiSession.Instance.LastSpawnId++;
      var future = AiSession.Instance.FutureBotAPIStack.Count > 0 ? AiSession.Instance.FutureBotAPIStack.Pop() : new FutureBotAPI();
      future.SetInfo(subType, displayName, positionAndOrientation, spawnId, grid, role, owner, color, callBack);
      AiSession.Instance.FutureBotAPIQueue.Enqueue(future);
    }

    /// <summary>
    /// This method will queue a Bot to be spawned with custom behavior
    /// </summary>
    /// <param name="positionAndOrientation">Position and Orientation</param>
    /// <param name="spawnData">The serialized <see cref="RemoteBotAPI.SpawnData"/> object</param>
    /// <param name="grid">If supplied, the Bot will start with a Cubegrid Map for pathfinding, otherwise a Voxel Map</param>
    /// <param name="owner">Owner's IdentityId for the Bot (if a HelperBot)</param>
    /// <param name="callBack">The callback method to invoke when the bot is spawned</param>
    /// <returns>The IMyCharacter created for the Bot, or null if unsuccessful, in a callback method</returns>
    public void SpawnBotQueued(MyPositionAndOrientation positionAndOrientation, byte[] spawnData, MyCubeGrid grid = null, long? owner = null, Action<IMyCharacter> callBack = null)
    {
      if (AiSession.Instance?.CanSpawn != true)
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command before mod was ready to spawn bots.", MessageType.WARNING);
        return;
      }

      if (grid != null && (grid.Physics == null || grid.IsPreview))
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command with grid that has null physics.", MessageType.WARNING);
        return;
      }

      var data = MyAPIGateway.Utilities.SerializeFromBinary<RemoteBotAPI.SpawnData>(spawnData);
      if (data == null)
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command with malformed SpawnData object.", MessageType.WARNING);
        return;
      }

      var spawnId = AiSession.Instance.LastSpawnId++;
      var future = AiSession.Instance.FutureBotAPIStack.Count > 0 ? AiSession.Instance.FutureBotAPIStack.Pop() : new FutureBotAPI();
      future.SetInfo(positionAndOrientation, data, spawnId, grid, owner, callBack);
      AiSession.Instance.FutureBotAPIQueue.Enqueue(future);
    }

    /// <summary>
    /// This method will queue a Bot to be spawned with custom behavior
    /// </summary>
    /// <param name="subType">The SubtypeId of the Bot you want to Spawn (see <see cref="GetBotSubtypes"/>)</param>
    /// <param name="displayName">The DisplayName of the Bot</param>
    /// <param name="role">Bot Role: see <see cref="GetFriendlyBotRoles"/>, <see cref="GetNPCBotRoles"/>, or <see cref="GetNeutralBotRoles"/>. If not supplied, it will be determined by the subType's default usage</param>
    /// <param name="positionAndOrientation">Position and Orientation</param>
    /// <param name="grid">If supplied, the Bot will start with a Cubegrid Map for pathfinding, otherwise a Voxel Map</param>
    /// <param name="owner">Owner / Identity of the Bot (if a HelperBot)</param>
    /// <param name="color">The color for the bot in RGB format</param>
    /// <param name="callBack">The callback method to invoke when the bot is spawned</param>
    /// <returns>The spawn id associated with the request, or -1 if invalid, 
    /// and the IMyCharacter created for the Bot, or null if unsuccessful, in a callback method</returns>
    public long SpawnBotQueuedWithId(string subType, string displayName, MyPositionAndOrientation positionAndOrientation, MyCubeGrid grid = null, string role = null, long? owner = null, Color? color = null, Action<IMyCharacter, long> callBack = null)
    {
      if (AiSession.Instance?.CanSpawn != true)
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command before mod was ready to spawn bots.", MessageType.WARNING);
        return -1;
      }

      if (grid != null && (grid.Physics == null || grid.IsPreview))
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command with grid that has null physics.", MessageType.WARNING);
        return -1;
      }

      var spawnId = AiSession.Instance.LastSpawnId++;
      var future = AiSession.Instance.FutureBotAPIStack.Count > 0 ? AiSession.Instance.FutureBotAPIStack.Pop() : new FutureBotAPI();
      future.SetInfo(subType, displayName, positionAndOrientation, spawnId, grid, role, owner, color, callBack);
      AiSession.Instance.FutureBotAPIQueue.Enqueue(future);

      return spawnId;
    }

    /// <summary>
    /// This method will queue a Bot to be spawned with custom behavior
    /// </summary>
    /// <param name="positionAndOrientation">Position and Orientation</param>
    /// <param name="spawnData">The serialized <see cref="RemoteBotAPI.SpawnData"/> object</param>
    /// <param name="grid">If supplied, the Bot will start with a Cubegrid Map for pathfinding, otherwise a Voxel Map</param>
    /// <param name="owner">Owner's IdentityId for the Bot (if a HelperBot)</param>
    /// <param name="callBack">The callback method to invoke when the bot is spawned</param>
    /// <returns>The spawn id associated with the request, or -1 if invalid, 
    /// and the IMyCharacter created for the Bot, or null if unsuccessful, in a callback method</returns>
    public long SpawnBotQueuedWithId(MyPositionAndOrientation positionAndOrientation, byte[] spawnData, MyCubeGrid grid = null, long? owner = null, Action<IMyCharacter, long> callBack = null)
    {
      if (AiSession.Instance?.CanSpawn != true)
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command before mod was ready to spawn bots.", MessageType.WARNING);
        return -1;
      }

      if (grid != null && (grid.Physics == null || grid.IsPreview))
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command with grid that has null physics.", MessageType.WARNING);
        return -1;
      }

      var data = MyAPIGateway.Utilities.SerializeFromBinary<RemoteBotAPI.SpawnData>(spawnData);
      if (data == null)
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SpawnBot command with malformed SpawnData object.", MessageType.WARNING);
        return -1;
      }

      var spawnId = AiSession.Instance.LastSpawnId++;
      var future = AiSession.Instance.FutureBotAPIStack.Count > 0 ? AiSession.Instance.FutureBotAPIStack.Pop() : new FutureBotAPI();
      future.SetInfo(positionAndOrientation, data, spawnId, grid, owner, callBack);
      AiSession.Instance.FutureBotAPIQueue.Enqueue(future);

      return spawnId;
    }

    /// <summary>
    /// Gets the current Overridden (GoTo) position for the Bot
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    public Vector3D? GetBotOverride(long botEntityId)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return null;

      BotBase bot;
      AiSession.Instance.Bots.TryGetValue(botEntityId, out bot);
      return bot?.Target?.Override;
    }

    /// <summary>
    /// Sets the Override (GoTo) position for the Bot
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="goTo">The World Position the Bot should path to</param>
    /// <returns>true if the override is set successfully, otherwise false</returns>
    public bool SetBotOverride(long botEntityId, Vector3D goTo)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot == null || bot.IsDead)
        return false;

      bot.UseAPITargets = true;
      bot.Target.SetOverride(goTo);
      bot.CleanPath();
      bot.CheckGraphNeeded = true;
      return true;
    }

    /// <summary>
    /// Assigns a patrol route to the Bot. In patrol mode, the bot will attack any enemies that come near its route.
    /// You must call <see cref="ResetBotTargeting(long)"/> for it to resume normal functions
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="waypoints">A list of world coordinates for the bot to patrol</param>
    /// <returns>true if the route is assigned successfully, otherwise false</returns>
    public bool SetBotPatrol(long botEntityId, List<Vector3D> waypoints)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      if (waypoints == null || waypoints.Count == 0)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?._currentGraph == null || bot.IsDead)
        return false;

      bot.UseAPITargets = false;
      bot.PatrolMode = true;
      bot.FollowMode = false;
      bot.Target.RemoveOverride(false);

      if (bot is RepairBot)
      {
        bot.Target.RemoveTarget();
      }

      bot.UpdatePatrolPoints(waypoints, "API Route");

      var seat = bot.Character.Parent as IMyCockpit;
      if (seat != null)
      {
        seat.RemovePilot();
        Vector3D relPosition;
        if (!AiSession.Instance.BotToSeatRelativePosition.TryGetValue(bot.Character.EntityId, out relPosition))
          relPosition = Vector3D.Forward * 2.5 + Vector3D.Up;

        var position = seat.GetPosition() + Vector3D.Rotate(relPosition, seat.WorldMatrix) + bot.WorldMatrix.Down;
        bot.Character.SetPosition(position);

        var jetpack = bot.Character.Components?.Get<MyCharacterJetpackComponent>();
        if (jetpack != null && bot.RequiresJetpack && !jetpack.TurnedOn)
        {
          var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
          MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
          jetpack.TurnOnJetpack(true);
          MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
        }
      }

      return true;
    }

    /// <summary>
    /// Assigns a patrol route to the Bot. In patrol mode, the bot will attack any enemies that come near its route.
    /// You must call <see cref="ResetBotTargeting(long)"/> for it to resume normal functions
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="waypoints">A list of grid-local coordinates for the bot to patrol</param>
    /// <returns>true if the route is assigned successfully, otherwise false</returns>
    public bool SetBotPatrol(long botEntityId, List<Vector3I> waypoints)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      if (waypoints == null || waypoints.Count == 0)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?._currentGraph == null || bot.IsDead)
        return false;

      bot.UseAPITargets = false;
      bot.PatrolMode = true;
      bot.FollowMode = false;
      bot.Target.RemoveOverride(false);

      if (bot is RepairBot)
      {
        bot.Target.RemoveTarget();
      }

      bot.UpdatePatrolPoints(waypoints, "API Route");

      var seat = bot.Character.Parent as IMyCockpit;
      if (seat != null)
      {
        seat.RemovePilot();
        Vector3D relPosition;
        if (!AiSession.Instance.BotToSeatRelativePosition.TryGetValue(bot.Character.EntityId, out relPosition))
          relPosition = Vector3D.Forward * 2.5 + Vector3D.Up;

        var position = seat.GetPosition() + Vector3D.Rotate(relPosition, seat.WorldMatrix) + bot.WorldMatrix.Down;
        bot.Character.SetPosition(position);

        var jetpack = bot.Character.Components?.Get<MyCharacterJetpackComponent>();
        if (jetpack != null && bot.RequiresJetpack && !jetpack.TurnedOn)
        {
          var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
          MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
          jetpack.TurnOnJetpack(true);
          MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
        }
      }

      return true;
    }

    /// <summary>
    /// Sets the Override Complete Action associated with a given Bot. 
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="action">The Action to perform when the current Overridden GoTo is nullified</param>
    /// <returns>true if the action is set successfully, otherwise false</returns>
    public bool SetOverrideAction(long botEntityId, Action<long, bool> action)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.IsDead != false)
        return false;

      bot.Target.OverrideComplete = action;
      return true;
    }

    /// <summary>
    /// Sets the Bots target and forces the Bot to use only API-provided targets. 
    /// You must call <see cref="ResetBotTargeting(long)"/> for it to resume autonomous targeting
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="target">The target can be any player, character, block, or grid. 
    /// DO NOT use a VECTOR as the target. To override the GoTo, use <see cref="SetBotOverride(long, Vector3D)"/></param>
    /// <returns>true if the target is set successfully, otherwise false</returns>
    public bool SetBotTarget(long botEntityId, object target)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.IsDead != false)
        return false;

      bot.UseAPITargets = true;
      bot.Target.SetTarget(bot.Owner, target);
      bot.CleanPath();
      return true;
    }

    /// <summary>
    /// Clears the Bot's current target and re-enables autonomous targeting
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <returns>true if targeting is successfully reset, otherwise false</returns>
    public bool ResetBotTargeting(long botEntityId)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.IsDead != false)
        return false;

      BotFactory.ResetBotTargeting(bot);
      return true;
    }

    /// <summary>
    /// Removed a bot from the world. Use this to ensure everything is cleaned up properly!
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <returns>true if the bot is successfully removed, otherwise false</returns>
    public bool CloseBot(long botEntityId)
    {
      if (AiSession.Instance == null || !AiSession.Instance.Registered || AiSession.Instance.Bots == null)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.IsDead != false)
        return false;

      bot.Close(true);
      return true;
    }

    /// <summary>
    /// Sets the Action to perform when the Bot's API target is removed. 
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="action">The Action to perform when the current API target is nullified</param>
    /// <returns>true if the action is set successfully, otherwise false</returns>
    public bool SetTargetAction(long botEntityId, Action<long> action)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.IsDead != false)
        return false;

      bot.Target.TargetRemoved = action;
      return true;
    }

    /// <summary>
    /// Attempts to place the Bot in the given Seat
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="seat">The Seat to place the Bot in</param>
    /// <returns>true if able to seat the Bot, otherwise false</returns>
    public bool TrySeatBot(long botEntityId, IMyCockpit seat)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      if (seat == null || seat.MarkedForClose || !seat.IsFunctional)
        return false;

      if (seat.Pilot != null)
        return seat.Pilot.EntityId == botEntityId;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.Character == null || bot.IsDead)
        return false;

      var botParent = bot.Character.Parent as IMyCockpit;
      if (botParent != null)
      {
        if (botParent.EntityId == seat.EntityId)
          return true;
        
        botParent.RemovePilot();
      }

      if (!seat.HasPlayerAccess(bot.BotIdentityId))
        return false;

      return BotFactory.TrySeatBot(bot, seat);
    }

    /// <summary>
    /// Attempts to place the Bot in the first open seat on the Grid
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="grid">The IMyCubeGrid to find a seat on</param>
    /// <returns>true if able to seat the Bot, otherwise false</returns>
    public bool TrySeatBotOnGrid(long botEntityId, IMyCubeGrid grid)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      if (grid?.Physics == null || grid.MarkedForClose)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.Character == null || bot.IsDead)
        return false;

      var botParent = bot.Character.Parent as IMyCockpit;
      if (botParent != null)
      {
        if (botParent.CubeGrid.EntityId == grid.EntityId)
          return true;
        else
          botParent.RemovePilot();
      }

      return BotFactory.TrySeatBotOnGrid(bot, grid);
    }

    /// <summary>
    /// Attempts to remove the Bot from its seat
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <returns>true if able to remove the Bot from its seat, otherwise false</returns>
    public bool TryRemoveBotFromSeat(long botEntityId)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.Character == null || bot.IsDead)
        return false;

      if (!(bot.Character.Parent is IMyCockpit))
        return true;

      return BotFactory.RemoveBotFromSeat(bot);
    }

    /// <summary>
    /// Determines whether or not the supplied grid can be walked on by AiEnabled bots (ie it has at least one large grid connected to it)
    /// </summary>
    /// <param name="grid">the initial grid to check</param>
    /// <returns>true if the grid can be used by AiEnabled bots, otherwise false</returns>
    public bool IsValidForPathfinding(IMyCubeGrid grid)
    {
      if (AiSession.Instance == null || !AiSession.Instance.Registered)
        return false;

      return GridBase.GetLargestGridForMap(grid) != null;
    }

    /// <summary>
    /// Attempts to find interior nodes based on how many airtight blocks are found in all directions of a given point
    /// </summary>
    /// <param name="grid">The main grid. All connected grids will also be considered.</param>
    /// <param name="nodeList">The list to fill with local points. Convert to world using mainGrid.GridIntegerToWorld(point).</param>
    /// <param name="enclosureRating">How many sides need to be found to consider a point inside. Default is 5.</param>
    /// <param name="allowAirNodes">Whether or not to consider air nodes for inside-ness.</param>
    /// <param name="onlyAirtightNodes">Whether or not to only accept airtight nodes.</param>
    /// <param name="callBack">The Action to be invoked when the thread finishes</param>
    public void GetInteriorNodes(MyCubeGrid grid, List<Vector3I> nodeList, int enclosureRating = 5, bool allowAirNodes = true, bool onlyAirtightNodes = false, Action<IMyCubeGrid, List<Vector3I>> callBack = null)
    {
      if (AiSession.Instance == null || !AiSession.Instance.Registered)
      {
        callBack?.Invoke(grid, nodeList);
        return;
      }

      ApiWorkData data = AiSession.Instance.ApiWorkDataPool.Get();

      data.Grid = grid;
      data.NodeList = nodeList;
      data.EnclosureRating = enclosureRating;
      data.AirtightNodesOnly = onlyAirtightNodes;
      data.AllowAirNodes = allowAirNodes;
      data.CallBack = callBack;

      MyAPIGateway.Parallel.Start(BotFactory.GetInteriorNodes, BotFactory.GetInteriorNodesCallback, data);
    }

    /// <summary>
    /// Attempts to transform world to local using the proper grid for a given Grid Map. Check null!
    /// </summary>
    /// <param name="gridEntityId">the EntityId for any grid in a Grid Map</param>
    /// <param name="worldPosition">the World Position to transform</param>
    /// <returns>the local position if everything is valid, otherwise null</returns>
    public Vector3I? GetLocalPositionForGrid(long gridEntityId, Vector3D worldPosition)
    {
      if (AiSession.Instance == null || !AiSession.Instance.Registered)
      {
        return null;
      }

      var grid = MyEntities.GetEntityById(gridEntityId) as IMyCubeGrid;
      if (grid != null)
      {
        CubeGridMap gridMap;
        var biggest = GridBase.GetLargestGridForMap(grid);
        if (biggest != null && AiSession.Instance.GridGraphDict.TryGetValue(biggest.EntityId, out gridMap))
        {
          return gridMap?.WorldToLocal(worldPosition);
        }
      }

      return null;
    }

    /// <summary>
    /// Attempts to retrieve the main grid used for position transformations for a given grid map. Check null!
    /// </summary>
    /// <param name="gridEntityId">the EntityId for any grid in a Grid Map</param>
    /// <returns>the IMyCubeGrid to use for a map's position transformations, if a map exists, otherwise null</returns>
    public IMyCubeGrid GetMainMapGrid(long gridEntityId)
    {
      if (AiSession.Instance == null || !AiSession.Instance.Registered)
      {
        return null;
      }

      var grid = MyEntities.GetEntityById(gridEntityId) as IMyCubeGrid;
      if (grid != null)
      {
        CubeGridMap gridMap;
        var biggest = GridBase.GetLargestGridForMap(grid);
        if (biggest != null && AiSession.Instance.GridGraphDict.TryGetValue(biggest.EntityId, out gridMap))
        {
          return gridMap?.MainGrid;
        }
      }

      return null;
    }

    /// <summary>
    /// Attmepts to find valid grid nodes to spawn NPCs at. Check for null before iterating the returned list!
    /// </summary>
    /// <param name="grid">The grid to spawn on</param>
    /// <param name="numberOfNodesNeeded">The number of bots you want to spawn</param>
    /// <param name="upVector">The normalized Up direction for the grid, if known</param>
    /// <param name="onlyAirtightNodes">If only pressurized areas should be considered</param>
    /// <param name="nodeList">The list to fill with World Positions. This will only be valid for one frame unless grid doesn't move!</param>
    /// <returns></returns>
    public void GetAvailableGridNodes(MyCubeGrid grid, int numberOfNodesNeeded, List<Vector3D> nodeList, Vector3D? upVector = null, bool onlyAirtightNodes = false)
    {
      if (grid?.Physics == null || grid.IsPreview || grid.MarkedAsTrash || grid.MarkedForClose || grid.Closed)
        return;

      if (nodeList == null)
        nodeList = new List<Vector3D>(numberOfNodesNeeded);
      else
        nodeList.Clear();

      var biggestGrid = GridBase.GetLargestGridForMap(grid) as MyCubeGrid;
      if (biggestGrid == null || biggestGrid.Physics == null || biggestGrid.MarkedForClose || biggestGrid.Closed)
        return;

      int numSeats;
      if (biggestGrid.HasMainCockpit())
      {
        upVector = biggestGrid.MainCockpit.WorldMatrix.Up;
      }
      else if (biggestGrid.HasMainRemoteControl())
      {
        upVector = biggestGrid.MainRemoteControl.WorldMatrix.Up;
      }
      else if (biggestGrid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_Cockpit), out numSeats) && numSeats > 0)
      {
        foreach (var b in biggestGrid.GetFatBlocks())
        {
          if (b is IMyShipController || b is IMyCockpit)
          {
            upVector = b.WorldMatrix.Up;
            break;
          }
        }
      }
      else
      {
        float _;
        var gridPos = biggestGrid.PositionComp.WorldAABB.Center;
        var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(gridPos, out _);
        var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(gridPos, 0);

        if (aGrav.LengthSquared() > 0)
        {
          upVector = Vector3D.Normalize(-aGrav);
        }
        else if (nGrav.LengthSquared() > 0)
        {
          upVector = Vector3D.Normalize(-nGrav);
        }
        else
        {
          upVector = (upVector.HasValue && !Vector3D.IsZero(upVector.Value)) ? upVector : biggestGrid.WorldMatrix.Up;
        }
      }

      var normal = Base6Directions.GetIntVector(biggestGrid.WorldMatrix.GetClosestDirection(upVector.Value));
      var blocks = biggestGrid.GetBlocks().ToList();
      var positionList = AiSession.Instance.LineListPool.Get();

      foreach (IMySlimBlock block in blocks)
      {
        if (nodeList.Count >= numberOfNodesNeeded)
          break;

        var cubeDef = block.BlockDefinition as MyCubeBlockDefinition;

        bool isPassageStair = cubeDef.Context.ModName == "PassageIntersections" && cubeDef.Id.SubtypeName.EndsWith("PassageStairs_Large");
        if (isPassageStair && normal != Base6Directions.GetIntVector(block.Orientation.Up))
          continue;

        if (AiSession.Instance.GratedCatwalkExpansionBlocks.Contains(cubeDef.Id))
        {
          if (cubeDef.Id.SubtypeName.EndsWith("Raised"))
          {
            if (normal != Base6Directions.GetIntVector(block.Orientation.Up))
              continue;
          }
          else if (normal != -Base6Directions.GetIntVector(block.Orientation.Up))
          {
            continue;
          }
        }

        bool airTight = cubeDef.IsAirTight ?? false;

        bool allowSolar = false;
        if (!airTight && block.FatBlock is IMySolarPanel)
        {
          bool isColorable = block.BlockDefinition.Id.SubtypeName.IndexOf("colorable", StringComparison.OrdinalIgnoreCase) >= 0;
          Vector3I vecFwd = Base6Directions.GetIntVector(block.Orientation.Forward);

          if (isColorable)
          {
            allowSolar = vecFwd.Dot(ref normal) < 0;
          }
          else
          {
            allowSolar = vecFwd.Dot(ref normal) > 0;
          }
        }

        bool isFlatWindow = !allowSolar && AiSession.Instance.FlatWindowDefinitions.ContainsItem(cubeDef.Id);
        bool isCylinder = !isFlatWindow && AiSession.Instance.PipeBlockDefinitions.ContainsItem(cubeDef.Id);
        bool isAllowedConveyor = !isCylinder && AiSession.Instance.ConveyorFullBlockDefinitions.ContainsItem(cubeDef.Id);
        bool isAutomatonsFull = !isAllowedConveyor && AiSession.Instance.AutomatonsFullBlockDefinitions.ContainsItem(cubeDef.Id);
        bool isSlopeBlock = !isAutomatonsFull && AiSession.Instance.SlopeBlockDefinitions.Contains(cubeDef.Id)
          && !AiSession.Instance.SlopedHalfBlockDefinitions.Contains(cubeDef.Id)
          && !AiSession.Instance.HalfStairBlockDefinitions.Contains(cubeDef.Id);

        bool isScaffold = !isSlopeBlock && AiSession.Instance.ScaffoldBlockDefinitions.Contains(cubeDef.Id);
        bool exclude = isScaffold && (cubeDef.Id.SubtypeName.EndsWith("Open") || cubeDef.Id.SubtypeName.EndsWith("Structure"));
        bool isPlatform = isScaffold && !exclude && cubeDef.Id.SubtypeName.EndsWith("Unsupported");

        positionList.Clear();
        AiUtils.FindAllPositionsForBlock(block, positionList);

        foreach (var position in positionList)
        {
          if (nodeList.Count >= numberOfNodesNeeded)
            break;

          var positionAbove = position + normal;
          var worldPosition = biggestGrid.GridIntegerToWorld(positionAbove);
          var cubeAbove = biggestGrid.GetCubeBlock(positionAbove) as IMySlimBlock;
          var cubeAboveDef = cubeAbove?.BlockDefinition as MyCubeBlockDefinition;
          bool cubeAboveEmpty = cubeAbove == null || !cubeAboveDef.HasPhysics || cubeAboveDef.Id.SubtypeName.StartsWith("LargeWarningSign");
          bool aboveisScaffold = cubeAboveDef != null && AiSession.Instance.ScaffoldBlockDefinitions.Contains(cubeAboveDef.Id);
          bool aboveIsPassageStair = cubeAbove != null && cubeAbove.BlockDefinition.Id.SubtypeName.EndsWith("PassageStairs_Large");
          bool aboveIsConveyorCap = cubeAbove != null && AiSession.Instance.ConveyorEndCapDefinitions.ContainsItem(cubeAbove.BlockDefinition.Id);
          bool aboveisAutomatonsFlat = cubeAbove != null && AiSession.Instance.AutomatonsFlatBlockDefinitions.ContainsItem(cubeAbove.BlockDefinition.Id);
          bool checkAbove = !exclude && (airTight || allowSolar || isCylinder || aboveisScaffold || isAllowedConveyor
            || AiUtils.IsSidePressurizedForBlock(block, position, normal) /*(kvp.Value?.Contains(side) ?? false)*/);

          if (cubeAboveEmpty)
          {
            if (checkAbove)
            {
              if (allowSolar && block.BlockDefinition.Id.SubtypeName.IndexOf("colorablesolarpanelcorner", StringComparison.OrdinalIgnoreCase) >= 0)
              {
                var cellKey = AiUtils.GetCellForPosition(block, position);
                var xVal = block.BlockDefinition.Id.SubtypeName.EndsWith("Inverted") ? 1 : 0;

                if (cellKey == new Vector3I(xVal, 0, 0) || cellKey == new Vector3I(xVal, 1, 0))
                {
                  if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                    nodeList.Add(worldPosition);
                }
              }
              else if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                nodeList.Add(worldPosition);
            }
            else if (isAutomatonsFull)
            {
              var subtype = cubeDef.Id.SubtypeName;
              if (subtype.EndsWith("WallB"))
              {
                if (Base6Directions.GetIntVector(block.Orientation.Left).Dot(ref normal) != 0)
                {
                  if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                    nodeList.Add(worldPosition);
                }
              }
              else if (subtype == "AirDuct2")
              {
                if (Base6Directions.GetIntVector(block.Orientation.Up).Dot(ref normal) > 0)
                {
                  if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                    nodeList.Add(worldPosition);
                }
              }
              else
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (isPlatform)
            {
              if (Base6Directions.GetIntVector(block.Orientation.Up).Dot(ref normal) > 0)
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (isScaffold)
            {
              // TODO ??
              //AiSession.Instance.Logger.Log($"Scaffold block found: {cubeDef.Id.SubtypeName}, HasPhysics = {cubeDef.HasPhysics}");
            }
            else if (isFlatWindow)
            {
              if (block.BlockDefinition.Id.SubtypeName == "LargeWindowSquare")
              {
                if (Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) > 0)
                {
                  if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                    nodeList.Add(worldPosition);
                }
              }
              else if (Base6Directions.GetIntVector(block.Orientation.Left).Dot(ref normal) < 0)
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
              else if (block.BlockDefinition.Id.SubtypeName.StartsWith("HalfWindowCorner")
                && Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) > 0)
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (isSlopeBlock)
            {
              var leftVec = Base6Directions.GetIntVector(block.Orientation.Left);
              if (leftVec.Dot(ref normal) != 0 && (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove)))
                nodeList.Add(worldPosition);
            }
          }
          else if (!aboveIsPassageStair && (checkAbove || aboveisScaffold || aboveIsConveyorCap || aboveisAutomatonsFlat))
          {
            if (aboveisScaffold)
            {
              if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                nodeList.Add(worldPosition);
            }
            else if (cubeAboveDef.Id.SubtypeName.StartsWith("LargeBlockLargeFlatAtmosphericThrust"))
            {
              var thrustForwardVec = Base6Directions.GetIntVector(cubeAbove.Orientation.Forward);
              var subtractedPosition = positionAbove - thrustForwardVec;

              if (thrustForwardVec.Dot(ref normal) == 0 && cubeAbove.CubeGrid.GetCubeBlock(subtractedPosition)?.Position == cubeAbove.Position)
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (aboveisAutomatonsFlat)
            {
              if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                nodeList.Add(worldPosition);
            }
            else if (aboveIsConveyorCap)
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) >= 0)
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (cubeAbove.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_WindTurbine))
            {
              var blockPos = cubeAbove.Position;

              if (blockPos != position)
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (cubeAbove.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_MedicalRoom))
            {
              var subtype = cubeAbove.BlockDefinition.Id.SubtypeName;
              if (subtype == "LargeMedicalRoom" || subtype == "LargeMedicalRoomReskin")
              {
                var relPosition = positionAbove - cubeAbove.Position;
                if (relPosition.RectangularLength() > 1)
                {
                  if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                    nodeList.Add(worldPosition);
                }
              }
            }
            else if (cubeAbove.BlockDefinition.Id.SubtypeName.IndexOf("NeonTubes", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                nodeList.Add(worldPosition);
            }
            else if (cubeAbove.FatBlock is IMyInteriorLight && cubeAbove.BlockDefinition.Id.SubtypeName == "LargeLightPanel")
            {
              if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                nodeList.Add(worldPosition);
            }
            else if (cubeAbove.BlockDefinition.Id.SubtypeName.StartsWith("DeadBody"))
            {
              if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                nodeList.Add(worldPosition);
            }
            else if (cubeAbove.BlockDefinition.Id.SubtypeName == "RoboFactory")
            {
              var cubeAbovePosition = cubeAbove.Position;

              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) > 0 && positionAbove != cubeAbovePosition)
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (AiSession.Instance.DecorativeBlockDefinitions.ContainsItem(cubeAbove.BlockDefinition.Id))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) > 0)
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (AiSession.Instance.RailingBlockDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                nodeList.Add(worldPosition);
            }
            else if (cubeAboveDef.Id.SubtypeName.StartsWith("LargeCoverWall") || cubeAboveDef.Id.SubtypeName.StartsWith("FireCover"))
            {
              if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                nodeList.Add(worldPosition);
            }
            else if (AiSession.Instance.LockerDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) > 0)
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (AiSession.Instance.ArmorPanelAllDefinitions.Contains(cubeAboveDef.Id))
            {
              if (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(cubeAboveDef.Id)
                || AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(cubeAboveDef.Id))
              {
                if (Base6Directions.GetIntVector(cubeAbove.Orientation.Left).Dot(ref normal) == 0)
                {
                  if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                    nodeList.Add(worldPosition);
                }
              }
              else
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (AiSession.Instance.FreightBlockDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                nodeList.Add(worldPosition);
            }
            else if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              var turretBasePosition = cubeAbove.Position - Base6Directions.GetIntVector(cubeAbove.Orientation.Up);
              //if (needsPositionAdjusted)
              //  turretBasePosition = MainGrid.WorldToGridInteger(cubeAbove.CubeGrid.GridIntegerToWorld(turretBasePosition));

              if (turretBasePosition != positionAbove || cubeAboveDef.Id.TypeId == typeof(MyObjectBuilder_InteriorTurret))
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (AiSession.Instance.CatwalkBlockDefinitions.Contains(cubeAboveDef.Id))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) != 0)
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (AiSession.Instance.FlatWindowDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                nodeList.Add(worldPosition);
            }
            else if (AiSession.Instance.HalfBlockDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) != 0)
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (cubeAboveDef.Id.SubtypeName.EndsWith("Slope2Tip"))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Left).Dot(ref normal) != 0)
              {
                if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (cubeAbove.FatBlock != null)
            {
              var door = cubeAbove.FatBlock as IMyDoor;
              if (door != null)
              {
                var doorPosition = door.Position;
                //if (needsPositionAdjusted)
                //  doorPosition = MainGrid.WorldToGridInteger(cubeAbove.CubeGrid.GridIntegerToWorld(doorPosition));

                if (door is IMyAirtightHangarDoor)
                {
                  if (positionAbove != doorPosition)
                  {
                    if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                      nodeList.Add(worldPosition);
                  }
                }
                else if (cubeAbove.BlockDefinition.Id.SubtypeName.StartsWith("SlidingHatchDoor"))
                {
                  if (positionAbove == doorPosition)
                  {
                    if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                      nodeList.Add(worldPosition);
                  }
                }
                else if (cubeAbove.BlockDefinition.Id.SubtypeName == "LargeBlockGate")
                {
                  var doorCenter = door.WorldAABB.Center;
                  var nextPos = biggestGrid.GridIntegerToWorld(positionAbove);
                  var vector = nextPos - doorCenter;

                  if (vector.LengthSquared() < 8)
                  {
                    if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                      nodeList.Add(worldPosition);
                  }
                }
                else if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
              else if (cubeAbove.FatBlock is IMySolarPanel)
              {
                if (cubeAbove.BlockDefinition.Id.SubtypeName.IndexOf("colorable") < 0 && Base6Directions.GetIntVector(cubeAbove.Orientation.Forward).Dot(ref normal) > 0)
                {
                  if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                    nodeList.Add(worldPosition);
                }
              }
              else if (cubeAbove.FatBlock is IMyButtonPanel || AiSession.Instance.ButtonPanelDefinitions.ContainsItem(cubeAboveDef.Id)
                || (cubeAbove.FatBlock is IMyTextPanel && !AiSession.Instance.SlopeBlockDefinitions.Contains(cubeAboveDef.Id)))
              {
                if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) != 0)
                {
                  if (!onlyAirtightNodes || biggestGrid.IsRoomAtPositionAirtight(positionAbove))
                    nodeList.Add(worldPosition);
                }
              }
            }
          }
        }
      }

      AiSession.Instance.LineListPool?.Return(ref positionList);
    }

    /// <summary>
    /// Changes a Bot's ability to equip character tools. If disabled, the bot will unequip its current tool.
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="enable">Whether or not to allow the Bot to use tools and weapons</param>
    /// <returns>true if the change is successful, otherwise false</returns>
    public bool SetToolsEnabled(long botEntityId, bool enable)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.Character == null || bot.IsDead)
        return false;

      bot.AllowEquipWeapon = enable;

      if (!enable && bot.Character.EquippedTool != null)
      {
        var controlEnt = bot.Character as Sandbox.Game.Entities.IMyControllableEntity;
        controlEnt?.SwitchToWeapon(null);
      }

      return true;
    }

    /// <summary>
    /// Changes the bot's role
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="newRole">Bot Role: see <see cref="GetFriendlyBotRoles"/>, <see cref="GetNPCBotRoles"/>, or <see cref="GetNeutralBotRoles"/></param>
    /// <param name="toolSubtypes">A list of SubtypeIds for the weapon or tool you want to give the bot. A random item will be chosen from the list.</param>
    /// <returns>true if the change is successful, otherwise false</returns>
    public bool SwitchBotRole(long botEntityId, string newRole, List<string> toolSubtypes)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      BotBase bot, newBot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.Character == null || bot.IsDead)
        return false;

      var character = bot.Character;
      var gun = character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
      gun?.OnControlReleased();

      var controlEnt = character as Sandbox.Game.Entities.IMyControllableEntity;
      controlEnt?.SwitchToWeapon(null);

      GridBase graph;
      if (bot._currentGraph != null && bot._currentGraph.IsValid)
        graph = bot._currentGraph;
      else
        graph = AiSession.Instance.GetNewGraph(null, character.WorldAABB.Center, character.WorldMatrix);

      string toolType = null;
      if (toolSubtypes?.Count > 0)
      {
        for (int i = toolSubtypes.Count - 1; i >= 0; i--)
        {
          var subtype = toolSubtypes[i];
          if (!AiSession.Instance.IsBotAllowedToUse(newRole, subtype))
            toolSubtypes.RemoveAtFast(i);
        }

        if (toolSubtypes.Count > 0)
        {
          var num = MyUtils.GetRandomInt(0, toolSubtypes.Count);
          toolType = toolSubtypes[num];
        }
      }

      if (bot.Owner != null)
      {
        var role = BotFactory.ParseFriendlyRole(newRole);
        switch (role)
        {
          case BotFactory.BotRoleFriendly.COMBAT:
            newBot = new CombatBot(character, graph, bot.Owner.IdentityId, bot.BotControlInfo, toolType);
            break;
          case BotFactory.BotRoleFriendly.REPAIR:
            newBot = new RepairBot(character, graph, bot.Owner.IdentityId, bot.BotControlInfo, toolType);
            break;
          case BotFactory.BotRoleFriendly.SCAVENGER:
            newBot = new ScavengerBot(character, graph, bot.Owner.IdentityId, bot.BotControlInfo);
            break;
          default:
            AiSession.Instance.Logger.Log($"LocalBotAPI.SwitchBotRole received an invalid friendly role: {newRole}", MessageType.WARNING);
            return false;
        }
      }
      else if (newRole.Equals("nomad", StringComparison.OrdinalIgnoreCase))
      {
        newBot = new NomadBot(character, graph, bot.BotControlInfo);
      }
      else if (newRole.Equals("enforcer", StringComparison.OrdinalIgnoreCase))
      {
        newBot = new EnforcerBot(character, graph, bot.BotControlInfo);
      }
      else
      {
        var role = BotFactory.ParseEnemyBotRole(newRole);
        switch (role)
        {
          case BotFactory.BotRoleEnemy.BRUISER:
            newBot = new BruiserBot(character, graph, bot.BotControlInfo);
            break;
          case BotFactory.BotRoleEnemy.CREATURE:
            newBot = new CreatureBot(character, graph, bot.BotControlInfo);
            break;
          case BotFactory.BotRoleEnemy.GHOST:
            newBot = new GhostBot(character, graph, bot.BotControlInfo);
            break;
          case BotFactory.BotRoleEnemy.GRINDER:
            newBot = new GrinderBot(character, graph, bot.BotControlInfo, toolType);
            break;
          case BotFactory.BotRoleEnemy.SOLDIER:
            newBot = new SoldierBot(character, graph, bot.BotControlInfo, toolType);
            break;
          case BotFactory.BotRoleEnemy.ZOMBIE:
            newBot = new ZombieBot(character, graph, bot.BotControlInfo);
            break;
          default:
            AiSession.Instance.Logger.Log($"LocalBotAPI.SwitchBotRole received an invalid enemy role: {newRole}", MessageType.WARNING);
            return false;
        }
      }

      if (newBot != null)
      {
        if (AiSession.Instance.ModSaveData.AllowEnemiesToFly)
        {
          newBot.CanUseAirNodes = bot.CanUseAirNodes;
          newBot.CanUseSpaceNodes = bot.CanUseSpaceNodes;
        }

        newBot.AllowHelmetVisorChanges = bot.AllowHelmetVisorChanges;
        newBot.AllowIdleMovement = bot.AllowIdleMovement;
        newBot.CanTransitionMaps = bot.CanTransitionMaps;
        newBot.ConfineToMap = bot.ConfineToMap;
        newBot.CanUseWaterNodes = bot.CanUseWaterNodes;
        newBot.WaterNodesOnly = bot.WaterNodesOnly;
        newBot.GroundNodesFirst = bot.GroundNodesFirst;
        newBot.CanUseLadders = bot.CanUseLadders;
        newBot.CanUseSeats = bot.CanUseSeats;
        newBot.ShouldLeadTargets = bot.ShouldLeadTargets;
        newBot.HelmetEnabled = bot.HelmetEnabled;
        newBot.EnableDespawnTimer = bot.EnableDespawnTimer;
        newBot._lootContainerSubtype = bot._lootContainerSubtype;
        newBot._shotAngleDeviationTan = bot._shotAngleDeviationTan;
        newBot._despawnTicks = bot._despawnTicks;
        newBot.RepairPriorities = bot.RepairPriorities;
        newBot.TargetPriorities = bot.TargetPriorities;

        if (!string.IsNullOrWhiteSpace(bot._deathSoundString))
        {
          newBot._deathSoundString = bot._deathSoundString;
          newBot._deathSound = new MySoundPair(bot._deathSoundString);
        }

        if (bot._attackSounds?.Count > 0)
        {
          newBot._attackSounds.Clear();
          newBot._attackSounds.AddList(bot._attackSounds);
        }

        if (bot._attackSoundStrings?.Count > 0)
        {
          newBot._attackSoundStrings.Clear();
          newBot._attackSoundStrings.AddList(bot._attackSoundStrings);
        }

        if (bot.Behavior?.Phrases?.Count > 0)
        {
          newBot.Behavior.Phrases.Clear();
          newBot.Behavior.Phrases.AddList(bot.Behavior.Phrases);
        }

        if (bot.Behavior?.Actions?.Count > 0)
        {
          newBot.Behavior.Actions.Clear();
          newBot.Behavior.Actions.AddList(bot.Behavior.Actions);
        }

        if (bot.Behavior?.PainSounds?.Count > 0)
        {
          newBot.Behavior.PainSounds.Clear();
          newBot.Behavior.PainSounds.AddList(bot.Behavior.PainSounds);
        }

        if (bot.Behavior?.Taunts?.Count > 0)
        {
          newBot.Behavior.Taunts.Clear();
          newBot.Behavior.Taunts.AddList(bot.Behavior.Taunts);
        }
      }

      AiSession.Instance.SwitchBot(newBot);
      AiSession.Instance.Bots[botEntityId] = newBot;
      bot.Close(false, false);
      return true;
    }

    /// <summary>
    /// Changes the bot's role and associated data from <see cref="RemoteBotAPI.SpawnData"/>
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="spawnData">The serialized <see cref="RemoteBotAPI.SpawnData"/> object</param>
    /// <returns>true if the change is successful, otherwise false</returns>
    public bool SwitchBotRole(long botEntityId, byte[] data)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      BotBase bot, newBot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.Character == null || bot.IsDead)
        return false;

      var spawnData = MyAPIGateway.Utilities.SerializeFromBinary<RemoteBotAPI.SpawnData>(data);
      if (spawnData == null)
      {
        AiSession.Instance.Logger.Log($"AiEnabled API received SwitchBotRole command with malformed SpawnData object.", MessageType.WARNING);
        return false;
      }

      var newRole = spawnData.BotRole;
      var needNewbot = bot.Role.IndexOf(newRole, StringComparison.OrdinalIgnoreCase) < 0;
      if (needNewbot)
      {
        var character = bot.Character;
        var controlEnt = character as Sandbox.Game.Entities.IMyControllableEntity;

        var gun = character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
        gun?.OnControlReleased();

        controlEnt?.SwitchToWeapon(null);

        GridBase graph;
        if (bot._currentGraph != null && bot._currentGraph.IsValid)
          graph = bot._currentGraph;
        else
          graph = AiSession.Instance.GetNewGraph(null, character.WorldAABB.Center, character.WorldMatrix);

        string toolType = null;
        if (spawnData?.ToolSubtypeIdList?.Count > 0)
        {
          for (int i = spawnData.ToolSubtypeIdList.Count - 1; i >= 0; i--)
          {
            var subtype = spawnData.ToolSubtypeIdList[i];
            if (!AiSession.Instance.IsBotAllowedToUse(spawnData.BotRole, subtype))
              spawnData.ToolSubtypeIdList.RemoveAtFast(i);
          }

          if (spawnData.ToolSubtypeIdList.Count > 0)
          {
            var num = MyUtils.GetRandomInt(0, spawnData.ToolSubtypeIdList.Count);
            toolType = spawnData.ToolSubtypeIdList[num];
          }
        }
        else if (!string.IsNullOrWhiteSpace(spawnData.ToolSubtypeId) && AiSession.Instance.IsBotAllowedToUse(spawnData.BotRole, spawnData.ToolSubtypeId))
        {
          toolType = spawnData.ToolSubtypeId;
        }

        if (bot.Owner != null)
        {
          var role = BotFactory.ParseFriendlyRole(newRole);
          switch (role)
          {
            case BotFactory.BotRoleFriendly.COMBAT:
              newBot = new CombatBot(character, graph, bot.Owner.IdentityId, bot.BotControlInfo, toolType);
              break;
            case BotFactory.BotRoleFriendly.REPAIR:
              newBot = new RepairBot(character, graph, bot.Owner.IdentityId, bot.BotControlInfo, toolType);
              break;
            case BotFactory.BotRoleFriendly.SCAVENGER:
              newBot = new ScavengerBot(character, graph, bot.Owner.IdentityId, bot.BotControlInfo);
              break;
            default:
              AiSession.Instance.Logger.Log($"LocalBotAPI.SwitchBotRole received an invalid friendly role: {newRole}", MessageType.WARNING);
              return false;
          }
        }
        else if (newRole.Equals("nomad", StringComparison.OrdinalIgnoreCase))
        {
          newBot = new NomadBot(character, graph, bot.BotControlInfo);
        }
        else if (newRole.Equals("enforcer", StringComparison.OrdinalIgnoreCase))
        {
          newBot = new EnforcerBot(character, graph, bot.BotControlInfo);
        }
        else
        {
          var role = BotFactory.ParseEnemyBotRole(newRole);
          switch (role)
          {
            case BotFactory.BotRoleEnemy.BRUISER:
              newBot = new BruiserBot(character, graph, bot.BotControlInfo);
              break;
            case BotFactory.BotRoleEnemy.CREATURE:
              newBot = new CreatureBot(character, graph, bot.BotControlInfo);
              break;
            case BotFactory.BotRoleEnemy.GHOST:
              newBot = new GhostBot(character, graph, bot.BotControlInfo);
              break;
            case BotFactory.BotRoleEnemy.GRINDER:
              newBot = new GrinderBot(character, graph, bot.BotControlInfo, toolType);
              break;
            case BotFactory.BotRoleEnemy.SOLDIER:
              newBot = new SoldierBot(character, graph, bot.BotControlInfo, toolType);
              break;
            case BotFactory.BotRoleEnemy.ZOMBIE:
              newBot = new ZombieBot(character, graph, bot.BotControlInfo);
              break;
            default:
              AiSession.Instance.Logger.Log($"LocalBotAPI.SwitchBotRole received an invalid enemy role: {newRole}", MessageType.WARNING);
              return false;
          }
        }

        AiSession.Instance.SwitchBot(newBot);
        AiSession.Instance.Bots[botEntityId] = newBot;
        bot.Close(false, false);
      }
      else
      {
        newBot = bot;
      }

      if (newBot != null)
      {
        if (AiSession.Instance.ModSaveData.AllowEnemiesToFly)
        {
          newBot.CanUseAirNodes = spawnData.CanUseAirNodes;
          newBot.CanUseSpaceNodes = spawnData.CanUseSpaceNodes;
        }

        newBot.AllowHelmetVisorChanges = spawnData.AllowHelmetVisorChanges;
        newBot.AllowIdleMovement = spawnData.AllowIdleMovement;
        newBot.CanTransitionMaps = spawnData.AllowMapTransitions;
        newBot.ConfineToMap = spawnData.ConfineToMap;
        newBot.CanDamageGrid = spawnData.CanDamageGrids;
        newBot.CanUseWaterNodes = spawnData.CanUseWaterNodes;
        newBot.WaterNodesOnly = spawnData.WaterNodesOnly;
        newBot.GroundNodesFirst = spawnData.UseGroundNodesFirst;
        newBot.CanUseLadders = spawnData.CanUseLadders;
        newBot.CanUseSeats = spawnData.CanUseSeats;
        newBot.ShouldLeadTargets = spawnData.LeadTargets;
        newBot.HelmetEnabled = spawnData.AllowHelmetVisorChanges;
        newBot._lootContainerSubtype = spawnData.LootContainerSubtypeId;
        newBot._shotAngleDeviationTan = (float)Math.Tan(MathHelper.ToRadians(spawnData.ShotDeviationAngle));

        newBot.EnableDespawnTimer = spawnData.DespawnTicks > 0;

        if (spawnData.DespawnTicks > 0)
          newBot._despawnTicks = spawnData.DespawnTicks;

        if (!string.IsNullOrWhiteSpace(spawnData.DeathSound))
        {
          if (newBot._deathSound != null)
            newBot._deathSound.Init(spawnData.DeathSound);
          else
            newBot._deathSound = new MySoundPair(spawnData.DeathSound);

          newBot._deathSoundString = spawnData.DeathSound;
        }

        if (spawnData.AttackSounds?.Count > 0)
        {
          var botSounds = newBot._attackSoundStrings;
          var botSoundPairs = newBot._attackSounds;
          botSounds.Clear();

          for (int i = 0; i < spawnData.AttackSounds.Count; i++)
          {
            var sound = spawnData.AttackSounds[i];
            botSounds.Add(sound);

            if (botSoundPairs.Count > i)
              botSoundPairs[i].Init(sound);
            else
              botSoundPairs.Add(new MySoundPair(sound));
          }

          var remaining = botSoundPairs.Count - botSounds.Count;
          if (remaining > 0)
            botSoundPairs.RemoveRange(botSounds.Count, remaining);
        }

        if (spawnData.IdleSounds?.Count > 0)
        {
          var sounds = newBot.Behavior.Phrases;
          sounds.Clear();
          sounds.AddList(spawnData.IdleSounds);
        }

        if (spawnData.Actions?.Count > 0)
        {
          var actions = newBot.Behavior.Actions;
          actions.Clear();
          actions.AddList(spawnData.Actions);
        }

        if (spawnData.PainSounds?.Count > 0)
        {
          var sounds = newBot.Behavior.PainSounds;
          sounds.Clear();
          sounds.AddList(spawnData.PainSounds);
        }

        if (spawnData.TauntSounds?.Count > 0)
        {
          var sounds = newBot.Behavior.Taunts;
          sounds.Clear();
          sounds.AddList(spawnData.TauntSounds);
        }

        if (spawnData.RepairPriorities?.Count > 0)
        {
          newBot.RepairPriorities = new RepairPriorities(spawnData.RepairPriorities);
        }
        else
        {
          newBot.RepairPriorities = bot.RepairPriorities ?? new RepairPriorities();
        }

        if (spawnData.TargetPriorities?.Count > 0)
        {
          newBot.TargetPriorities = new TargetPriorities(spawnData.TargetPriorities);
        }
        else
        {
          newBot.TargetPriorities = bot.TargetPriorities ?? new TargetPriorities();
        }
      }

      return true;
    }

    /// <summary>
    /// Gets the bot's owner's IdentityId (Player Id)
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <returns>the IdentityId of the bot's owner if found, otherwise 0</returns>
    public long GetBotOwnerId(long botEntityId)
    {
      long ownerId = 0L;

      if (AiSession.Instance?.Bots != null && AiSession.Instance.Registered)
      {
        BotBase bot;
        if (AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) && bot?.Owner != null)
          ownerId = bot.Owner.IdentityId;
      }

      return ownerId;
    }

    /// <summary>
    /// Attempts to switch a bot's weapon or tool. The item will be added if not found in the bot's inventory.
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="toolSubtypeId">The SubtypeId for the weapon or tool you want the bot to use</param>
    /// <returns>true if the switch is successful, otherwise false</returns>
    public bool SwitchBotWeapon(long botEntityId, string toolSubtypeId)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.Character == null || bot.IsDead)
        return false;

      string reason;
      if (!AiSession.Instance.IsBotAllowedToUse(bot, toolSubtypeId, out reason))
        return false;

      var weaponDefinition = new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), toolSubtypeId);
      var toolDef = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(weaponDefinition);
      if (toolDef == null)
        return false;

      var inventory = bot.Character.GetInventory();
      if (inventory == null)
        return false;

      if (inventory.GetItemAmount(weaponDefinition) == 0)
      {
        if (!inventory.CanItemsBeAdded(1, weaponDefinition))
          return false;

        var weapon = (MyObjectBuilder_PhysicalGunObject)MyObjectBuilderSerializer.CreateNewObject(weaponDefinition);
        inventory.AddItems(1, weapon);
      }

      bot.ToolDefinition = toolDef;
      bot.EquipWeapon();
      return true;
    }

    /// <summary>
    /// Adds AiEnabled bots to a user-supplied dictionary
    /// </summary>
    /// <param name="botDict">the dictionary to add bots to. Key is the bot's IdentityId. The method will create and/or clear the dictionary before use</param>
    /// <param name="includeFriendly">whether or not to include all player-owned bots</param>
    /// <param name="includeEnemy">whether or not to include all enemy bots</param>
    /// <param name="includeNeutral">whether or not to include Nomad bots</param>
    public void GetBots(Dictionary<long, IMyCharacter> botDict, bool includeFriendly = true, bool includeEnemy = true, bool includeNeutral = true)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return;

      if (botDict == null)
        botDict = new Dictionary<long, IMyCharacter>(AiSession.Instance.ModSaveData.MaxBotsInWorld);
      else
        botDict.Clear();

      foreach (var kvp in AiSession.Instance.Bots)
      {
        var bot = kvp.Value;
        if (bot?.Character == null || bot.IsDead)
          continue;

        if (bot.Owner != null)
        {
          if (includeFriendly)
            botDict[bot.BotIdentityId] = bot.Character;
        }
        else if (bot is NomadBot)
        {
          if (includeNeutral)
          {
            botDict[bot.BotIdentityId] = bot.Character;
          }
        }
        else if (includeEnemy)
        {
          botDict[bot.BotIdentityId] = bot.Character;
        }
      }
    }

    /// <summary>
    /// Attempts to throw a grenade at the bot's current target. Does nothing if the target is not an IMyEntity or is friendly.
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    public void ThrowGrenade(long botEntityId)
    {
      if (AiSession.Instance?.Bots == null || !AiSession.Instance.Registered)
        return;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.Character == null || bot.IsDead)
        return;

      if (AiSession.Instance.GrenadesEnabled)
        BotFactory.ThrowGrenade(bot);
    }
  }
}
