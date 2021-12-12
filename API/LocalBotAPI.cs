﻿using AiEnabled.Ai.Support;
using AiEnabled.Bots;
using AiEnabled.Utilities;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
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
        { "SpawnBotCustom", new Func<string, MyPositionAndOrientation, byte[], MyCubeGrid, long?, IMyCharacter>(SpawnBot)},
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
        { "GetClosestValidNode", new GetClosestNodeDelegate(GetClosestValidNode) },
        { "CreateGridMap", new Func<MyCubeGrid, MatrixD?, bool>(CreateGridMap) },
        { "IsGridMapReady", new Func<MyCubeGrid, bool>(IsGridMapReady) },
        { "RemoveBot", new Func<long, bool>(CloseBot) },
        { "Speak", new Action<long, string>(Speak) },
        { "Perform", new Action<long, string>(Perform) },
        { "IsBot", new Func<long, bool>(IsBot) },
        { "GetRelationshipBetween", new Func<long, long, MyRelationsBetweenPlayerAndBlock>(GetRelationshipBetween) },
        { "GetBotAndRelationTo", new GetBotAndRelationTo(CheckBotRelationTo) },
     };

      return dict;
    }

    delegate bool GetClosestNodeDelegate(long botEntityId, MyCubeGrid grid, Vector3I start, Vector3D? up, out Vector3D result);
    delegate bool GetBotAndRelationTo(long botEntityId, long otherIdentityId, out MyRelationsBetweenPlayerAndBlock relationBetween);

    /// <summary>
    /// Check this BEFORE attempting to spawn a bot to ensure the mod is ready
    /// </summary>
    public bool CanSpawn() => AiSession.Instance?.CanSpawn ?? false;

    /// <summary>
    /// Retrieves the current set of available bot subtypes the mod will recognize
    /// </summary>
    public string[] GetBotSubtypes() => AiSession.Instance?.RobotSubtypes?.ToArray() ?? null;

    /// <summary>
    /// Retrieves the current set of available friendly bot roles
    /// </summary>
    public string[] GetFriendlyBotRoles() => Enum.GetNames(typeof(BotFactory.BotRoleFriendly));

    /// <summary>
    /// Retrieves the current set of available non-friendly bot roles
    /// </summary>
    public string[] GetNPCBotRoles() => Enum.GetNames(typeof(BotFactory.BotRoleEnemy));

    /// <summary>
    /// Retrieves the current set of available neutral bot roles
    /// </summary>
    public string[] GetNeutralBotRoles() => Enum.GetNames(typeof(BotFactory.BotRoleNeutral));

    /// <summary>
    /// Determines if the entity id belongs to an AiEnabled bot
    /// </summary>
    /// <param name="entityId">The EntityId of the Character</param>
    /// <returns>true if the EntityId belongs to a bot, otherwise false</returns>
    public bool IsBot(long entityId)
    {
      if (AiSession.Instance?.Registered != true)
        return false;

      return AiSession.Instance.Bots.ContainsKey(entityId);
    }

    /// <summary>
    /// Retrieves the relationship between an AiEnabled bot and an identity
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="otherIdentityId">The IdentityId to check against</param>
    /// <returns>MyRelationsBetweenPlayerAndBlock, default is NoOwnership</returns>
    public MyRelationsBetweenPlayerAndBlock GetRelationshipBetween(long botEntityId, long otherIdentityId)
    {
      if (AiSession.Instance?.Registered != true)
        return MyRelationsBetweenPlayerAndBlock.NoOwnership;

      BotBase bot;
      if (AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) && bot?.Character != null && !bot.IsDead)
      {
        if (otherIdentityId == bot.Owner?.IdentityId)
          return MyRelationsBetweenPlayerAndBlock.Friends;

        var botOwnerId = bot.Character.ControllerInfo.ControllingIdentityId;
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
    /// <param name="relationBetween">The relationship between the bot and the identity, defaults to NoOwnership</param>
    /// <returns>true if the EntityId is an AiEnabled bot, otherwise false</returns>
    public bool CheckBotRelationTo(long botEntityId, long otherIdentityId, out MyRelationsBetweenPlayerAndBlock relationBetween)
    {
      relationBetween = MyRelationsBetweenPlayerAndBlock.NoOwnership;

      if (AiSession.Instance?.Registered != true)
        return false;

      BotBase bot;
      if (AiSession.Instance.Bots.TryGetValue(botEntityId, out bot))
      {
        if (bot?.Character != null && !bot.IsDead)
        {
          if (otherIdentityId == bot.Owner?.IdentityId)
          {
            relationBetween = MyRelationsBetweenPlayerAndBlock.Friends;
          }
          else
          {
            var botOwnerId = bot.Character.ControllerInfo.ControllingIdentityId;
            var shareMode = bot.Owner != null ? MyOwnershipShareModeEnum.Faction : MyOwnershipShareModeEnum.None;
            relationBetween = MyIDModule.GetRelationPlayerBlock(botOwnerId, otherIdentityId, shareMode);
          }
        }

        return true;
      }

      return false;
    }

    /// <summary>
    /// Attempts to have a bot speak some phrase
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <param name="phrase">The name of the phrase (leave null to use a random phrase)</param>
    public void Speak(long botEntityId, string phrase = null)
    {
      if (AiSession.Instance?.Registered != true)
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
      if (AiSession.Instance?.Registered != true)
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
      if (AiSession.Instance?.Registered != true)
        return false;

      if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose)
        return false;

      CubeGridMap gridMap;
      return AiSession.Instance.GridGraphDict.TryGetValue(grid.EntityId, out gridMap) && gridMap != null && gridMap.Ready;
    }

    /// <summary>
    /// Starts processing a grid to be used as a grid map, if it doesn't already exist
    /// </summary>
    /// <param name="grid">The grid to process</param>
    /// <param name="rotationMatrix">The matrix to use to determine proper orientation for the grid map</param>
    /// <returns>true if a map exists or if able to create one, otherwise false</returns>
    public bool CreateGridMap(MyCubeGrid grid, MatrixD? rotationMatrix = null)
    {
      if (AiSession.Instance?.Registered != true)
        return false;

      if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose)
        return false;

      if (rotationMatrix == null)
      {
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
        else if (grid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_Cockpit), out numSeats) && numSeats > 0)
        {
          foreach (var b in grid.GetFatBlocks())
          {
            if (b is IMyShipController)
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

        rotationMatrix = MatrixD.CreateWorld(grid.PositionComp.WorldAABB.Center, Vector3D.CalculatePerpendicularVector(upVector), upVector);
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
    /// <param name="validWorldPosition">The returned world position</param>
    /// <returns>true if able to find a valid node nearby, otherwise false</returns>
    public bool GetClosestValidNode(long botEntityId, MyCubeGrid grid, Vector3I startPosition, Vector3D? upVec, out Vector3D validWorldPosition)
    {
      validWorldPosition = Vector3D.Zero;

      if (AiSession.Instance?.Registered != true)
        return false;

      if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose)
        return false;

      CubeGridMap gridMap;
      if (!AiSession.Instance.GridGraphDict.TryGetValue(grid.EntityId, out gridMap) || gridMap?.Grid == null || !gridMap.Ready)
        return false;

      bool isSlim = gridMap.Grid.GetCubeBlock(startPosition) != null;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot))
        return false;

      Vector3I node;
      if (gridMap.GetClosestValidNode(bot, startPosition, out node, upVec, isSlim, currentIsDenied: true))
      {
        validWorldPosition = gridMap.LocalToWorld(node);
        return true;
      }

      return false;
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
    public IMyCharacter SpawnBot(string subType, string displayName, MyPositionAndOrientation positionAndOrientation, MyCubeGrid grid = null, string role = null, long? owner = null, Color? color = null)
    {
      if (AiSession.Instance?.CanSpawn != true)
      {
        if (MyAPIGateway.Session?.Player != null)
          MyAPIGateway.Utilities.ShowNotification($"The mod is not ready to spawn bots just yet..");

        AiSession.Instance.Logger.Log($"AiEnabled: API received SpawnBot command before mod was ready to spawn bots.");

        return null;
      }

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
    public IMyCharacter SpawnBot(string displayName, MyPositionAndOrientation positionAndOrientation, byte[] spawnData, MyCubeGrid grid = null, long? owner = null)
    {
      if (AiSession.Instance?.CanSpawn != true)
      {
        if (MyAPIGateway.Session?.Player != null)
          MyAPIGateway.Utilities.ShowNotification($"The mod is not ready to spawn bots just yet..");

        AiSession.Instance.Logger.Log($"AiEnabled: API received SpawnBot command before mod was ready to spawn bots.");

        return null;
      }

      var data = MyAPIGateway.Utilities.SerializeFromBinary<RemoteBotAPI.SpawnData>(spawnData);
      if (data == null)
        return null;

      return BotFactory.SpawnBotFromAPI(displayName, positionAndOrientation, data, grid, owner);
    }

    /// <summary>
    /// Gets the current Overridden (GoTo) position for the Bot
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    public Vector3D? GetBotOverride(long botEntityId)
    {
      if (AiSession.Instance?.Registered != true)
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
      if (AiSession.Instance?.Registered != true)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot == null || bot.IsDead)
        return false;

      AiSession.Instance.Logger.Log($"Setting Bot Override to {goTo}");
      bot.UseAPITargets = true;
      bot.Target.SetOverride(goTo);
      bot._pathCollection?.CleanUp(true);
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
      if (AiSession.Instance?.Registered != true)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.IsDead != false)
        return false;

      AiSession.Instance.Logger.Log($"Setting Override Action to {action?.Method.Name ?? "NULL"}");
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
      if (AiSession.Instance?.Registered != true)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.IsDead != false)
        return false;

      bot.UseAPITargets = true;
      bot.Target.SetTarget(bot.Owner, target);
      bot._pathCollection?.CleanUp(true);
      return true;
    }

    /// <summary>
    /// Clears the Bot's current target and re-enables autonomous targeting
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <returns>true if targeting is successfully reset, otherwise false</returns>
    public bool ResetBotTargeting(long botEntityId)
    {
      if (AiSession.Instance?.Registered != true)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.IsDead != false)
        return false;

      bot.UseAPITargets = false;
      bot.Target.RemoveTarget();
      bot._pathCollection?.CleanUp(true);
      return true;
    }

    /// <summary>
    /// Removed a bot from the world. Use this to ensure everything is cleaned up properly!
    /// </summary>
    /// <param name="botEntityId">The EntityId of the Bot's Character</param>
    /// <returns>true if the bot is successfully removed, otherwise false</returns>
    public bool CloseBot(long botEntityId)
    {
      if (AiSession.Instance?.Registered != true)
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
      if (AiSession.Instance?.Registered != true)
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
      if (AiSession.Instance?.Registered != true)
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
        else
          botParent.RemovePilot();
      }

      if (!seat.HasPlayerAccess(bot.Character.ControllerInfo.ControllingIdentityId))
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
      if (AiSession.Instance?.Registered != true)
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
      if (AiSession.Instance?.Registered != true)
        return false;

      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(botEntityId, out bot) || bot?.Character == null || bot.IsDead)
        return false;

      if (!(bot.Character.Parent is IMyCockpit))
        return true;

      return BotFactory.RemoveBotFromSeat(bot);
    }

    /// <summary>
    /// Attmepts to find valid grid nodes to spawn NPCs at. Check for null before iterating the returned list!
    /// </summary>
    /// <param name="grid">The grid to spawn on</param>
    /// <param name="numberOfNodesNeeded">The number of bots you want to spawn</param>
    /// <param name="upVector">The normalized Up direction for the grid, if known</param>
    /// <param name="onlyAirtightNodes">If only pressurized areas should be considered</param>
    /// <returns></returns>
    public void GetAvailableGridNodes(MyCubeGrid grid, int numberOfNodesNeeded, List<Vector3D> nodeList, Vector3D? upVector = null, bool onlyAirtightNodes = false)
    {
      if (grid == null || grid.IsPreview || grid.MarkedAsTrash || grid.MarkedForClose)
        return;

      if (nodeList == null)
        nodeList = new List<Vector3D>(numberOfNodesNeeded);
      else
        nodeList.Clear();

      if (upVector == null || Vector3D.IsZero(upVector.Value))
      {
        int numSeats;
        if (grid.HasMainCockpit())
        {
          upVector = grid.MainCockpit.WorldMatrix.Up;
        }
        else if (grid.HasMainRemoteControl())
        {
          upVector = grid.MainRemoteControl.WorldMatrix.Up;
        }
        else if (grid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_Cockpit), out numSeats) && numSeats > 0)
        {
          foreach (var b in grid.GetFatBlocks())
          {
            if (b is IMyShipController)
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
      }

      var normal = Base6Directions.GetIntVector(grid.WorldMatrix.GetClosestDirection(upVector.Value));
      var blocks = grid.GetBlocks();

      foreach (IMySlimBlock block in blocks)
      {
        if (nodeList.Count >= numberOfNodesNeeded)
          break;

        var cubeDef = block.BlockDefinition as MyCubeBlockDefinition;

        bool airTight = cubeDef.IsAirTight ?? false;
        bool allowSolar = !airTight && block.FatBlock is IMySolarPanel && Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) > 0;
        bool allowConn = !allowSolar && block.FatBlock is IMyShipConnector && cubeDef.Id.SubtypeName == "Connector";
        bool isFlatWindow = !allowConn && AiSession.Instance.FlatWindowDefinitions.ContainsItem(cubeDef.Id);
        bool isCylinder = !isFlatWindow && AiSession.Instance.PipeBlockDefinitions.ContainsItem(cubeDef.Id);
        bool isSlopeBlock = !isCylinder && AiSession.Instance.SlopeBlockDefinitions.Contains(cubeDef.Id)
          && !AiSession.Instance.SlopedHalfBlockDefinitions.Contains(cubeDef.Id)
          && !AiSession.Instance.HalfStairBlockDefinitions.Contains(cubeDef.Id);

        Matrix matrix = new Matrix
        {
          Forward = Base6Directions.GetVector(block.Orientation.Forward),
          Left = Base6Directions.GetVector(block.Orientation.Left),
          Up = Base6Directions.GetVector(block.Orientation.Up)
        };

        var faceDict = AiSession.Instance.BlockFaceDictionary[cubeDef.Id];

        if (faceDict.Count < 2)
          matrix.TransposeRotationInPlace();

        Vector3I side, center = cubeDef.Center;
        Vector3I.TransformNormal(ref normal, ref matrix, out side);
        Vector3I.TransformNormal(ref center, ref matrix, out center);
        var adjustedPosition = block.Position - center;

        foreach (var kvp in faceDict)
        {
          if (nodeList.Count >= numberOfNodesNeeded)
            break;

          var cell = kvp.Key;
          Vector3I.TransformNormal(ref cell, ref matrix, out cell);
          var positionAbove = adjustedPosition + cell + normal;
          var worldPosition = grid.GridIntegerToWorld(positionAbove);
          var cubeAbove = grid.GetCubeBlock(positionAbove) as IMySlimBlock;
          var cubeAboveDef = cubeAbove?.BlockDefinition as MyCubeBlockDefinition;
          bool cubeAboveEmpty = cubeAbove == null || !cubeAboveDef.HasPhysics;
          bool checkAbove = airTight || allowConn || allowSolar || isCylinder || (kvp.Value?.Contains(side) ?? false);

          if (cubeAboveEmpty)
          {
            if (checkAbove)
            {
              if (!onlyAirtightNodes || grid.IsRoomAtPositionAirtight(positionAbove))
                nodeList.Add(worldPosition);
            }
            else if (isFlatWindow)
            {
              if (block.BlockDefinition.Id.SubtypeName == "LargeWindowSquare")
              {
                if (Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) > 0)
                {
                  if (!onlyAirtightNodes || grid.IsRoomAtPositionAirtight(positionAbove))
                    nodeList.Add(worldPosition);
                }
              }
              else if (Base6Directions.GetIntVector(block.Orientation.Left).Dot(ref normal) < 0)
              {
                if (!onlyAirtightNodes || grid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
              else if (block.BlockDefinition.Id.SubtypeName.StartsWith("HalfWindowCorner")
                && Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) > 0)
              {
                if (!onlyAirtightNodes || grid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
          }
          else if (checkAbove)
          {
            if (AiSession.Instance.RailingBlockDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) != 0)
              {
                if (!onlyAirtightNodes || grid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (cubeAboveDef.Id.SubtypeName.StartsWith("LargeCoverWall") || cubeAboveDef.Id.SubtypeName.StartsWith("FireCover"))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) != 0)
              {
                if (!onlyAirtightNodes || grid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (AiSession.Instance.LockerDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) != 0)
              {
                if (!onlyAirtightNodes || grid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (AiSession.Instance.FreightBlockDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              if (!onlyAirtightNodes || grid.IsRoomAtPositionAirtight(positionAbove))
                nodeList.Add(worldPosition);
            }
            else if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              var turretBasePosition = cubeAbove.Position - Base6Directions.GetIntVector(cubeAbove.Orientation.Up);
              if (turretBasePosition != positionAbove || cubeAboveDef.Id.TypeId == typeof(MyObjectBuilder_InteriorTurret))
              {
                if (!onlyAirtightNodes || grid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (AiSession.Instance.CatwalkBlockDefinitions.Contains(cubeAboveDef.Id))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) != 0)
              {
                if (!onlyAirtightNodes || grid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
            else if (AiSession.Instance.FlatWindowDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              if (!onlyAirtightNodes || grid.IsRoomAtPositionAirtight(positionAbove))
                nodeList.Add(worldPosition);
            }
            else if (cubeAbove.FatBlock != null && cubeAbove.FatBlock is IMyButtonPanel)
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) != 0)
              {
                if (!onlyAirtightNodes || grid.IsRoomAtPositionAirtight(positionAbove))
                  nodeList.Add(worldPosition);
              }
            }
          }
        }
      }
    }
  }
}
