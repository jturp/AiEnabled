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
using VRage.Game.Components;
using System.Diagnostics;
using AiEnabled.Graphics.Support;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using VRage.Game.ModAPI.Interfaces;
using Sandbox.ModAPI.Weapons;
using Sandbox.Game.WorldEnvironment;
using AiEnabled.Bots.Roles;
using AiEnabled.Parallel;
using ParallelTasks;
using VRage;
using AiEnabled.API;
using AiEnabled.Projectiles;
using SpaceEngineers.Game.ModAPI;

namespace AiEnabled.Bots
{
  public abstract partial class BotBase
  {
    [Flags]
    enum BotInfo : ulong
    {
      None = 0x0,
      HasTool = 0x1,
      HasLOS = 0x2,
      UseAPITargets = 0x4,
      SwitchWalk = 0x8,
      DamagePending = 0x10,
      BehaviorReady = 0x20,
      BotMoved = 0x40,
      PFActive = 0x80,
      UsePathfinder = 0x100,
      UseLadder = 0x200,
      NextIsLadder = 0x400,
      AfterNextIsLadder = 0x800,
      NeedsTransition = 0x1000,
      IsShooting = 0x2000,
      LOSTimer = 0x4000,
      StuckTimer = 0x8000,
      SwerveTimer = 0x10000,
      CheckGraph = 0x20000,
      SpaceNode = 0x40000,
      AirNode = 0x80000,
      WaterNode = 0x100000,
      WaterNodeOnly = 0x200000,
      LadderNode = 0x400000,
      SeatNode = 0x800000,
      GroundFirst = 0x1000000,
      JetPackReq = 0x2000000,
      JetPackEnabled = 0x4000000,
      Despawn = 0x8000000,
      WantsTarget = 0x10000000,
      CallBack = 0x20000000,
      LastIsAirNode = 0x40000000,
      LeadTargets = 0x80000000,
      BuggZapped = 0x100000000,
      FollowMode = 0x200000000,
      PatrolMode = 0x400000000,
    }

    public IMyPlayer Owner;
    public IMyCharacter Character;
    public TargetInfo Target;
    public float DamageModifier;
    public int TicksBetweenProjectiles = 10;
    public MyHandItemDefinition ToolDefinition;
    public AiSession.BotType BotType;

    public bool HasWeaponOrTool
    {
      get { return (_botInfo & BotInfo.HasTool) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.HasTool;
        }
        else
        {
          _botInfo &= ~BotInfo.HasTool;
        }
      }
    }

    public bool HasLineOfSight 
    {
      get { return (_botInfo & BotInfo.HasLOS) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.HasLOS;
        }
        else
        {
          _botInfo &= ~BotInfo.HasLOS;
        }
      }
    }

    public bool UseAPITargets 
    {
      get { return (_botInfo & BotInfo.UseAPITargets) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.UseAPITargets;
        }
        else
        {
          _botInfo &= ~BotInfo.UseAPITargets;
        }
      }
    }


    internal bool SwitchWalk
    {
      get { return (_botInfo & BotInfo.SwitchWalk) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.SwitchWalk;
        }
        else
        {
          _botInfo &= ~BotInfo.SwitchWalk;
        }
      }
    }

    internal bool DamagePending
    {
      get { return (_botInfo & BotInfo.DamagePending) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.DamagePending;
        }
        else
        {
          _botInfo &= ~BotInfo.DamagePending;
        }
      }
    }

    internal bool BehaviorReady
    {
      get { return (_botInfo & BotInfo.BehaviorReady) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.BehaviorReady;
        }
        else
        {
          _botInfo &= ~BotInfo.BehaviorReady;
        }
      }
    }

    internal bool PathFinderActive
    {
      get { return (_botInfo & BotInfo.PFActive) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.PFActive;
        }
        else
        {
          _botInfo &= ~BotInfo.PFActive;
        }
      }
    }

    internal bool BotMoved
    {
      get { return (_botInfo & BotInfo.BotMoved) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.BotMoved;
        }
        else
        {
          _botInfo &= ~BotInfo.BotMoved;
        }
      }
    }

    internal bool UsePathFinder
    {
      get { return (_botInfo & BotInfo.UsePathfinder) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.UsePathfinder;
        }
        else
        {
          _botInfo &= ~BotInfo.UsePathfinder;
        }
      }
    }

    internal bool NextIsLadder
    {
      get { return (_botInfo & BotInfo.NextIsLadder) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.NextIsLadder;
        }
        else
        {
          _botInfo &= ~BotInfo.NextIsLadder;
        }
      }
    }

    internal bool AfterNextIsLadder
    {
      get { return (_botInfo & BotInfo.AfterNextIsLadder) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.AfterNextIsLadder;
        }
        else
        {
          _botInfo &= ~BotInfo.AfterNextIsLadder;
        }
      }
    }

    internal bool UseLadder
    {
      get { return (_botInfo & BotInfo.UseLadder) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.UseLadder;
        }
        else
        {
          _botInfo &= ~BotInfo.UseLadder;
        }
      }
    }

    internal bool NeedsTransition
    {
      get { return (_botInfo & BotInfo.NeedsTransition) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.NeedsTransition;
        }
        else
        {
          _botInfo &= ~BotInfo.NeedsTransition;
        }
      }
    }

    internal bool IsShooting
    {
      get { return (_botInfo & BotInfo.IsShooting) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.IsShooting;
        }
        else
        {
          _botInfo &= ~BotInfo.IsShooting;
        }
      }
    }

    internal bool WaitForLOSTimer
    {
      get { return (_botInfo & BotInfo.LOSTimer) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.LOSTimer;
        }
        else
        {
          _botInfo &= ~BotInfo.LOSTimer;
        }
      }
    }

    internal bool WaitForStuckTimer
    {
      get { return (_botInfo & BotInfo.StuckTimer) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.StuckTimer;
        }
        else
        {
          _botInfo &= ~BotInfo.StuckTimer;
        }
      }
    }

    internal bool WaitForSwerveTimer
    {
      get { return (_botInfo & BotInfo.SwerveTimer) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.SwerveTimer;
        }
        else
        {
          _botInfo &= ~BotInfo.SwerveTimer;
        }
      }
    }

    internal bool CheckGraphNeeded
    {
      get { return (_botInfo & BotInfo.CheckGraph) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.CheckGraph;
        }
        else
        {
          _botInfo &= ~BotInfo.CheckGraph;
        }
      }
    }

    internal bool CanUseSpaceNodes
    {
      get { return (_botInfo & BotInfo.SpaceNode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.SpaceNode;
        }
        else
        {
          _botInfo &= ~BotInfo.SpaceNode;
        }
      }
    }

    internal bool CanUseAirNodes
    {
      get { return (_botInfo & BotInfo.AirNode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.AirNode;
        }
        else
        {
          _botInfo &= ~BotInfo.AirNode;
        }
      }
    }

    internal bool CanUseWaterNodes
    {
      get { return (_botInfo & BotInfo.WaterNode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.WaterNode;
        }
        else
        {
          _botInfo &= ~BotInfo.WaterNode;
        }
      }
    }

    internal bool CanUseLadders
    {
      get { return (_botInfo & BotInfo.LadderNode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.LadderNode;
        }
        else
        {
          _botInfo &= ~BotInfo.LadderNode;
        }
      }
    }

    internal bool CanUseSeats
    {
      get { return (_botInfo & BotInfo.SeatNode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.SeatNode;
        }
        else
        {
          _botInfo &= ~BotInfo.SeatNode;
        }
      }
    }

    internal bool GroundNodesFirst
    {
      get { return (_botInfo & BotInfo.GroundFirst) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.GroundFirst;
        }
        else
        {
          _botInfo &= ~BotInfo.GroundFirst;
        }
      }
    }

    internal bool WaterNodesOnly
    {
      get { return (_botInfo & BotInfo.WaterNodeOnly) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.WaterNodeOnly;
        }
        else
        {
          _botInfo &= ~BotInfo.WaterNodeOnly;
        }
      }
    }

    internal bool RequiresJetpack
    {
      get { return (_botInfo & BotInfo.JetPackReq) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.JetPackReq;
        }
        else
        {
          _botInfo &= ~BotInfo.JetPackReq;
        }
      }
    }

    internal bool JetpackEnabled
    {
      get { return (_botInfo & BotInfo.JetPackEnabled) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.JetPackEnabled;
        }
        else
        {
          _botInfo &= ~BotInfo.JetPackEnabled;
        }
      }
    }

    internal bool EnableDespawnTimer
    {
      get { return (_botInfo & BotInfo.Despawn) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.Despawn;
        }
        else
        {
          _botInfo &= ~BotInfo.Despawn;
        }
      }
    }

    internal bool WantsTarget
    {
      get { return (_botInfo & BotInfo.WantsTarget) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.WantsTarget;
        }
        else
        {
          _botInfo &= ~BotInfo.WantsTarget;
        }
      }
    }

    internal bool AwaitingCallBack
    {
      get { return (_botInfo & BotInfo.CallBack) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.CallBack;
        }
        else
        {
          _botInfo &= ~BotInfo.CallBack;
        }
      }
    }

    internal bool LastWasAirNode
    {
      get { return (_botInfo & BotInfo.LastIsAirNode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.LastIsAirNode;
        }
        else
        {
          _botInfo &= ~BotInfo.LastIsAirNode;
        }
      }
    }

    internal bool ShouldLeadTargets
    {
      get { return (_botInfo & BotInfo.LeadTargets) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.LeadTargets;
        }
        else
        {
          _botInfo &= ~BotInfo.LeadTargets;
        }
      }
    }


    internal bool BugZapped
    {
      get { return (_botInfo & BotInfo.BuggZapped) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.BuggZapped;
        }
        else
        {
          _botInfo &= ~BotInfo.BuggZapped;
        }
      }
    }

    internal bool PatrolMode
    {
      get { return (_botInfo & BotInfo.PatrolMode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.PatrolMode;
        }
        else
        {
          _botInfo &= ~BotInfo.PatrolMode;
        }
      }
    }

    internal bool FollowMode
    {
      get { return (_botInfo & BotInfo.FollowMode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfo.FollowMode;
        }
        else
        {
          _botInfo &= ~BotInfo.FollowMode;
        }
      }
    }

    internal float _minDamage, _maxDamage, _followDistanceSqd = 10;
    internal float _blockDamagePerAttack, _blockDamagePerSecond = 100;
    internal float _shotAngleDeviationTan = 0;
    internal Vector3D? _prevMoveTo, _moveTo, _sideNode;
    internal GridBase _currentGraph, _nextGraph, _previousGraph;
    internal PathCollection _pathCollection;
    internal Node _transitionPoint;
    internal BotState _botState;
    internal IMyUseObject UseObject;
    internal Task _task;
    internal Vector3D _lastEnd;
    internal Vector3I _lastCurrent, _lastPrevious, _lastEndLocal;
    internal short _patrolIndex = -1;
    internal int _stuckCounter, _stuckTimer, _stuckTimerReset;
    internal int _tickCount, _xMoveTimer, _noPathCounter, _doorTgtCounter;
    internal uint _pathTimer, _idleTimer, _idlePathTimer, _lowHealthTimer = 1800;
    internal uint _ticksSinceFoundTarget, _damageTicks, _despawnTicks = 25000;
    internal uint _ticksBeforeDamage = 35;
    internal uint _ticksBetweenAttacks = 300;
    internal uint _ticksSinceLastAttack = 1000;
    internal uint _ticksSinceLastDismount = 1000;
    internal float _twoDegToRads = MathHelper.ToRadians(2);
    internal List<Vector3D> _patrolList;
    internal List<MySoundPair> _attackSounds;
    internal List<string> _attackSoundStrings;
    internal MySoundPair _deathSound;
    internal string _deathSoundString;
    internal string _lootContainerSubtype;
    internal MyObjectBuilder_ConsumableItem _energyOB;
    internal BotBehavior Behavior;

    List<IHitInfo> _hitList;
    Task _graphTask;
    BotInfo _botInfo;
    byte _ticksSinceLastIdleTransition;
    bool _transitionIdle;

    Action<WorkData> _graphWorkAction, _graphWorkCallBack;
    Action<WorkData> _pathWorkAction, _pathWorkCallBack;
    readonly GraphWorkData _graphWorkData;
    readonly PathWorkData _pathWorkData;
    readonly internal MyObjectBuilderType _animalBotType = MyObjectBuilderType.Parse("MyObjectBuilder_AnimalBot");

    public void SetShootInterval()
    {
      var gun = Character?.EquippedTool as IMyHandheldGunObject<MyGunBase>;
      if (gun == null)
      {
        TicksBetweenProjectiles = 10;
        DamageModifier = 1;
      }
      else
      {
        var multiplier = 0.334f;

        var weaponItemDef = gun.PhysicalItemDefinition as MyWeaponItemDefinition;
        if (weaponItemDef != null)
        {
          var weaponDef = MyDefinitionManager.Static.GetWeaponDefinition(weaponItemDef.WeaponDefinitionId);
          multiplier = weaponDef.DamageMultiplier;
        }

        TicksBetweenProjectiles = (int)Math.Ceiling(gun.GunBase.ShootIntervalInMiliseconds / 16.667f * 1.5f);
        DamageModifier = multiplier;
      }
    }

    public BotBase(IMyCharacter bot, float minDamage, float maxDamage, GridBase gridBase)
    {
      Character = bot;
      Target = new TargetInfo(this);
      UsePathFinder = gridBase != null;

      _botState = new BotState(this);
      _currentGraph = gridBase;
      _minDamage = minDamage;
      _maxDamage = maxDamage + 1;
      _energyOB = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_ConsumableItem>("Powerkit");

      if (!AiSession.Instance.HitListStack.TryPop(out _hitList) || _hitList == null)
        _hitList = new List<IHitInfo>();
      else
        _hitList.Clear();

      Character.OnClosing += Character_OnClosing;
      Character.OnClose += Character_OnClosing;
      Character.CharacterDied += Character_CharacterDied;

      _graphWorkAction = new Action<WorkData>(CheckGraph);
      _graphWorkCallBack = new Action<WorkData>(CheckGraphComplete);
      _pathWorkAction = new Action<WorkData>(FindPath);
      _pathWorkCallBack = new Action<WorkData>(FindPathCallBack);

      if (!AiSession.Instance.GraphWorkStack.TryPop(out _graphWorkData) || _graphWorkData == null)
        _graphWorkData = new GraphWorkData();

      if (!AiSession.Instance.PathWorkStack.TryPop(out _pathWorkData) || _pathWorkData == null)
        _pathWorkData = new PathWorkData();

      if (!AiSession.Instance.PatrolListStack.TryPop(out _patrolList) || _patrolList == null)
        _patrolList = new List<Vector3D>();
      else
        _patrolList.Clear();
    }

    private void Character_CharacterDied(IMyCharacter bot)
    {
      var inventory = bot?.GetInventory() as MyInventory;
      if (inventory != null)
      {
        MyContainerTypeDefinition container = null;

        if (!string.IsNullOrWhiteSpace(_lootContainerSubtype))
        {
          container = MyDefinitionManager.Static.GetContainerTypeDefinition(_lootContainerSubtype);
        }

        if (container == null)
        {
          var subtype = bot.Definition.Id.SubtypeName;

          if (subtype.IndexOf("spider", StringComparison.OrdinalIgnoreCase) >= 0)
            subtype = "SpaceSpider";
          else if (subtype.IndexOf("wolf", StringComparison.OrdinalIgnoreCase) >= 0)
            subtype = "Wolf";

          var botDef = new MyDefinitionId(_animalBotType, subtype);
          var agentDef = MyDefinitionManager.Static.GetBotDefinition(botDef) as MyAgentDefinition;
          var lootContainer = agentDef?.InventoryContainerTypeId.SubtypeName ?? "DroidLoot";
          container = MyDefinitionManager.Static.GetContainerTypeDefinition(lootContainer);
        }

        if (container != null)
        {
          inventory.GenerateContent(container);
        }
      }

      PlayDeathSound();
      CleanUp(true);
    }

    private void Character_OnClosing(IMyEntity bot)
    {
      CleanUp();
    }

    internal virtual void Close(bool cleanConfig = false, bool removeBot = true)
    {
      CleanUp(cleanConfig, removeBot);

      if (removeBot)
        Character?.Close();
    }

    internal virtual void CleanUp(bool cleanConfig = false, bool removeBot = true)
    {
      IsDead = true;

      if (Character != null)
      {
        Character.OnClosing -= Character_OnClosing;
        Character.OnClose -= Character_OnClosing;
        Character.CharacterDied -= Character_CharacterDied;

        if (Owner != null)
        {
          MyVisualScriptLogicProvider.SetHighlightLocal(Character.Name, -1, playerId: Owner.IdentityId);

          if (!MyAPIGateway.Utilities.IsDedicated && AiSession.Instance.CommandMenu?.ActiveBot?.EntityId == Character.EntityId)
          {
            AiSession.Instance.CommandMenu.CloseMenu();
            AiSession.Instance.CommandMenu.CloseInteractMessage();
            AiSession.Instance.CommandMenu.ResetCommands();
          }

          if (removeBot)
          {
            var packet = new SpawnPacketClient(Character.EntityId, remove: true);
            AiSession.Instance.Network.SendToPlayer(packet, Owner.SteamUserId);

            List<BotBase> helpers;
            if (AiSession.Instance.PlayerToHelperDict.TryGetValue(Owner.IdentityId, out helpers) && helpers?.Count > 0)
            {
              for (int i = 0; i < helpers.Count; i++)
              {
                var h = helpers[i];
                if (h?.Character?.EntityId == Character.EntityId)
                {
                  helpers.RemoveAtFast(i);
                  break;
                }
              }
            }

            if (!BugZapped && this is RepairBot)
            {
              if (Target.IsSlimBlock)
              {
                var packet2 = new ParticlePacket(Character.EntityId, Particles.ParticleInfoBase.ParticleType.Builder, remove: true);

                if (MyAPIGateway.Session.Player != null)
                  packet.Received(AiSession.Instance.Network);

                if (AiSession.Instance.IsServer && MyAPIGateway.Multiplayer.MultiplayerActive)
                  AiSession.Instance.Network.RelayToClients(packet2);
              }
            }
          }
        }

        if (removeBot)
          AiSession.Instance.RemoveBot(Character.EntityId, Owner?.IdentityId ?? 0L, cleanConfig);
      }

      if (_pathWorkData != null)
        AiSession.Instance.PathWorkStack.Push(_pathWorkData);

      if (_graphWorkData != null)
        AiSession.Instance.GraphWorkStack.Push(_graphWorkData);

      if (_pathCollection != null)
        AiSession.Instance.ReturnCollection(_pathCollection);

      if (_hitList != null)
      {
        _hitList.Clear();
        AiSession.Instance.HitListStack.Push(_hitList);
      }

      if (_patrolList != null)
      {
        AiSession.Instance.PatrolListStack.Push(_patrolList);
      }

      if (_attackSounds != null)
      {
        _attackSounds.Clear();
        AiSession.Instance.SoundListStack.Push(_attackSounds);
      }

      if (_attackSoundStrings != null)
      {
        _attackSoundStrings.Clear();
        AiSession.Instance.StringListStack.Push(_attackSoundStrings);
      }

      Target?.Close();
      Behavior?.Close();

      Target = null;
      Behavior = null;
      _hitList = null;
      _patrolList = null;
      _attackSounds = null;
      _attackSoundStrings = null;
      _currentGraph = null;
      _nextGraph = null;
      _previousGraph = null;
      _pathCollection = null;
      _pathWorkAction = null;
      _pathWorkCallBack = null;
      _graphWorkAction = null;
      _graphWorkCallBack = null;
    }

    internal virtual void PlayDeathSound()
    {
      if (_deathSound == null)
        return;

      if (MyAPIGateway.Utilities.IsDedicated)
        PlaySoundServer(_deathSoundString, Character.EntityId);
      else
      {
        if (MyAPIGateway.Multiplayer.MultiplayerActive && AiSession.Instance.IsServer)
        {
          var packet = new SoundPacket(_deathSoundString, Character.EntityId);
          AiSession.Instance.Network.RelayToClients(packet);
        }

        var soundComp = Character?.Components?.Get<MyCharacterSoundComponent>();
        soundComp?.PlayActionSound(_deathSound);
      }
    }

    internal virtual void Attack()
    {
      if (_ticksSinceLastAttack < _ticksBetweenAttacks)
        return;

      _ticksSinceLastAttack = 0;
      _damageTicks = 0;
      DamagePending = true;

      Character.TriggerCharacterAnimationEvent("Attack", true);
      PlaySound();
    }

    internal virtual void PlaySound(string sound = null, bool stop = false)
    {
      var isNull = string.IsNullOrWhiteSpace(sound);
      if (isNull && (_attackSounds == null || _attackSounds.Count == 0))
        return;

      var rand = isNull ? MyUtils.GetRandomInt(0, _attackSounds.Count) : 0;
      MySoundPair soundPair;

      if (!isNull)
      {
        if (!AiSession.Instance.SoundPairDict.TryGetValue(sound, out soundPair))
        {
          soundPair = new MySoundPair(sound);
          if (string.IsNullOrWhiteSpace(soundPair?.SoundId.ToString()))
            return;

          AiSession.Instance.SoundPairDict[sound] = soundPair;
        }
      }
      else
        soundPair = _attackSounds[rand];

      if (MyAPIGateway.Utilities.IsDedicated)
      {
        var soundString = isNull ? _attackSoundStrings[rand] : sound;
        if (string.IsNullOrWhiteSpace(soundString))
          return;

        PlaySoundServer(soundString, Character.EntityId, stop);
      }
      else
      {
        if (string.IsNullOrWhiteSpace(soundPair?.SoundId.ToString()))
          return;

        if (MyAPIGateway.Multiplayer.MultiplayerActive && AiSession.Instance.IsServer)
        {
          PlaySoundServer(soundPair.SoundId.ToString(), Character.EntityId, stop);
        }

        var soundComp = Character.Components?.Get<MyCharacterSoundComponent>();

        if (stop)
        {
          // figure out how to stop them...
        }
        else 
          soundComp?.PlayActionSound(soundPair);
      }
    }

    internal void PlaySoundServer(string sound, long entityId, bool stop = false)
    {
      if (MyAPIGateway.Multiplayer.MultiplayerActive)
      {
        var packet = new SoundPacket(sound, entityId, stop);
        AiSession.Instance.Network.RelayToClients(packet);
      }

      if (MyAPIGateway.Session.Player != null)
      {
        AiSession.Instance.PlaySoundForEntity(entityId, sound, stop, false);
      }
    }

    MyStringHash _roboDogSubtype = MyStringHash.GetOrCompute("RoboDog");
    public bool IsDead { get; private set; }
    public Vector3D GetPosition()
    {
      if (Character != null)
      {
        var center = Character.WorldAABB.Center;

        if (Character.Definition.Id.SubtypeId == _roboDogSubtype)
          center += Character.WorldMatrix.Up * 0.5;

        return center;
      }

      return Vector3D.Zero;
    }
    public MatrixD WorldMatrix => Character?.WorldMatrix ?? MatrixD.Identity;
    internal void GiveControl(IMyEntityController controller) => controller.TakeControl(Character);

    internal void UpdatePatrolPoints(List<Vector3D> waypoints)
    {
      _patrolList.Clear();
      _patrolIndex = -1;

      if (waypoints != null)
      {
        _patrolList.AddRange(waypoints);
      }
    }

    internal void UpdatePatrolPoints(List<SerializableVector3D> waypoints)
    {
      _patrolList.Clear();
      _patrolIndex = -1;

      if (waypoints?.Count > 0)
      {
        for (int i = 0; i < waypoints.Count; i++)
          _patrolList.Add(waypoints[i]);

        PatrolMode = true;
      }
    }

    internal Vector3D? GetNextPatrolPoint()
    {
      if (_patrolList == null || _patrolList.Count == 0)
      {
        return null;
      }

      var num = ++_patrolIndex % _patrolList.Count;
      return _patrolList[num];
    }

    internal virtual void Reset()
    {
      if (_pathCollection == null)
        return;

      _pathCollection.PathTimer.Stop();
      _pathCollection.ClearNode(true);
    }

    internal virtual void CheckPathCounter()
    {
      if (_currentGraph?.Ready != true || _pathCollection == null)
        return;

      if (_noPathCounter > 10 && (Owner != null || (!_pathCollection.HasPath && !_pathCollection.HasNode)))
      {

        _noPathCounter = 0;
        Vector3D? worldPos;
        Node pn;

        var direction = GetTravelDirection();
        var distance = MyUtils.GetRandomInt(10, (int)_currentGraph.OBB.HalfExtent.AbsMax());
        var botPosition = GetPosition();

        if (_currentGraph.IsGridGraph)
        {
          var gridGraph = _currentGraph as CubeGridMap;

          var ownerPos = Owner?.Character?.WorldAABB.Center ?? Vector3D.Zero;
          var localOwner = gridGraph.WorldToLocal(ownerPos);

          if (Owner?.Character == null || !gridGraph.OpenTileDict.TryGetValue(localOwner, out pn))
          {
            worldPos = gridGraph.GetLastValidNodeOnLine(botPosition, direction, distance, false);
            var localPos = gridGraph.WorldToLocal(worldPos.Value);

            if (!gridGraph.OpenTileDict.ContainsKey(localPos))
            {
              var upDir = gridGraph.WorldMatrix.GetClosestDirection(gridGraph.WorldMatrix.Up);
              var upVec = Base6Directions.GetIntVector(upDir);
              int count = 0;

              var newLocal = localPos - upVec;
              while (count < 10 && !gridGraph.OpenTileDict.ContainsKey(newLocal))
              {
                newLocal -= upVec;
                count++;
              }

              if (gridGraph.OpenTileDict.ContainsKey(newLocal))
                localPos = newLocal;
            }

            pn = gridGraph.OpenTileDict.GetValueOrDefault(localPos, null);
          }
        }
        else
        {
          var pos = botPosition + direction * distance;
          _currentGraph.GetRandomOpenNode(this, pos, out pn);
        }

        lock (_pathCollection.PathToTarget)
        {
          _pathCollection.CleanUp(true);
          
          if (pn != null)
            _pathCollection.PathToTarget.Enqueue(pn);
        }

        if (Target.Override.HasValue)
          Target.RemoveOverride(false);
      }
    }

    internal virtual void ReturnHome() { }
    internal abstract void MoveToTarget();
    internal abstract void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1);

    internal virtual bool Update()
    {
      ++_tickCount;
      ++_ticksSinceLastAttack;
      ++_xMoveTimer;
      ++_pathTimer;
      ++_ticksSinceFoundTarget;
      ++_ticksSinceLastDismount;
      ++_lowHealthTimer;
      ++_idlePathTimer;

      if (EnableDespawnTimer && _ticksSinceFoundTarget > _despawnTicks)
      {
        if (UseAPITargets || Owner != null)
        {
          _ticksSinceFoundTarget = 0;
        }
        else
        {
          Close();
          return false;
        }
      }

      Character.Flags &= ~EntityFlags.NeedsUpdate100;

      if (AiSession.Instance.ShieldAPILoaded && _tickCount % 2 == 0)
      {
        List<MyEntity> entList;
        if (!AiSession.Instance.EntListStack.TryPop(out entList) || entList == null)
          entList = new List<MyEntity>();
        else
          entList.Clear();

        var center = Character.WorldAABB.Center;
        var radius = Character.LocalVolume.Radius;
        var sphere = new BoundingSphereD(center, radius);
        MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entList, MyEntityQueryType.Dynamic);

        for (int i = 0; i < entList.Count; i++)
        {
          var shieldent = entList[i];
          if (ProjectileConstants.ShieldHash == shieldent?.DefinitionId?.SubtypeId && shieldent.Render.Visible)
          {
            var shieldInfo = AiSession.Instance.ShieldAPI.MatchEntToShieldFastExt(shieldent, true);
            if (shieldInfo != null && shieldInfo.Value.Item2.Item1)
            {
              var botIdentityId = Owner?.IdentityId ?? Character.ControllerInfo.ControllingIdentityId;
              var blockOwnerId = shieldInfo.Value.Item1.OwnerId;

              var relation = MyIDModule.GetRelationPlayerPlayer(botIdentityId, blockOwnerId, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
              if (relation == MyRelationsBetweenPlayers.Enemies)
              {
                var hitPoint = AiSession.Instance.ShieldAPI.GetClosestShieldPoint(shieldInfo.Value.Item1, center);
                if (hitPoint.HasValue && Vector3D.DistanceSquared(hitPoint.Value, center) < radius * radius)
                {
                  bool hasPlayer = MyAPIGateway.Session.Player != null;

                  AiSession.Instance.ShieldAPI.PointAttackShieldCon(shieldInfo.Value.Item1, hitPoint.Value, Character.EntityId, Character.Integrity * 10, 0f, true, hasPlayer);

                  var particlePkt = new ParticlePacket(Character.EntityId, Particles.ParticleInfoBase.ParticleType.Shield, center);

                  if (MyAPIGateway.Multiplayer.MultiplayerActive)
                  {
                    AiSession.Instance.Network.RelayToClients(particlePkt);

                    var shieldHitPkt = new ShieldHitPacket(shieldInfo.Value.Item1.EntityId, Character.EntityId, hitPoint.Value, Character.Integrity * 10);
                    AiSession.Instance.Network.RelayToClients(shieldHitPkt);
                  }

                  if (hasPlayer)
                  {
                    particlePkt.Received(AiSession.Instance.Network);
                  }

                  BugZapped = true;
                  break;
                }
              }
            }
          }
        }

        entList.Clear();
        AiSession.Instance.EntListStack.Push(entList);

        if (BugZapped)
        {
          Close(Owner != null);
          return false;
        }
      }

      Behavior?.Update();
      _botState.UpdateBotState();

      if (Target != null)
      {
        Target.Update();

        if (Target.Entity != null || PatrolMode || FollowMode || UseAPITargets)
        {
          _ticksSinceFoundTarget = 0;
        }
      }
      else if (PatrolMode || FollowMode || UseAPITargets)
      {
        _ticksSinceFoundTarget = 0;
      }

      var jetComp = Character.Components?.Get<MyCharacterJetpackComponent>();
      JetpackEnabled = jetComp?.TurnedOn ?? false;

      if (JetpackEnabled)
        SetVelocity();

      bool inSeat = Character?.Parent is IMyCockpit;
      bool collectionOK = _pathCollection != null;

      if (_previousGraph?.LastActiveTicks > 10)
      {
        _previousGraph.LastActiveTicks = 6;
      }

      if (_nextGraph?.LastActiveTicks > 10)
      {
        _nextGraph.LastActiveTicks = 6;
      }

      if (_currentGraph != null)
      {
        _currentGraph.LastActiveTicks = 0;
        _currentGraph.IsActive = true;

        if (!inSeat && _currentGraph.Ready && !UseLadder && !_botState.IsOnLadder && !_botState.WasOnLadder
          && collectionOK && (_pathCollection.HasPath || _pathCollection.HasNode))
          ++_stuckTimer;
      }

      bool isTick100 = _tickCount % 100 == 0;
      bool checkAll100 = isTick100 && (!inSeat || Owner == null);
      bool tgtDead = Target.IsDestroyed();
      if (checkAll100 || tgtDead)
      {
        if (tgtDead)
        {
          _sideNode = null;

          if (this is NomadBot)
            Target.RemoveTarget();
        }

        if (HasWeaponOrTool)
        {
          var tool = Character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
          if (tool == null)
          {
            var charController = Character as Sandbox.Game.Entities.IMyControllableEntity;
            //charController?.SwitchToWeapon(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), ToolSubtype));
            charController?.SwitchToWeapon(ToolDefinition.PhysicalItemId);
          }
        }

        if (!UseAPITargets)
          SetTarget();

        if (checkAll100)
        {
          CheckGraphNeeded = true;

          if (_currentGraph != null && _currentGraph.IsValid && Character != null && !IsDead
            && Math.Abs(VectorUtils.GetAngleBetween(Character.WorldMatrix.Up, _currentGraph.WorldMatrix.Up)) > MathHelper.ToRadians(3))
          {
            var matrix = _currentGraph.WorldMatrix;
            matrix.Translation = Character.WorldMatrix.Translation;

            Character.SetWorldMatrix(matrix);
          }

          if (Owner?.Character != null)
          {
            if (Owner.Character.EnabledLights != Character.EnabledLights)
              Character.SwitchLights();

            if (CanUseSeats && !UseAPITargets && !PatrolMode && Owner.Character.Parent is IMyCockpit && !(Character.Parent is IMyCockpit)
              && Vector3D.DistanceSquared(GetPosition(), Owner.Character.WorldAABB.Center) <= 10000)
              AiSession.Instance.PlayerEnteredCockpit(null, Owner.IdentityId, null);
          }
          else if (Character.EnabledLights)
            Character.SwitchLights();
        }
      }

      if (_tickCount % 150 == 0)
      {
        SwitchWalk = !inSeat;

        bool lowHealth = false;
        if (Owner != null && _lowHealthTimer > 1800 && !(this is ScavengerBot))
        {
          var statComp = Character.Components?.Get<MyEntityStatComponent>() as MyCharacterStatComponent;
          var healthRatio = statComp?.HealthRatio ?? 1f;
          lowHealth = healthRatio < 0.25f;
        }

        if (BehaviorReady)
        {
          if (lowHealth && Owner != null)
          {
            BehaviorReady = false;
            _lowHealthTimer = 0;
            AiSession.Instance.GlobalSpeakTimer = 0;
            Behavior.Speak("CriticalBatteries");
          }
          else if (this is NomadBot || this is ScavengerBot)
          {
            BehaviorReady = false;
            UseBehavior();
          }
          else if (AiSession.Instance?.GlobalSpeakTimer > 1000 && (inSeat || (Target.Entity != null && Target.GetDistanceSquared() < 2500 && !Target.IsFriendly())))
          {
            AiSession.Instance.GlobalSpeakTimer = 0;
            BehaviorReady = false;

            if (inSeat)
            {
              if (AiSession.Instance.AllowMusic)
              {
                var num = MyUtils.GetRandomInt(0, 10);
                if (num > 6)
                  Behavior?.Sing();
              }
            }
            else
            {
              UseBehavior();
            }
          }
        }
      }

      if (_tickCount % 1500 == 0)
      {
        var energy = Character.SuitEnergyLevel;
        if (energy < 0.25f)
        {
          var inv = Character.GetInventory() as MyInventory;
          if (inv?.AddItems(1, _energyOB) == true)
            inv.ConsumeItem(_energyOB.GetId(), 1, Character.EntityId);
        }

        var oxyComp = Character.Components?.Get<MyCharacterOxygenComponent>();
        if (oxyComp != null)
        {
          oxyComp.SuitOxygenLevel = 1f;

          var gasId = MyCharacterOxygenComponent.HydrogenId;
          oxyComp.UpdateStoredGasLevel(ref gasId, 1f);
        }
      }

      if (_tickCount % 3600 == 0)
        BehaviorReady = true;

      if (DamagePending)
        UpdateDamagePending();

      return true;
    }

    internal virtual void UseBehavior()
    {
      Behavior?.Speak();
    }

    void SetVelocity()
    {
      var velocity = Character.Physics?.LinearVelocity ?? Vector3.Zero;

      if (Vector3.IsZero(velocity))
        return;

      var maxVelocity = 4f;
      var gridLinearVelocity = Vector3.Zero;

      if (_currentGraph != null)
      {
        var botPosition = GetPosition();
        var positionInTwo = botPosition + velocity;

        float desiredVelocity;
        if (!_currentGraph.OBB.Contains(ref positionInTwo))
          desiredVelocity = 7f;
        else
          desiredVelocity = _currentGraph.RootVoxel == null ? 25f : 10f;

        var ch = Target.Entity as IMyCharacter;
        bool isOwner = ch != null && ch.EntityId == Owner?.Character?.EntityId;

        if (_pathCollection != null && _pathCollection.HasNode)
        {
          var pathNode = _pathCollection.NextNode;
          var tgtPos = _currentGraph.LocalToWorld(pathNode.Position) + pathNode.Offset;

          var distanceTo = Vector3D.DistanceSquared(tgtPos, botPosition);
          var ratio = (float)MathHelper.Clamp(distanceTo / 25, 0, 1);
          maxVelocity = MathHelper.Lerp(4f, desiredVelocity, ratio);
        }
        else
        {
          var tgtEnt = Target.Entity as IMyEntity;
          if (isOwner)
          {
            var minDistance = _followDistanceSqd * 2;
            var distanceTo = Vector3D.DistanceSquared(tgtEnt.WorldAABB.Center, botPosition) - minDistance;
            var ratio = (float)MathHelper.Clamp(distanceTo / minDistance, 0, 1);
            maxVelocity = MathHelper.Lerp(4f, 25f, ratio);
          }
          else if (Target.PositionsValid)
          {
            var tgtPos = Target.CurrentGoToPosition;
            var distanceTo = Vector3D.DistanceSquared(tgtPos, botPosition);
            var ratio = (float)MathHelper.Clamp(distanceTo / 25, 0, 1);
            maxVelocity = MathHelper.Lerp(4f, desiredVelocity, ratio);
          }
          else if (tgtEnt != null)
          {
            var tgtPos = tgtEnt.WorldAABB.Center;
            var distanceTo = Vector3D.DistanceSquared(tgtPos, botPosition);
            var ratio = (float)MathHelper.Clamp(distanceTo / 25, 0, 1);
            maxVelocity = MathHelper.Lerp(4f, desiredVelocity, ratio);
          }
        }

        if (_currentGraph.IsGridGraph)
        {
          var grid = ((CubeGridMap)_currentGraph).MainGrid;
          if (!grid.IsStatic)
          {
            gridLinearVelocity = grid.Physics.LinearVelocity;
            velocity -= gridLinearVelocity;
          }
        }
      }

      if (velocity.LengthSquared() > maxVelocity * maxVelocity)
        Character.Physics.LinearVelocity = Vector3.Normalize(velocity) * maxVelocity + gridLinearVelocity;
    }

    internal virtual void UpdateDamagePending()
    {
      ++_damageTicks;

      if (_damageTicks > _ticksBeforeDamage)
      {
        DamagePending = false;
        DoDamage();
      }
    }

    internal virtual void UpdateRelativeDampening()
    {
      if (_currentGraph?.IsGridGraph != true)
        return;

      var gridGraph = _currentGraph as CubeGridMap;
      var grid = gridGraph.MainGrid;

      if (grid?.Physics == null || grid.IsStatic)
        return;

      var controlEnt = Character as Sandbox.Game.Entities.IMyControllableEntity;
      if (controlEnt == null || controlEnt.RelativeDampeningEntity?.EntityId == grid.EntityId)
        return;

      controlEnt.RelativeDampeningEntity = grid;
    }

    internal virtual void DoDamage(float amount = 0)
    {
      IMyDestroyableObject destroyable;
      var cube = Target.Entity as IMyCubeBlock;
      if (cube != null)
      {
        destroyable = cube.SlimBlock;
        PlaySoundServer("ImpMetalMetalCat3", cube.EntityId);
      }
      else
      {
        destroyable = Target.Entity as IMyDestroyableObject;
      }

      if (destroyable == null || !destroyable.UseDamageSystem || destroyable.Integrity <= 0)
        return;

      var character = Target.Entity as IMyCharacter;
      bool isCharacter = character != null;
      var rand = amount > 0 ? amount : isCharacter ? MyUtils.GetRandomFloat(_minDamage, _maxDamage) : _blockDamagePerAttack;

      BotBase botTarget = null;

      if (cube != null || (isCharacter && AiSession.Instance.Players.ContainsKey(character.ControllerInfo.ControllingIdentityId)))
      {
        rand *= AiSession.Instance.ModSaveData.BotWeaponDamageModifier;
      }
      else if (isCharacter && AiSession.Instance.Bots.TryGetValue(character.EntityId, out botTarget) && botTarget?.Owner != null)
      {
        rand *= AiSession.Instance.ModSaveData.BotWeaponDamageModifier;
      }

      destroyable.DoDamage(rand, MyStringHash.GetOrCompute("Punch"), true);

      if (!isCharacter)
        return;

      if (botTarget != null)
      {
        botTarget._ticksSinceFoundTarget = 0;

        if (Owner != null)
        {
          HealthInfoStat infoStat;
          if (!AiSession.Instance.PlayerToHealthBars.TryGetValue(Owner.IdentityId, out infoStat))
          {
            infoStat = new HealthInfoStat();
            AiSession.Instance.PlayerToHealthBars[Owner.IdentityId] = infoStat;
          }

          infoStat.BotEntityIds.Add(character.EntityId);
        }

        var nomad = botTarget as NomadBot;
        if (nomad != null && nomad.Target.Entity == null)
        {
          nomad.SetHostile(Character);
        }
      }
    }

    internal virtual bool IsInRangeOfTarget()
    {
      if (Target?.HasTarget != true || Vector3D.IsZero(GetPosition()))
        return false;

      return Target.IsFriendly() || Target.GetDistanceSquared() < 650000;
    }

    public virtual void AddWeapon() { }

    public virtual void EquipWeapon()
    {
      if (ToolDefinition == null)
        return;
      //if (string.IsNullOrWhiteSpace(ToolSubtype))
      //  return;

      //var weaponDefinition = new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), ToolSubtype);
      var weaponDefinition = ToolDefinition.PhysicalItemId;

      var charController = Character as Sandbox.Game.Entities.IMyControllableEntity;
      if (charController.CanSwitchToWeapon(weaponDefinition))
      {
        charController.SwitchToWeapon(weaponDefinition);
        HasWeaponOrTool = true;
        SetShootInterval();
      }
      else
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.EquipWeapon: WARNING! Unable to switch to weapon ({weaponDefinition})!", MessageType.WARNING);
    }

    public virtual void SetTarget()
    {
      if (!WantsTarget)
        return;

      if (Target.IsDestroyed())
        Target.RemoveTarget();
      else if (Target.Entity is IMyDoor)
      {
        ++_doorTgtCounter;
        if (_doorTgtCounter <= 8)
          return;

        Target.RemoveTarget();
      }

      List<MyEntity> blockTargets;
      if (!AiSession.Instance.EntListStack.TryPop(out blockTargets) || blockTargets == null)
        blockTargets = new List<MyEntity>();

      List<IMyCubeGrid> gridGroups;
      if (!AiSession.Instance.GridGroupListStack.TryPop(out gridGroups) || gridGroups == null)
        gridGroups = new List<IMyCubeGrid>();

      List<MyEntity> entList;
      if (!AiSession.Instance.EntListStack.TryPop(out entList) || entList == null)
        entList = new List<MyEntity>();
      else
        entList.Clear();

      HashSet<long> checkedGridIDs;
      if (!AiSession.Instance.GridCheckHashStack.TryPop(out checkedGridIDs) || checkedGridIDs == null)
        checkedGridIDs = new HashSet<long>();
      else
        checkedGridIDs.Clear();

      var botPosition = GetPosition();
      var sphere = new BoundingSphereD(botPosition, AiSession.Instance.ModSaveData.MaxBotHuntingDistanceEnemy);
      var blockDestroEnabled = MyAPIGateway.Session.SessionSettings.DestructibleBlocks;
      var queryType = blockDestroEnabled ? MyEntityQueryType.Both : MyEntityQueryType.Dynamic;
      MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entList, queryType);

      IMyEntity tgt = null;
      var distance = double.MaxValue;
      entList.ShellSort(botPosition);

      for (int i = 0; i < entList.Count; i++)
      {
        var ent = entList[i];
        if (ent == null || ent.MarkedForClose)
          continue;

        long entOwnerId;
        MyCubeGrid grid;
        var ch = ent as IMyCharacter;

        if (ch != null)
        {
          if (ch.IsDead || ch.MarkedForClose || ch.EntityId == Character.EntityId)
            continue;

          long ownerIdentityId = ownerIdentityId = ch.ControllerInfo.ControllingIdentityId;
          BotBase bot;
          if (AiSession.Instance.Bots.TryGetValue(ch.EntityId, out bot))
          {
            if (bot == null || bot.IsDead)
              continue;

            if (bot.Owner != null)
              ownerIdentityId = bot.Owner.IdentityId;
          }
          else if (ch.IsPlayer)
          {
            if (ch.Parent is IMyCockpit)
            {
              var p = MyAPIGateway.Players.GetPlayerControllingEntity(ch.Parent);
              if (p != null)
                ownerIdentityId = p.IdentityId;
            }

            IMyPlayer player;
            if (!AiSession.Instance.Players.TryGetValue(ownerIdentityId, out player) || player == null)
            {
              continue;
            }

            MyAdminSettingsEnum adminSettings;
            if (MyAPIGateway.Session.TryGetAdminSettings(player.SteamUserId, out adminSettings))
            {
              if ((adminSettings & MyAdminSettingsEnum.Untargetable) != 0)
              {
                continue;
              }
            }
          }

          var relation = MyIDModule.GetRelationPlayerPlayer(ownerIdentityId, Character.ControllerInfo.ControllingIdentityId);
          if (relation == MyRelationsBetweenPlayers.Allies || relation == MyRelationsBetweenPlayers.Self)
            continue;
          else if (relation == MyRelationsBetweenPlayers.Neutral && !AiSession.Instance.ModSaveData.AllowNeutralTargets)
            continue;

          var dSqd = Vector3D.DistanceSquared(ent.PositionComp.WorldAABB.Center, botPosition);
          if (dSqd < distance)
          {
            tgt = ent;
            distance = dSqd;
          }
        }
        else if ((grid = ent as MyCubeGrid)?.Physics != null && !grid.IsPreview && !grid.MarkedForClose && !checkedGridIDs.Contains(grid.EntityId))
        {
          blockTargets.Clear();
          gridGroups.Clear();

          grid.GetGridGroup(GridLinkTypeEnum.Mechanical).GetGrids(gridGroups);
          var thisGridOwner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : 0;

          foreach (var g in gridGroups)
          {
            var myGrid = g as MyCubeGrid;
            if (myGrid == null || myGrid.MarkedForClose)
              continue;

            checkedGridIDs.Add(g.EntityId);
            var myGridOwner = myGrid.BigOwners?.Count > 0 ? myGrid.BigOwners[0] : myGrid.SmallOwners?.Count > 0 ? myGrid.SmallOwners[0] : 0;

            if (myGridOwner != 0 && (thisGridOwner == 0 || grid.BlocksCount < myGrid.BlocksCount))
            {
              thisGridOwner = myGridOwner;
              grid = myGrid;
            }
          }

          entOwnerId = thisGridOwner;
          var relation = MyIDModule.GetRelationPlayerPlayer(entOwnerId, Character.ControllerInfo.ControllingIdentityId);
          if (relation == MyRelationsBetweenPlayers.Allies || relation == MyRelationsBetweenPlayers.Self)
            continue;
          else if (relation == MyRelationsBetweenPlayers.Neutral && !AiSession.Instance.ModSaveData.AllowNeutralTargets)
            continue;

          foreach (var g in gridGroups)
          {
            var myGrid = g as MyCubeGrid;
            if (myGrid == null || myGrid.MarkedForClose)
              continue;

            foreach (var cpit in myGrid.OccupiedBlocks)
            {
              if (cpit.Pilot != null)
                entList.Add(cpit.Pilot);
            }

            if (HasWeaponOrTool && blockDestroEnabled)
            {
              var blockCounter = myGrid?.BlocksCounters;
              var hasTurretOrRotor = (AiSession.Instance.WcAPILoaded && AiSession.Instance.WcAPI.HasGridAi(myGrid))
                || (blockCounter != null 
                && (blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_LargeGatlingTurret), 0) > 0
                || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_LargeMissileTurret), 0) > 0
                || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_InteriorTurret), 0) > 0 
                || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_SmallMissileLauncher), 0) > 0
                || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_SmallMissileLauncherReload), 0) > 0
                || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_SmallGatlingGun), 0) > 0
                || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_TurretControlBlock), 0) > 0
                || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_MotorStator), 0) > 0
                || blockCounter.GetValueOrDefault(typeof(MyObjectBuilder_MotorAdvancedStator), 0) > 0));

              if (hasTurretOrRotor)
              { 
                var blocks = myGrid.GetFatBlocks();
                for (int j = 0; j < blocks.Count; j++)
                {
                  var b = blocks[j];
                  if (b == null || b.MarkedForClose)
                    continue;

                  if (!b.IsWorking || b.SlimBlock.IsBlockUnbuilt())
                    continue;

                  var stator = b as IMyMotorStator;
                  if (stator != null)
                  {
                    if (stator.Enabled && stator.TopGrid != null)
                      blockTargets.Add(b);
                  }
                  else if (AiSession.Instance.AllCoreWeaponDefinitions.Contains(b.BlockDefinition.Id)
                    || b is IMyLargeTurretBase || b is IMySmallGatlingGun
                    || b is IMySmallMissileLauncher || b is IMySmallMissileLauncherReload
                    || b is IMyTurretControlBlock)
                  {
                    blockTargets.Add(b);
                  }
                }
              }
            }
          }

          if (blockTargets.Count > 0)
          {
            blockTargets.ShellSort(botPosition);

              // check for weapons or rotors
            for (int j = 0; j < blockTargets.Count; j++)
            {
              var blockTgt = blockTargets[j];
              var blockPos = blockTgt.PositionComp.WorldAABB.Center;

              IHitInfo hit;
              MyAPIGateway.Physics.CastRay(botPosition, blockPos, out hit, CollisionLayers.CharacterCollisionLayer);

              var hitGrid = hit?.HitEntity as IMyCubeGrid;
              if (hitGrid != null)
              {
                var allowedDistance = hitGrid.GridSizeEnum == MyCubeSize.Large ? 2.5 : 10;
                var d = Vector3D.DistanceSquared(blockPos, hit.Position);
                if (d < allowedDistance * allowedDistance)
                {
                  tgt = blockTgt;
                  break;
                }
              }
            }

            if (tgt == null)
              continue;
          }
          else
            continue;
        }
        else
          continue;

        //var dSqd = Vector3D.DistanceSquared(ent.PositionComp.WorldAABB.Center, botPosition);
        //if (dSqd < distance)
        //{
        //  tgt = ent;
        //  distance = dSqd;
        //}

        if (tgt != null)
          break;
      }

      blockTargets.Clear();
      gridGroups.Clear();
      entList.Clear();
      checkedGridIDs.Clear();

      AiSession.Instance.EntListStack.Push(blockTargets);
      AiSession.Instance.GridGroupListStack.Push(gridGroups);
      AiSession.Instance.EntListStack.Push(entList);
      AiSession.Instance.GridCheckHashStack.Push(checkedGridIDs);

      var onPatrol = PatrolMode && _patrolList?.Count > 0;

      if (tgt == null)
      {
        if (onPatrol)
        {
          if (Target.Entity != null)
            Target.RemoveTarget();

          if (Target.Override.HasValue)
            return;

          var patrolPoint = GetNextPatrolPoint();

          if (patrolPoint.HasValue)
          {
            Target.SetOverride(patrolPoint.Value);
          }
        }
        else
        {
          Target.RemoveTarget();
        }

        return;
      }

      if (onPatrol && Target.Override.HasValue)
      {
        _patrolIndex = Math.Max((short)-1, (short)(_patrolIndex - 1));
        Target.RemoveOverride(false);
      }

      var tgtChar = tgt as IMyCharacter;
      if (tgtChar != null)
      {
        List<BotBase> helpers;
        if (!(tgtChar.Parent is IMyCockpit) && AiSession.Instance.PlayerToHelperDict.TryGetValue(tgtChar.ControllerInfo.ControllingIdentityId, out helpers))
        {
          foreach (var bot in helpers)
          {
            if (bot.IsDead)
              continue;

            var d = Vector3D.DistanceSquared(bot.GetPosition(), botPosition);
            if (d < distance * 0.6)
            {
              tgt = bot.Character;
              distance = d;
            }
          }
        }
      }

      var parent = (tgtChar != null && tgt.Parent != null) ? tgt.Parent : tgt;
      if (ReferenceEquals(Target.Entity, parent))
        return;

      Target.SetTarget(parent);
      _pathCollection?.CleanUp(true);

      var seat = Character.Parent as IMyCockpit;
      if (seat != null && Owner == null && Target.Entity != null)
      {
        seat.RemovePilot();
      }
    }

    internal void TrySwitchWalk()
    {
      if (Character.Definition.Id.SubtypeName.IndexOf("wolf", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        if (!_botState.IsRunning)
          Character.SwitchWalk();
      }
      else if (PatrolMode && Target.Entity == null && AiSession.Instance.ModSaveData.EnforceWalkingOnPatrol)
      {
        if (_botState.IsRunning)
          Character.SwitchWalk();
      }
      else if (SwitchWalk)
      {
        SwitchWalk = false;

        var rand = MyUtils.GetRandomInt(0, 6);
        if (rand < 3)
          Character.SwitchWalk();
      }
    }

    internal bool CheckGraphValidity(Vector3D targetPosition, ref bool force, out MyCubeGrid newGrid, out Vector3D newGraphPosition, out Vector3D intermediary, out bool botInNewBox)
    {
      bool result = false;
      newGrid = null;
      botInNewBox = false;
      newGraphPosition = targetPosition;
      intermediary = targetPosition;
      var botPosition = GetPosition();

      bool positionValid = _currentGraph.IsPositionValid(targetPosition);
      bool getNewGraph = !positionValid && !force;

      if (getNewGraph) 
      {
        double maxLength, lengthToTop;
        var graphCenter = _currentGraph.OBB.Center;
        var vectorGraphToTarget = targetPosition - graphCenter;
        var dir = _currentGraph.WorldMatrix.GetClosestDirection(vectorGraphToTarget);
        var normal = _currentGraph.WorldMatrix.GetDirectionVector(dir);
        var graphUp = (dir == Base6Directions.Direction.Up || dir == Base6Directions.Direction.Down) ? _currentGraph.WorldMatrix.Backward : _currentGraph.WorldMatrix.Up;

        if (_currentGraph.IsGridGraph)
        {
          if (!_currentGraph.GetEdgeDistanceInDirection(normal, out maxLength))
            maxLength = 2 * (VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultCellSize) - (3 * _currentGraph.CellSize);
          else
            maxLength += (VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultCellSize) - (3 * _currentGraph.CellSize);

          if (!_currentGraph.GetEdgeDistanceInDirection(graphUp, out lengthToTop))
            lengthToTop = VoxelGridMap.DefaultHalfSize;
        }
        else
        {
          maxLength = 2 * (VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultCellSize) - (3 * _currentGraph.CellSize);
          lengthToTop = VoxelGridMap.DefaultHalfSize;
        }

        newGraphPosition = graphCenter + normal * maxLength;

        var intVecDir = Base6Directions.GetIntVector(dir);
        var dotUp = Vector3I.Up.Dot(ref intVecDir);

        bool targetAboveBox = false;
        bool targetBelowBox = false;

        Vector3D projUp = VectorUtils.Project(vectorGraphToTarget, graphUp);
        if (projUp.LengthSquared() > lengthToTop * lengthToTop)
        {
          var projGraphUp = projUp.Dot(ref graphUp);
          targetAboveBox = projGraphUp > 0;
          targetBelowBox = projGraphUp < 0;
        }

        var pointAboveNext = newGraphPosition + (graphUp * lengthToTop * 0.9);
        var pointAboveThis = graphCenter + (graphUp * lengthToTop * 0.9) + (normal * maxLength * 0.75);
        var pointBelowThis = graphCenter - (graphUp * lengthToTop * 0.9) + (normal * maxLength * 0.5);

        if (targetAboveBox && dotUp <= 0 && GridBase.PointInsideVoxel(pointAboveNext, _currentGraph.RootVoxel))
        {
          // the target is going to be above the current map box
          maxLength = lengthToTop + (VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultCellSize) - (3 * _currentGraph.CellSize);

          if (GridBase.PointInsideVoxel(pointAboveThis, _currentGraph.RootVoxel))
          {
            newGraphPosition = graphCenter + graphUp * maxLength;
          }
          else
          {
            newGraphPosition += graphUp * maxLength;
          }
        }
        else if (targetBelowBox && dotUp >= 0 && !GridBase.PointInsideVoxel(pointBelowThis, _currentGraph.RootVoxel))
        {
          // the target is going to be below the current map box
          maxLength = lengthToTop + (VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultCellSize) - (3 * _currentGraph.CellSize);
          newGraphPosition = graphCenter - graphUp * maxLength;
        }

        var vectorBotToTgt = targetPosition - botPosition;
        if (vectorBotToTgt.LengthSquared() > 15625) // 125 * 125
        {
          var edgePoint = _currentGraph.GetBufferZoneTargetPosition(targetPosition, graphCenter, true);
          if (edgePoint.HasValue)
          {
            intermediary = edgePoint.Value;
          }
          else
          {
            vectorBotToTgt.Normalize();
            intermediary = botPosition + vectorBotToTgt * 50;
          }
        }
      }

      List<IMyCubeGrid> gridGroups;
      if (!AiSession.Instance.GridGroupListStack.TryPop(out gridGroups) || gridGroups == null)
        gridGroups = new List<IMyCubeGrid>();
      else
        gridGroups.Clear();

      List<MyLineSegmentOverlapResult<MyEntity>> rayEntities;
      if (!AiSession.Instance.OverlapResultListStack.TryPop(out rayEntities) || rayEntities == null)
        rayEntities = new List<MyLineSegmentOverlapResult<MyEntity>>();
      else
        rayEntities.Clear();

      HashSet<long> checkedGridIDs;
      if (!AiSession.Instance.GridCheckHashStack.TryPop(out checkedGridIDs) || checkedGridIDs == null)
        checkedGridIDs = new HashSet<long>();
      else
        checkedGridIDs.Clear();

      var lineToNewGraph = new LineD(botPosition, newGraphPosition);
      var lineToTarget = new LineD(botPosition, targetPosition);
      MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref lineToNewGraph, rayEntities);

      MyCubeGrid bigGrid = null;
      MyOrientedBoundingBoxD? newGridOBB = null;

      var gridGraph = _currentGraph as CubeGridMap;
      if (gridGraph?.MainGrid != null && !gridGraph.MainGrid.MarkedForClose)
      {
        bigGrid = gridGraph.MainGrid;
      }

      double distanceTargetToBot = Vector3D.DistanceSquared(botPosition, newGraphPosition);
      foreach (var overlapResult in rayEntities)
      {
        var grid = overlapResult.Element as MyCubeGrid;
        if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose)
        {
          continue;
        }

        if (grid.GridSize < 1 || grid.EntityId == bigGrid?.EntityId || grid.BlocksCount <= 5 || checkedGridIDs.Contains(grid.EntityId))
        {
          continue;
        }

        bool keepGoing = false;
        var min = grid.WorldToGridInteger(newGraphPosition);
        var max = grid.WorldToGridInteger(botPosition);
        Vector3I.MinMax(ref min, ref max);

        min = Vector3I.Max(grid.Min, min);
        max = Vector3I.Min(grid.Max, max);

        Vector3I_RangeIterator iter = new Vector3I_RangeIterator(ref min, ref max);
        while (iter.IsValid())
        {
          var current = iter.Current;
          iter.MoveNext();

          if (grid.CubeExists(current))
          {
            keepGoing = true;
            break;
          }
        }

        if (!keepGoing)
          continue;

        gridGroups.Clear();
        grid.GetGridGroup(GridLinkTypeEnum.Mechanical).GetGrids(gridGroups);

        MyCubeGrid biggest = grid;

        foreach (MyCubeGrid g in gridGroups)
        {
          if (g != null && !g.MarkedForClose && g.GridSize > 1 && g.BlocksCount > biggest.BlocksCount)
            biggest = g;
        }

        checkedGridIDs.Add(biggest.EntityId);
        BoundingBoxI box = new BoundingBoxI(biggest.Min, biggest.Max);

        foreach (var g in gridGroups)
        {
          if (g == null || g.MarkedForClose || g.EntityId == biggest.EntityId)
            continue;

          checkedGridIDs.Add(g.EntityId);
          min = biggest.WorldToGridInteger(g.GridIntegerToWorld(g.Min));
          max = biggest.WorldToGridInteger(g.GridIntegerToWorld(g.Max));

          box = box.Include(ref min);
          box = box.Include(ref max);
        }

        var center = biggest.GridIntegerToWorld(box.Center);
        var halfExtents = (box.HalfExtents + Vector3.Half) * biggest.GridSize;
        var upDir = biggest.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
        var upVec = Base6Directions.GetIntVector(upDir);
        halfExtents += (upVec * 3) * biggest.GridSize;

        var quat = Quaternion.CreateFromRotationMatrix(biggest.WorldMatrix);
        var obb = new MyOrientedBoundingBoxD(center, halfExtents, quat);

        if (obb.IntersectsOrContains(ref lineToTarget) == null)
          continue;

        var addition = biggest.Physics.IsStatic ? 10 : 5;
        obb.HalfExtent = ((Vector3)(box.HalfExtents + addition) + Vector3.Half) * biggest.GridSize;

        if (obb.Contains(ref newGraphPosition) || obb.Contains(ref botPosition))
        {
          var distance = overlapResult.Distance;
          if (distance < distanceTargetToBot)
          {
            distanceTargetToBot = distance;
            newGrid = biggest;
            newGridOBB = obb;
          }
        }
      }

      gridGroups.Clear();
      rayEntities.Clear();
      checkedGridIDs.Clear();

      AiSession.Instance.GridGroupListStack.Push(gridGroups);
      AiSession.Instance.OverlapResultListStack.Push(rayEntities);
      AiSession.Instance.GridCheckHashStack.Push(checkedGridIDs);

      if (newGrid != null && newGridOBB != null)
      {
        botInNewBox = newGridOBB.Value.Contains(ref botPosition);

        if (!botInNewBox)
        {
          var curOBB = _currentGraph.OBB;
          if (!newGridOBB.Value.Intersects(ref curOBB))
          {
            newGrid = null;
          }
        }
      }
      else if (positionValid)
        result = true;

      return result;
    }

    internal bool CheckIfCloseEnoughToAct(ref Vector3D targetPosition, out bool shouldReturn)
    {
      shouldReturn = false;
      var botPos = GetPosition();
      var localBot = _currentGraph.WorldToLocal(botPos);
      var localTgt = _currentGraph.WorldToLocal(targetPosition);
      var manhattanDist = Vector3I.DistanceManhattan(localTgt, localBot);

      if (targetPosition == Target.Override)
      {
        if (_currentGraph.IsGridGraph)
        {
          var gridGraph = _currentGraph as CubeGridMap;
          var localTarget = gridGraph.WorldToLocal(targetPosition);

          if (PatrolMode)
          {
            Node node;
            if (gridGraph.TryGetNodeForPosition(localTarget, out node))
            {
              var checkPosition = _currentGraph.LocalToWorld(node.Position) + node.Offset;
              var distanceSqd = Vector3D.DistanceSquared(checkPosition, botPos);
              shouldReturn = distanceSqd < 1.75;
              return shouldReturn;
            }
          }

          var cube = gridGraph.GetBlockAtPosition(localTarget);
          var seat = cube?.FatBlock as IMyCockpit;
          if (seat != null)
          {
            if (CanUseSeats && seat.Pilot == null && (localTgt - localBot).AbsMax() < 2 && manhattanDist <= 2)
            {
              var seatCube = seat as MyCubeBlock;
              var useObj = _pathCollection.GetBlockUseObject(seatCube);
              if (useObj != null)
              {
                var shareMode = seatCube.IDModule?.ShareMode ?? MyOwnershipShareModeEnum.All;
                bool changeBack = false;

                if (shareMode != MyOwnershipShareModeEnum.All)
                {
                  var owner = Owner?.IdentityId ?? Character.ControllerInfo.ControllingIdentityId;
                  var gridOwner = seat.CubeGrid.BigOwners?.Count > 0 ? seat.CubeGrid.BigOwners[0] : seat.CubeGrid.SmallOwners?.Count > 0 ? seat.CubeGrid.SmallOwners[0] : seat.SlimBlock.BuiltBy;

                  var relation = MyIDModule.GetRelationPlayerPlayer(owner, gridOwner, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
                  if (relation != MyRelationsBetweenPlayers.Enemies)
                  {
                    changeBack = true;
                    seatCube.IDModule.ShareMode = MyOwnershipShareModeEnum.All;
                  }
                }

                if (seatCube.IDModule == null || seatCube.IDModule.ShareMode == MyOwnershipShareModeEnum.All)
                {
                  var relativePosition = Vector3D.Rotate(botPos - seat.GetPosition(), MatrixD.Transpose(seat.WorldMatrix));
                  AiSession.Instance.BotToSeatRelativePosition[Character.EntityId] = relativePosition;
                  useObj.Use(UseActionEnum.Manipulate, Character);
                }

                if (changeBack)
                  seatCube.IDModule.ShareMode = shareMode;

                shouldReturn = true;
                return true;
              }
            }

            return manhattanDist < 2;
          }
        }

        var distance = Vector3D.DistanceSquared(botPos, targetPosition);
        return distance < 1.75;
      }

      if (localBot != localTgt)
      {
        bool isFriendly = Target.IsFriendly();
        bool isInv = Target.IsInventory;
        bool isSlim = isInv || Target.IsSlimBlock;
        bool isCube = !isSlim && Target.IsCubeBlock;
        bool checkCube = isInv || isSlim || isCube;
        float checkDistance;

        if (checkCube)
        {
          var slim = isInv ? Target.Inventory : isCube ? ((IMyCubeBlock)Target.Entity).SlimBlock : Target.Entity as IMySlimBlock;
          checkDistance = slim == null ? (isFriendly ? 4 : 2) : Math.Max(4, (slim.Max - slim.Min).RectangularLength() + 2);
        }
        else
        {
          checkDistance = isFriendly ? 4 : 2;
        }

        if (manhattanDist > checkDistance || _pathCollection.PathToTarget.Count > checkDistance)
          return false;

        if (checkCube)
        {
          var slim = isInv ? Target.Inventory : Target.Entity as IMySlimBlock;
          if (slim != null)
          {
            if (slim.FatBlock != null)
            {
              checkDistance = slim.FatBlock.PositionComp.LocalAABB.HalfExtents.AbsMax() + 4f;
            }
            else
            {
              BoundingBoxD box;
              slim.GetWorldBoundingBox(out box, true);
              checkDistance = (float)box.HalfExtents.AbsMax() + 4f;
            }
          }
          else
          {
            var cube = Target.Entity as IMyCubeBlock;
            if (cube != null)
            {
              checkDistance = cube.PositionComp.LocalAABB.HalfExtents.AbsMax() + 4f;
            }
            else
            {
              checkDistance = 4f;
            }
          }

          checkDistance *= checkDistance;
        }
        else if (isFriendly)
        {
          checkDistance = _followDistanceSqd;
        }
        else if (Target.IsFloater)
        {
          checkDistance = 8;
        }
        else
        {
          checkDistance = 4;
        }

        var tgtPos = Target.IsSlimBlock ? _currentGraph.LocalToWorld(localTgt) : targetPosition;
        var dSquared = Vector3D.DistanceSquared(tgtPos, botPos);

        if (dSquared > checkDistance)
        {
          return false;
        }
      }

      if (Target.Entity != null)
      {
        List<IHitInfo> hitlist;
        if (!AiSession.Instance.HitListStack.TryPop(out hitlist) || hitlist == null)
          hitlist = new List<IHitInfo>();
        else
          hitlist.Clear();

        MyAPIGateway.Physics.CastRay(botPos, targetPosition, hitlist, CollisionLayers.CharacterCollisionLayer);
        bool result = true;

        for (int i = 0; i < hitlist.Count; i++)
        {
          var hit = hitlist[i];
          var hitEnt = hit?.HitEntity;

          if (hitEnt == null || hitEnt.EntityId == Character.EntityId)
            continue;

          if (hitEnt == Target.Entity)
          {
            if (_botState.IsFlying && Owner?.Character != null && Target.Entity == Owner.Character)
            {
              var vector = Vector3D.Rotate(targetPosition - botPos, MatrixD.Transpose(WorldMatrix));
              result = vector.Y < 1;
              break;
            }

            result = true;
            break;
          }

          var subpart = hitEnt as MyEntitySubpart;
          var door = subpart?.Parent as IMyDoor;
          if (door != null)
          {
            var doorPos = door.WorldAABB.Center;
            if (door is IMyAirtightHangarDoor)
              doorPos += door.WorldMatrix.Down * door.CubeGrid.GridSize;
            else if (door.BlockDefinition.SubtypeName == "LargeBlockGate")
              doorPos += door.WorldMatrix.Down * door.CubeGrid.GridSize * 0.5;

            if (Target.Entity is IMyDoor && Vector3D.IsZero(doorPos - targetPosition))
            {
              if (ToolDefinition != null && ToolDefinition.WeaponType != MyItemWeaponType.None)
              {
                var vecToDoor = doorPos - botPos;
                Vector3D newPos;
                if (vecToDoor.Dot(door.WorldMatrix.Forward) > 0)
                {
                  newPos = botPos + door.WorldMatrix.Backward * 5;
                }
                else
                {
                  newPos = botPos + door.WorldMatrix.Forward * 5;
                }

                _sideNode = newPos;
              }
              else
                _sideNode = null;

              result = true;
              break;
            }
          }

          var hitGrid = hitEnt as IMyCubeGrid;
          if (hitGrid != null && (Target.IsCubeBlock || Target.IsSlimBlock))
          {
            var localPos = hitGrid.WorldToGridInteger(hit.Position);
            var cube = hitGrid.GetCubeBlock(localPos);
            if (cube == null)
            {
              var fixedPos = hit.Position - hit.Normal * hitGrid.GridSize * 0.2f;
              localPos = hitGrid.WorldToGridInteger(fixedPos);
              cube = hitGrid.GetCubeBlock(localPos);
            }

            var targetCube = Target.IsCubeBlock ? (Target.Entity as IMyCubeBlock)?.SlimBlock : Target.Entity as IMySlimBlock;
            var checkCube = cube != null && targetCube != null;

            if (checkCube)
            {
              if (cube == targetCube)
              {
                result = true;
              }
              else if (cube.CubeGrid?.GridSizeEnum == MyCubeSize.Small)
              {
                var worldCube = cube.CubeGrid.GridIntegerToWorld(cube.Position);
                var worldTgtCube = targetCube.CubeGrid.GridIntegerToWorld(targetCube.Position);

                result = Vector3D.DistanceSquared(worldCube, worldTgtCube) < 10;
              }
              else if ((cube.Position - targetCube.Position).RectangularLength() < 2)
              {
                // Just in case we are positioned too close to the block 
                // and the ray clips through the corner of a neighboring block

                var worldCube = cube.CubeGrid.GridIntegerToWorld(cube.Position);
                result = Vector3D.DistanceSquared(hit.Position, worldCube) < 13;
              }
              else
                result = false;
            }
            else
              result = false;

            break;
          }
          
          if (Target.IsFloater)
          {
            var floater = Target.Entity as MyFloatingObject;
            var floaterPosition = floater?.PositionComp.WorldAABB.Center ?? Vector3D.PositiveInfinity;
            var distanceToFloater = Vector3D.DistanceSquared(floaterPosition, hit.Position);
            result = distanceToFloater < 1.5;
            break;
          }

          var voxelBase = hitEnt as MyVoxelBase;
          if (voxelBase != null && (Target.IsCubeBlock || Target.IsSlimBlock || Target.IsInventory))
          {
            var slim = Target.IsCubeBlock ? (Target.Entity as IMyCubeBlock).SlimBlock : Target.Entity as IMySlimBlock;
            if (slim != null)
            {
              Node node;
              if (!_currentGraph.TryGetNodeForPosition(slim.Position, out node) || node?.IsGridNodeUnderGround == true)
              {
                BoundingBoxD box;
                slim.GetWorldBoundingBox(out box, true);
                var checkDistance = (float)box.HalfExtents.AbsMax() + 1.5f;

                if (Vector3D.DistanceSquared(hit.Position, box.Center) < checkDistance * checkDistance)
                {
                  result = true;
                  break;
                }
              }
            }
          }
          
          result = HasLineOfSight;
          break;
        }

        hitlist.Clear();
        AiSession.Instance.HitListStack.Push(hitlist);

        return result;
      }

      return true;
    }

    internal void CheckDeniedDoors()
    {
      if (_pathCollection == null)
        return;

      List<Vector3I> tempNodes;
      if (!AiSession.Instance.LineListStack.TryPop(out tempNodes) || tempNodes == null)
        tempNodes = new List<Vector3I>();
      else
        tempNodes.Clear();

      foreach (var kvp in _pathCollection.DeniedDoors)
      {
        var door = kvp.Value;
        if (door == null || door.Closed || door.MarkedForClose || door.SlimBlock.IsDestroyed)
        {
          tempNodes.Add(kvp.Key);
          continue;
        }

        if (door.SlimBlock.IsBlockUnbuilt())
          tempNodes.Add(kvp.Key);
      }

      foreach (var node in tempNodes)
        _pathCollection.DeniedDoors.Remove(node);

      tempNodes.Clear();
      AiSession.Instance.LineListStack.Push(tempNodes);
    }

    internal void FindPathForIdleMovement(Vector3D moveTo)
    {
      if (_pathCollection.Locked)
        return;

      _stuckTimer = 0;
      _stuckCounter = 0;
      BotMoved = false;

      Vector3I start = _currentGraph.WorldToLocal(GetPosition());
      bool startDenied = _pathCollection.DeniedDoors.ContainsKey(start);
      if (!_currentGraph.GetClosestValidNode(this, start, out start, currentIsDenied: startDenied))
      {
        //var pn = _currentGraph.GetReturnHomePoint(this);
        //if (pn != null)
        //{
        //  _moveTo = _currentGraph.LocalToWorld(pn.Position) + pn.Offset;
        //}

        _currentGraph.TeleportNearby(this);
        _idlePathTimer = 0;
        return;
      }

      Vector3I goal = _currentGraph.WorldToLocal(moveTo);
      bool goalDenied = _pathCollection.DeniedDoors.ContainsKey(goal);
      if (!_currentGraph.GetClosestValidNode(this, goal, out goal, currentIsDenied: goalDenied) || start == goal)
      {
        _moveTo = null;
        return;
      }

      _lastEnd = moveTo;
      _pathCollection.CleanUp(true);

      _pathWorkData.PathStart = start;
      _pathWorkData.PathEnd = goal;
      _pathWorkData.IsIntendedGoal = Owner != null || !Target.HasTarget;
      _pathCollection.PathTimer.Restart();

      _task = MyAPIGateway.Parallel.StartBackground(_pathWorkAction, _pathWorkCallBack, _pathWorkData);
    }

    internal void CheckLineOfSight()
    {
      if (AwaitingCallBack || Character == null || Character.MarkedForClose || Character.IsDead)
        return;

      if (!HasWeaponOrTool)
      {
        HasLineOfSight = false;
        return;
      }

      var targetEnt = Target?.Entity as IMyEntity;
      if (targetEnt == null)
      {
        HasLineOfSight = false;
        return;
      }

      var botPosition = GetPosition();
      var botMatrix = WorldMatrix;

      var pos = botPosition + botMatrix.Up * 0.4; // close to the muzzle height
      var tgt = targetEnt.WorldAABB.Center;

      var tgtChar = targetEnt as IMyCharacter;
      if (tgtChar != null)
      {
        tgt = tgtChar.GetHeadMatrix(true).Translation;
      }
      else if (!(targetEnt is IMyLargeTurretBase))
      {
        var hangar = targetEnt as IMyAirtightHangarDoor;
        if (hangar != null)
          tgt += hangar.WorldMatrix.Down * hangar.CubeGrid.GridSize;

        var angle = VectorUtils.GetAngleBetween(botMatrix.Forward, tgt - pos);
        if (Math.Abs(angle) > VectorUtils.PiOver3)
        {
          if (hangar != null)
          {
            // Just in case the bot happens to be standing in front of the tip of the hangar
            tgt += hangar.WorldMatrix.Down * hangar.CubeGrid.GridSize;
            angle = VectorUtils.GetAngleBetween(botMatrix.Forward, tgt - pos);
            if (Math.Abs(angle) > VectorUtils.PiOver3)
            {
              HasLineOfSight = false;
              return;
            }
          }
          else
          {
            HasLineOfSight = false;
            return;
          }
        }
      }

      AwaitingCallBack = true;
      _hitList.Clear();

      MyAPIGateway.Physics.CastRayParallel(ref pos, ref tgt, _hitList, CollisionLayers.CharacterCollisionLayer, RayBlockedCallback);
    }

    void RayBlockedCallback(List<IHitInfo> hitList)
    {
      AwaitingCallBack = false;

      if (Character?.IsDead != false || Character.MarkedForClose)
      {
        HasLineOfSight = false;
        return;
      }

      var targetTopMost = Target?.Entity as IMyEntity;
      var character = targetTopMost as IMyCharacter; ;
      if (targetTopMost == null || character?.IsDead == true)
      {
        HasLineOfSight = false;
        return;
      }

      List<BotBase> helpers = null;
      bool friendly = false;
      if (Owner != null)
      {
        AiSession.Instance.PlayerToHelperDict.TryGetValue(Owner.IdentityId, out helpers);
        friendly = Target.IsFriendly();
      }

      var cube = Target.Entity as MyCubeBlock;
      var door = targetTopMost as IMyDoor;

      int helperCount = helpers?.Count ?? 0;
      HasLineOfSight = true;

      foreach (var hitInfo in hitList)
      {
        var hitEnt = hitInfo?.HitEntity;
        if (hitEnt == null || hitEnt.MarkedForClose)
          continue;

        if (hitEnt.EntityId == targetTopMost?.EntityId)
          break;

        if (hitEnt.EntityId == Character.EntityId)
          continue;

        if (door != null)
        {
          var subpart = hitEnt as MyEntitySubpart;
          if (subpart != null)
          {
            var tgtDoor = subpart.Parent as IMyDoor;
            if (door.EntityId == tgtDoor?.EntityId)
              break;
          }
        }

        var vMap = hitEnt as MyVoxelMap;
        if (vMap != null && vMap != vMap.RootVoxel)
          continue;

        if (friendly && helperCount > 0)
        {
          bool isHelper = false;

          foreach (var helper in helpers)
          {
            var bot = helper?.Character;
            if (bot == null)
              continue;

            if (hitEnt.EntityId == bot.EntityId)
            {
              isHelper = true;
              break;
            }
          }

          if (isHelper)
            continue;
        }

        var grid = hitEnt as MyCubeGrid;
        if (cube != null && grid != null)
        {
          if (grid != cube.CubeGrid && grid.BlocksCount < 5)
            continue;

          var allowedDistance = cube.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 2.5 : 10;
          if (Vector3D.DistanceSquared(hitInfo.Position, cube.PositionComp.WorldAABB.Center) <= allowedDistance * allowedDistance)
            break;
        }

        HasLineOfSight = false;
        break;
      }

      if (!HasLineOfSight)
        WaitForLOSTimer = true;
    }

    // debug only
    bool tempTaskInProcess;
    public void StartCheckGraph(ref Vector3D tgtPosition, bool force = false)
    {
      if (!_graphTask.IsComplete || tempTaskInProcess)
        return;

      tempTaskInProcess = true;

      CheckGraphNeeded = true;
      _graphWorkData.Force = force;
      _graphWorkData.TargetPosition = tgtPosition;
      _graphTask = MyAPIGateway.Parallel.Start(_graphWorkAction, _graphWorkCallBack, _graphWorkData);

      // testing only
      //CheckGraph(_graphWorkData);
      //CheckGraphComplete(_graphWorkData);
    }

    void CheckGraphComplete(WorkData workData)
    {
      CheckGraphNeeded = false;
      tempTaskInProcess = false;
    }

    void CheckGraph(WorkData workData)
    {
      var data = workData as GraphWorkData;
      var targetPosition = data.TargetPosition;
      bool force = data.Force;

      MyCubeGrid newGrid;
      Vector3D newGraphPosition, intermediatePosition;
      bool botInNewBox;

      if (NeedsTransition)
      {
        bool nextGraphOK = _nextGraph != null;
        bool targetInNext = nextGraphOK && _nextGraph.IsPositionValid(targetPosition);
        bool targetInCurrent = _currentGraph.IsPositionValid(targetPosition);
        bool transitionOk = _transitionPoint != null && _currentGraph.IsPositionUsable(this, _transitionPoint.Position);
        if (!nextGraphOK || !transitionOk || (!targetInNext && targetInCurrent))
        {
          if (nextGraphOK && !targetInNext)
            _nextGraph = null;

          _transitionPoint = null;
          NeedsTransition = false;
        }
        else if (nextGraphOK)
        {
          bool switchNow = true;
          var botPosition = GetPosition();

          var localBot = _currentGraph.WorldToLocal(botPosition);
          var currentNode = _currentGraph.GetValueOrDefault(localBot, null);
          var currentIsTunnel = currentNode?.IsTunnelNode ?? false;
          var targetIsTunnel = _transitionPoint?.IsTunnelNode ?? false;

          if (!_nextGraph.IsPositionValid(botPosition) || (targetIsTunnel && !currentIsTunnel))
          {
            if (_transitionPoint != null)
            {
              var transitionPosition = _currentGraph.LocalToWorld(_transitionPoint.Position) + _transitionPoint.Offset;
              var distance = Vector3D.DistanceSquared(botPosition, transitionPosition);
              if (distance > 5)
              {
                switchNow = false;
                CheckGraphNeeded = true;
              }
            }
            else
            {
              switchNow = false;
            }
          }
          else if (_currentGraph.IsGridGraph)
          {
            var gridGraph = _currentGraph as CubeGridMap;
            if (!gridGraph.IsInBufferZone(botPosition))
            {
              switchNow = false;
            }
          }

          if (switchNow)
          {
            _previousGraph = _currentGraph;
            _currentGraph = _nextGraph;
            _nextGraph = null;
            _transitionPoint = null;
            _transitionIdle = false;
            NeedsTransition = false;
            _pathTimer = 101;

            if (_pathCollection != null)
            {
              _pathCollection.Graph = _currentGraph;
              _pathCollection.CleanUp(true);
            }
          }
        }
      }
      else if (!CheckGraphValidity(targetPosition, ref force, out newGrid, out newGraphPosition, out intermediatePosition, out botInNewBox))
      {
        NeedsTransition = !force;
        var botPosition = GetPosition();
        var botMatrix = WorldMatrix;

        if (newGrid != null || !_currentGraph.IsPositionValid(newGraphPosition))
        {         
          _nextGraph = AiSession.Instance.GetNewGraph(newGrid, newGraphPosition, botMatrix);
        }

        bool switchNow = true;

        if (!force)
        {
          var center = _currentGraph.OBB.Center;

          if (_currentGraph.IsGridGraph)
          {
            // check to see if we should switch now or find a point in the buffer zone to move to
            var gridGraph = _currentGraph as CubeGridMap;

            if (!botInNewBox && (!gridGraph.IsInBufferZone(botPosition) || _nextGraph?.IsPositionValid(botPosition) == false))
            {
              switchNow = false;
              Node tgtPoint = null;

              if (_nextGraph != null)
              {
                var direction = _currentGraph.WorldMatrix.GetClosestDirection(_nextGraph.OBB.Center - _currentGraph.OBB.Center);
                tgtPoint = gridGraph.GetBufferZoneTargetPositionFromPrunik(ref _nextGraph.OBB, ref direction, ref targetPosition, this);

                if (tgtPoint == null)
                {
                  //var m = MatrixD.Transpose(Character.WorldMatrix);
                  //var vector = _nextGraph.OBB.Center - _currentGraph.OBB.Center;
                  //Vector3D.Rotate(ref vector, ref m, out vector);

                  switchNow = true;
                  var midPoint = (_currentGraph.OBB.Center + _nextGraph.OBB.Center) * 0.5;
                  _nextGraph = AiSession.Instance.GetVoxelGraph(midPoint, WorldMatrix);
                }
              }
              else
              {
                var bufferZonePosition = gridGraph.GetBufferZoneTargetPosition(targetPosition, center);

                if (bufferZonePosition.HasValue && !_currentGraph.IsPositionUsable(this, bufferZonePosition.Value))
                {
                  bufferZonePosition = gridGraph.GetClosestSurfacePointFast(this, bufferZonePosition.Value, botMatrix.Up);
                }

                var localNode = _currentGraph.WorldToLocal(bufferZonePosition.Value);
                if (_currentGraph.GetClosestValidNode(this, localNode, out localNode))
                {
                  //tgtPoint = _currentGraph.OpenTileDict[localNode];
                  _currentGraph.TryGetNodeForPosition(localNode, out tgtPoint);
                }
              }

              if (tgtPoint == null || !NeedsTransition)
              {
                _nextGraph = null;
                _pathCollection?.CleanUp(true);
                NeedsTransition = false;
              }
              else
              {
                _transitionPoint = tgtPoint;
              }
            }
          }
          else if (_nextGraph != null)
          {
            var voxelGraphNext = _nextGraph as VoxelGridMap;
            var voxelGraphPrev = _previousGraph as VoxelGridMap;
            bool nextIsPrev = voxelGraphNext != null && voxelGraphNext.Key == voxelGraphPrev?.Key;

            if (!botInNewBox)
            {
              var direction = _currentGraph.WorldMatrix.GetClosestDirection(_nextGraph.OBB.Center - _currentGraph.OBB.Center);
              var tgtPoint = _currentGraph.GetBufferZoneTargetPositionFromPrunik(ref _nextGraph.OBB, ref direction, ref targetPosition, this);

              if (tgtPoint == null || nextIsPrev)
              {
                switchNow = true;
                var midPoint = (_currentGraph.OBB.Center + _nextGraph.OBB.Center) * 0.5;
                _nextGraph = AiSession.Instance.GetVoxelGraph(midPoint, WorldMatrix, returnFirstFound: !nextIsPrev);
              }
              else
              {
                _transitionPoint = tgtPoint;
                switchNow = false;
              }
            }
            else if (nextIsPrev)
            {
              switchNow = true;
              var midPoint = (_currentGraph.OBB.Center + _nextGraph.OBB.Center) * 0.5;
              _nextGraph = AiSession.Instance.GetVoxelGraph(midPoint, WorldMatrix, returnFirstFound: false);
            }
          }
          else if (NeedsTransition)
          {
            var intermediateNode = _currentGraph.WorldToLocal(intermediatePosition);
            if (_currentGraph.GetClosestValidNode(this, intermediateNode, out intermediateNode))
            {
              //_transitionPoint = _currentGraph.OpenTileDict[intermediateNode];
              _currentGraph.TryGetNodeForPosition(intermediateNode, out _transitionPoint);
            }
          }
        }

        if (switchNow && _nextGraph != null)
        {
          _previousGraph = _currentGraph;
          _currentGraph = _nextGraph;
          _nextGraph = null;
          _transitionPoint = null;
          NeedsTransition = false;
          _pathTimer = 101;

          if (_pathCollection != null)
          {
            _pathCollection.Graph = _currentGraph;
            _pathCollection.CleanUp(true);
          }
        }
      }
    }

    internal bool TrySwitchJetpack(bool enable)
    {
      var jetComp = Character?.Components?.Get<MyCharacterJetpackComponent>();
      if (jetComp != null && MyAPIGateway.Session.SessionSettings.EnableJetpack)
      {
        enable |= RequiresJetpack;

        if (jetComp.TurnedOn)
        {
          if (!enable)
            jetComp.TurnOnJetpack(false);
        }
        else if (enable)
          jetComp.TurnOnJetpack(true);

        return true;
      }

      CanUseAirNodes = false;
      return false;
    }

    internal void UsePathfinder(Vector3D gotoPosition, Vector3D actualPosition)
    {
      try
      {
        if (_currentGraph == null || !_currentGraph.Ready)
        {
          AiSession.Instance.AnalyzeHash.Add(Character.EntityId);

          CubeGridMap gridGraph;
          if ((_currentGraph != null && !_currentGraph.IsValid)
            || ((gridGraph = _currentGraph as CubeGridMap) != null && (gridGraph.MainGrid == null || gridGraph.MainGrid.MarkedForClose)))
          {
            CheckGraphNeeded = false;
            NeedsTransition = false;

            _previousGraph = null;
            _currentGraph = AiSession.Instance.GetVoxelGraph(GetPosition(), WorldMatrix, true);

            if (_pathCollection != null)
            {
              _pathCollection.Graph = _currentGraph;
              _pathCollection.CleanUp(true);
            }
          }

          return;
        }

        if (_pathCollection == null)
        {
          _pathCollection = AiSession.Instance.GetCollection();
          _pathCollection.Bot = this;
          _pathCollection.Graph = _currentGraph;
        }

        #region debugOnly
        if (AiSession.Instance.DrawDebug) // && Owner != null)
          _pathCollection.DrawFreeSpace(GetPosition(), gotoPosition);
        #endregion

        if (CheckGraphNeeded)
        {
          StartCheckGraph(ref gotoPosition);
          return;
        }

        if (_currentGraph.Dirty)
        {
          _pathCollection.CleanUp(true);
          return;
        }

        if (_task.Exceptions != null)
        {
          AiSession.Instance.Logger.ClearCached();
          AiSession.Instance.Logger.AddLine($"Exceptions found during pathfinder task!\n");
          foreach (var ex in _task.Exceptions)
            AiSession.Instance.Logger.AddLine($" -> {ex.Message}\n{ex.StackTrace}\n");

          AiSession.Instance.Logger.LogAll();
          MyAPIGateway.Utilities.ShowNotification($"Exception during task!");
        }

        bool returnNow;
        if (CheckIfCloseEnoughToAct(ref actualPosition, out returnNow))
        {
          _pathCollection.CleanUp(true);

          if (Target.Override.HasValue)
          {
            if (actualPosition == Target.Override)
              Target.RemoveOverride(true);
          }

          if (!returnNow)
          {
            double checkDistance;
            if (Target.IsCubeBlock)
            {
              checkDistance = 5;
            }
            else
            {
              var ch = Target.Entity as IMyCharacter;
              if (ch != null && ch.Definition.Id.SubtypeName.StartsWith("space_spider", StringComparison.OrdinalIgnoreCase))
                checkDistance = 3.5;
              else
                checkDistance = 2;
            }

            MoveToPoint(gotoPosition, Target.Entity != null, checkDistance);
          }

          return;
        }

        var distanceToCheck = (_botState.IsRunning) ? 1f : 0.5f;
        var targetLocal = _currentGraph.WorldToLocal(gotoPosition);
        bool targetMoved = (_lastEndLocal != Vector3I.Zero && _lastEndLocal != targetLocal)
          || (!Vector3D.IsZero(_lastEnd) && Vector3D.DistanceSquared(gotoPosition, _lastEnd) > 4);

        if (_pathCollection.Dirty && !_pathCollection.Locked)
        {
          _pathCollection.CleanUp(true);
        }

        if (_pathCollection.HasNode)
        {
          CheckNode(ref distanceToCheck);
        }

        if (UseLadder || _botState.IsOnLadder || _botState.WasOnLadder)
        {
          _stuckTimer = 0;

          if (UseLadder)
          {
            bool forceUse = false;
            if (_pathCollection.HasNode && (NextIsLadder || (AfterNextIsLadder && _pathCollection.PathToTarget.Count > 0)))
            {
              var node = NextIsLadder ? _pathCollection.NextNode.Position : _pathCollection.PathToTarget.Peek().Position;
              var worldNode = _currentGraph.LocalToWorld(node);
              var rotated = Vector3D.Rotate(worldNode - GetPosition(), MatrixD.Transpose(WorldMatrix));
              forceUse = rotated.Y < -1;
            }

            bool wait;
            if (FaceLadderAndUse(forceUse, out wait))
            {
              AfterNextIsLadder = false;
              UseLadder = false;
              _pathCollection.ClearNode();
            }
            else if (wait || forceUse)
            {
              _stuckCounter = 0;
              Character.MoveAndRotate(Vector3.Zero, Vector2.Zero, 0);
              return;
            }
          }
          else if (_botState.WasOnLadder && _botState.IsFalling)
          {
            NextIsLadder = false;
            AfterNextIsLadder = false;
            _stuckCounter = 0;
            _stuckTimerReset = 0;
            WaitForStuckTimer = false;
            _pathCollection.CleanUp(true);
          }
        }

        if (!_pathCollection.HasNode && !UseLadder && _botState.IsOnLadder)
          PathFinderActive = false;

        if (BotMoved || targetMoved || (!_pathCollection.HasNode && !_pathCollection.HasPath))
        {
          if (ShouldFind(ref gotoPosition))
          {
            if (_task.IsComplete)
              FindNewPath(gotoPosition);
            else
              _pathTimer = 1000;
          }
          else if (!BotMoved)
          {
            if (_pathCollection.HasNode)
            {
              UpdatePathAndMove(ref distanceToCheck);
            }
            else if (_pathCollection.HasPath && !UseLadder)
            {
              GetNextNodeAndMove(ref distanceToCheck);
            }
            else if (!_pathCollection.HasPath && !_pathCollection.Locked && !Target.IsSlimBlock
              && (!NeedsTransition || _transitionPoint == null))
            {
              SimulateIdleMovement(false, Owner?.Character?.IsDead == false);
            }
          }
        }
        else if (_pathCollection.HasNode)
        {
          UpdatePathAndMove(ref distanceToCheck);
        }
        else if (_pathCollection.HasPath && !UseLadder)
        {
          GetNextNodeAndMove(ref distanceToCheck);
        }
      }
      catch (Exception ex)
      {
        if (MyAPIGateway.Session?.Player != null)
          MyAPIGateway.Utilities.ShowNotification($"Exception in UsePathFinder: {ex.Message}", 10000);
  
        AiSession.Instance.Logger.LogAll($"Exception in AiEnabled.BotBase.UsePathFinder: {ex.Message}\n{ex.StackTrace}\n", MessageType.ERROR);
      }
    }

    void CheckNode(ref float distanceToCheck)
    {
      if (_stuckCounter > 2 || _pathCollection.UpdateDistanceToNextNode())
      {
        _stuckTimer = 0;
        _stuckCounter = 0;

        _sideNode = null;
        BotMoved = true;
        NextIsLadder = false;
        AfterNextIsLadder = false;
        UseLadder = false;

        if (_stuckCounter > 2)
        {
          if (!_currentGraph.IsGridGraph)
          {
            var voxelGraph = _currentGraph as VoxelGridMap;
            var lastNode = _pathCollection.LastNode;
            var nextNode = _pathCollection.NextNode;

            var currentNode = _currentGraph.WorldToLocal(GetPosition());
            //var current = _currentGraph.LocalToWorld(currentNode);
            var last = lastNode != null ? lastNode.Position : currentNode; // _currentGraph.LocalToWorld(lastNode.Position) + lastNode.Offset : current;
            var next = nextNode != null ? nextNode.Position : currentNode; // _currentGraph.LocalToWorld(nextNode.Position) + nextNode.Offset : current;

            voxelGraph.AddToObstacles(last, currentNode, next);
            _pathCollection.CleanUp(true);
          }
          else
            _pathCollection.ClearNode(true);
        }
        else
          _pathCollection.CleanUp(true);
      }
      else if (AfterNextIsLadder)
      {
        // Only true if you're about to climb down from a cliff edge
        UseLadder = true;
      }
      else
      {
        var pNode = _pathCollection.NextNode;
        var worldNode = _currentGraph.LocalToWorld(pNode.Position) + pNode.Offset;

        var vector = worldNode - GetPosition();
        var relVector = Vector3D.TransformNormal(vector, MatrixD.Transpose(WorldMatrix));
        var flattenedVector = new Vector3D(relVector.X, 0, relVector.Z);
        var flattenedLengthSquared = flattenedVector.LengthSquared();
        var check = 1.1 * distanceToCheck;

        if (flattenedLengthSquared < check && Math.Abs(relVector.Y) < check)
        {
          if (NextIsLadder)
          {
            UseLadder = true;
          }
          else
          {
            _pathCollection.ClearNode();
          }
        }
      }
    }

    internal void ResetSideNode()
    {
      _sideNode = null;
    }

    void FindNewPath(Vector3D targetPosition)
    {
      if (_pathCollection.Locked || _sideNode.HasValue)
      {
        return;
      }

      _stuckTimer = 0;
      _stuckCounter = 0;
      BotMoved = false;
      var botPosition = GetPosition();

      Vector3I start = _currentGraph.WorldToLocal(botPosition);
      if (!_currentGraph.GetClosestValidNode(this, start, out start))
      {
        _pathCollection.CleanUp(true);
        _currentGraph.TeleportNearby(this);

        //var home = _currentGraph.GetReturnHomePoint(this);
        //if (home != null)
        //{
        //  lock (_pathCollection.PathToTarget)
        //    _pathCollection.PathToTarget.Enqueue(home);
        //}

        return;
      }

      var isSlimBlock = Target.IsSlimBlock;

      Vector3D exit;
      Vector3I goal;
      var tgtCharacter = Target.Entity as IMyCharacter;
      var isCharacter = tgtCharacter != null;
      if (NeedsTransition && _transitionPoint != null)
      {
        isCharacter = false;
        goal = _transitionPoint.Position;
        exit = _currentGraph.LocalToWorld(goal) + _transitionPoint.Offset;
      }
      else
      {
        exit = targetPosition;
        goal = _currentGraph.WorldToLocal(exit);
      }

      Node goalNode;
      bool found = false;
      if (isCharacter && tgtCharacter.EntityId == Owner?.Character?.EntityId && _currentGraph.TryGetNodeForPosition(goal, out goalNode) && goalNode.IsAirNode)
      {
        Vector3I vec3I;
        if (_currentGraph.GetClosestValidNode(this, goalNode.Position, out vec3I, WorldMatrix.Up, currentIsDenied: true))
        {
          goal = vec3I;
          found = true;
        }
      }

      if (!found && !_currentGraph.GetClosestValidNode(this, goal, out goal, isSlimBlock: isSlimBlock))
      {
        bool goBack = true;
        if (isCharacter)
        {
          var newGoal = _currentGraph.WorldToLocal(targetPosition);
          goBack = !_currentGraph.GetClosestValidNode(this, newGoal, out goal);
        }

        if (goBack)
        {
          if (Target.Override.HasValue && exit == Target.Override)
            Target.RemoveOverride(false);

          if (isSlimBlock || Target.IsInventory)
          {
            _currentGraph.TempBlockedNodes[_currentGraph.WorldToLocal(exit)] = new byte();
            Target.RemoveTarget();
          }

          return;
        }
      }

      if (start == goal)
      {
        if (Target.IsInventory || isSlimBlock)
        {
          _pathCollection.CleanUp(true);

          var block = isSlimBlock ? Target.Entity as IMySlimBlock : Target.Inventory;
          var grid = block.CubeGrid as MyCubeGrid;
          var worldPostion = grid.GridIntegerToWorld(block.Position);
          var position = _currentGraph.WorldToLocal(worldPostion);

          lock (_pathCollection.PathToTarget)
          {
            TempNode temp;
            if (!AiSession.Instance.NodeStack.TryPop(out temp))
              temp = new TempNode();

            temp.Update(position, Vector3.Zero, NodeType.None, 0, grid, block);
            _pathCollection.PathToTarget.Enqueue(temp);
          }
        }

        return;
      }

      _lastEnd = targetPosition;
      _lastEndLocal = _currentGraph.WorldToLocal(targetPosition);
      _pathCollection.CleanUp();

      _pathWorkData.PathStart = start;
      _pathWorkData.PathEnd = goal;
      _pathWorkData.IsIntendedGoal = true;
      _pathCollection.PathTimer.Restart();
      _task = MyAPIGateway.Parallel.StartBackground(_pathWorkAction, _pathWorkCallBack, _pathWorkData);

      // testing only
      //_pathCollection.PathTimer.Stop();
      //FindPath(_pathWorkData);
      //Reset();
    }

    public void FindPath(WorkData workData)
    {
      var pathData = workData as PathWorkData;
      PathFinding.FindPath(pathData.PathStart, pathData.PathEnd, _pathCollection, pathData.IsIntendedGoal);
    }

    void FindPathCallBack(WorkData workData)
    {
      Reset();
    }

    bool ShouldFind(ref Vector3D targetPosition)
    {
      if (!Target.HasTarget)
        return false;

      int checkTime;
      if (Target.IsNewTarget)
      {
        Target.IsNewTarget = false;
        checkTime = -1;
      }
      else
      {
        var distanceToTgtSqd = Vector3D.DistanceSquared(GetPosition(), targetPosition);
        checkTime = distanceToTgtSqd < 10 ? 1 : distanceToTgtSqd < 100 ? 10 : 100;
      }

      if (_pathTimer > checkTime)
      {
        _pathTimer = 0;
        return true;
      }

      return false;
    }

    bool FaceLadderAndUse(bool force, out bool needToWait)
    {
      needToWait = false;

      if (!CanUseLadders || !_pathCollection.HasNode || !_currentGraph.IsGridGraph)
      {
        UseLadder = NextIsLadder = false;
        return false;
      }

      var botMatrix = WorldMatrix;

      if (UseObject != null)
      {
        var ladder = UseObject.Owner as MyCubeBlock;
        if (ladder != null)
        {
          bool inVoxel, charBlocked;
          if (_pathCollection.IsLadderBlocked(ladder, out inVoxel, out charBlocked, force))
          {
            if (!force && (inVoxel || !charBlocked))
            {
              var up = Base6Directions.GetIntVector(ladder.CubeGrid.WorldMatrix.GetClosestDirection(botMatrix.Up));
              var gridWorld = ladder.CubeGrid.GridIntegerToWorld(ladder.Position + up);
              var graphLocal = _currentGraph.WorldToLocal(gridWorld);
              var blockAbove = _currentGraph.GetBlockAtPosition(graphLocal);
              if (blockAbove == null)
                blockAbove = ladder.CubeGrid.GetCubeBlock(ladder.Position + up) as IMySlimBlock;
              
              if (blockAbove != null && AiSession.Instance.LadderBlockDefinitions.Contains(blockAbove.BlockDefinition.Id))
              {
                ladder = blockAbove.FatBlock as MyCubeBlock;
                var nextNode = _pathCollection.PathToTarget.Peek();
                if (blockAbove.Position == nextNode.Position && !_pathCollection.IsLadderBlocked(ladder, out inVoxel, out charBlocked))
                {
                  UseObject = _pathCollection.GetBlockUseObject(ladder);
                  if (UseObject != null)
                  {
                    try
                    {
                      UseObject.Use(UseActionEnum.Manipulate, Character);
                      return true;
                    }
                    catch (NullReferenceException)
                    {
                      // sometimes the game still throws an error trying to display the "ladder is blocked" notification for bots
                    }
                  }
                }
              }
            }

            if (_botState.IsFalling)
            {
              _pathCollection.CleanUp(true);
              return true;
            }

            needToWait = true;
            return false;
          }
        }

        if (!_botState.IsOnLadder && (force || _botState.IsFalling))
        {
          try
          {
            UseObject.Use(UseActionEnum.Manipulate, Character);
          }
          catch (NullReferenceException)
          {
            // sometimes the game still throws an error trying to display the "ladder is blocked" notification
          }
          return true;
        }
      }

      var gridGraph = _currentGraph as CubeGridMap;
      var localPathTo = _pathCollection.NextNode.Position;
      var slim = gridGraph.GetBlockAtPosition(localPathTo); // gridGraph.MainGrid.GetCubeBlock(localPathTo) as IMySlimBlock;

      if (slim == null || slim.BlockDefinition.Id.TypeId != typeof(MyObjectBuilder_Ladder2))
        return false;

      var cubeForward = slim.CubeGrid.WorldMatrix.GetDirectionVector(slim.Orientation.Forward);
      var adjustedLadderPosition = slim.CubeGrid.GridIntegerToWorld(slim.Position) + cubeForward * slim.CubeGrid.GridSize * -0.5;

      var botMatrixT = MatrixD.Transpose(botMatrix);
      var vectorToLadder = adjustedLadderPosition - GetPosition();
      var rotatedVector = Vector3D.Rotate(vectorToLadder, botMatrixT);

      if (force || _botState.IsFalling || (rotatedVector.Z < 0 && Math.Abs(rotatedVector.X) < 0.5))
      {
        if (UseObject != null && !_botState.IsOnLadder)
        {
          try
          {
            UseObject.Use(UseActionEnum.Manipulate, Character);
          }
          catch (NullReferenceException)
          {
            // sometimes the game still throws an error trying to display the "ladder is blocked" notification
          }
        }

        return true;
      }

      var rotation = new Vector2(0, Math.Sign(rotatedVector.X) * 75);
      Character.MoveAndRotate(Vector3.Zero, rotation, 0);
      return false;
    }

    void UpdatePathAndMove(ref float distanceToCheck)
    {
      PathFinderActive = true;
      var botPosition = GetPosition();
      var pNode = _pathCollection.NextNode;
      var worldNode = _currentGraph.LocalToWorld(pNode.Position) + pNode.Offset;

      if (!NextIsLadder && _botState.IsOnLadder && _botState.GoingDownLadder)
      {
        Character.Use();
        UseLadder = false;
        AfterNextIsLadder = false;
        _ticksSinceLastDismount = 0;

        if (RequiresJetpack && !JetpackEnabled)
        {
          var jetpack = Character.Components?.Get<MyCharacterJetpackComponent>();
          if (jetpack != null && !jetpack.TurnedOn)
          {
            var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
            MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
            jetpack.TurnOnJetpack(true);
            MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
          }
        }
        else if (_pathCollection.HasNode)
        {
          var vector = worldNode - botPosition;
          var direction = WorldMatrix.GetClosestDirection(vector);

          if (direction != Base6Directions.Direction.Down)
          {
            Character.SetPosition(worldNode);
          }
        }
      }

      var current = _currentGraph.WorldToLocal(botPosition);
      if (current != _lastCurrent && current != _lastPrevious)
      {
        _lastPrevious = _lastCurrent;
        _lastCurrent = current;
        _stuckTimer = 0;
      }

      MoveToPoint(worldNode, false, NextIsLadder ? distanceToCheck * 0.5 : distanceToCheck);
    }

    void GetNextNodeAndMove(ref float distanceToCheck)
    {
      var botPosition = GetPosition();
      bool useLadderNow, findNewPath, wait, nextIsAirNode, nextIsLadder, afterNextIsLadder;
      _pathCollection.GetNextNode(botPosition, _botState.IsOnLadder, _transitionPoint != null,
        out nextIsLadder, out afterNextIsLadder, out UseObject, out useLadderNow, out findNewPath, out nextIsAirNode);

      NextIsLadder = nextIsLadder;
      AfterNextIsLadder = afterNextIsLadder;

      if (!findNewPath)
      {
        if (nextIsAirNode)
        {
          if (!CanUseAirNodes || !TrySwitchJetpack(true))
          {
            findNewPath = true;
          }
        }
        else if (JetpackEnabled && !RequiresJetpack && !LastWasAirNode)
        {
          Node curNode;
          var current = _currentGraph.WorldToLocal(botPosition);
          if (!_currentGraph.TryGetNodeForPosition(current, out curNode) || !curNode.IsAirNode)
            TrySwitchJetpack(false);
        }
      }

      LastWasAirNode = nextIsAirNode;

      if (findNewPath)
      {
        _pathCollection.ClearNode(true);
      }
      else if (useLadderNow && FaceLadderAndUse(true, out wait))
      {
        NextIsLadder = false;
        AfterNextIsLadder = false;
        UseLadder = false;
        _pathCollection.ClearNode();
      }

      _stuckCounter = 0;

      if (!_pathCollection.Dirty && _pathCollection.HasNode)
        UpdatePathAndMove(ref distanceToCheck);
    }

    internal bool CheckPathForObstacles(ref int stuckTimer, out bool swerve, out bool doorFound)
    {
      swerve = false;
      doorFound = false;

      try
      {
        if (Character == null || Character.MarkedForClose || Character.IsDead)
          return false;

        var checkDoors = stuckTimer > 60;
        var botPosition = GetPosition();
        var botMatrix = WorldMatrix;

        List<IHitInfo> hitlist;
        if (!AiSession.Instance.HitListStack.TryPop(out hitlist) || hitlist == null)
          hitlist = new List<IHitInfo>();
        else
          hitlist.Clear();

        MyAPIGateway.Physics.CastRay(botPosition, botPosition + botMatrix.Forward * 3, hitlist, CollisionLayers.CharacterCollisionLayer);

        //bool result = false;
        bool foundTarget = false;
        IMyDoor door = null;

        for (int i = 0; i < hitlist.Count; i++)
        {
          var hitInfo = hitlist[i];
          var hitEnt = hitInfo?.HitEntity;

          if (hitEnt == null || hitEnt.EntityId == Character.EntityId)
            continue;

          if (hitEnt == Target?.Entity)
          {
            foundTarget = true;
            break;
          }

          if (hitEnt is IMyCharacter || hitEnt is MyEnvironmentSector)
          {
            //result = true;
            swerve = true;
            break;
          }

          if (!checkDoors)
            break;

          door = hitEnt as IMyDoor;
          if (door != null)
          {
            //result = true;
            break;
          }

          var subpart = hitEnt as MyEntitySubpart;
          if (subpart != null)
          {
            door = subpart.Parent as IMyDoor;
            //result = true;
            break;
          }

          var grid = hitEnt as IMyCubeGrid;
          if (grid != null)
          {
            var worldPos = hitInfo.Position - hitInfo.Normal * grid.GridSize * 0.2f;
            var blockPos = grid.WorldToGridInteger(worldPos);
            door = grid.GetCubeBlock(blockPos)?.FatBlock as IMyDoor;

            if (door == null)
            {
              worldPos = hitInfo.Position + hitInfo.Normal * grid.GridSize * 0.2f;
              blockPos = grid.WorldToGridInteger(worldPos);
              door = grid.GetCubeBlock(blockPos)?.FatBlock as IMyDoor;
            }

            //result = door != null;
            break;
          }
        }

        hitlist.Clear();
        AiSession.Instance.HitListStack.Push(hitlist);

        if (swerve)
          return true;

        var gridGraph = _currentGraph as CubeGridMap;

        if (foundTarget || !checkDoors || gridGraph == null)
          return false;

        IMySlimBlock block;

        if (door == null)
        {
          var localPos = gridGraph.WorldToLocal(botPosition);
          block = gridGraph.GetBlockAtPosition(localPos);
          door = block?.FatBlock as IMyDoor;

          if (door == null)
            return false;
        }
        else
        {
          block = door.SlimBlock;
        }

        if (!door.IsFunctional)
        {
          if (door.SlimBlock.IsBlockUnbuilt())
          {
            return true;
          }
        }

        bool isRotaryAirlock = door.BlockDefinition.SubtypeName.StartsWith("RotaryAirlock");

        if (!isRotaryAirlock && door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open)
        {
          if (door.IsWorking)
            AiSession.Instance.DoorsToClose[door.EntityId] = MyAPIGateway.Session.ElapsedPlayTime;

          return true;
        }

        bool hasAccess;
        var doorOwner = door.CubeGrid.BigOwners.Count > 0 ? door.CubeGrid.BigOwners[0] : door.CubeGrid.SmallOwners.Count > 0 ? door.CubeGrid.SmallOwners[0] : door.OwnerId;

        if (Owner != null)
        {
          var botOwner = Owner.IdentityId;
          var relation = MyIDModule.GetRelationPlayerPlayer(botOwner, doorOwner, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);

          hasAccess = ((MyDoorBase)door).AnyoneCanUse || relation != MyRelationsBetweenPlayers.Enemies;
        }
        else
        {
          var botOwner = Character.ControllerInfo.ControllingIdentityId;
          var relation = MyIDModule.GetRelationPlayerPlayer(botOwner, doorOwner, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);

          hasAccess = relation != MyRelationsBetweenPlayers.Enemies;
        }

        if (!hasAccess)
        {
          if (Owner != null)
          {
            _pathCollection.DeniedDoors[door.Position] = door;
          }
          else
          {
            // assume enemy, attack door!
            if (Target.Entity == null || !ReferenceEquals(Target.Entity, door))
            {
              _doorTgtCounter = 0;
              _pathCollection.CleanUp(true);

              Target.SetTarget(door);
            }
          }
        }
        else if (!door.Enabled || !door.IsFunctional)
        {
          var pos = door.Position;
          if (door.CubeGrid.EntityId != gridGraph.MainGrid.EntityId)
            pos = gridGraph.MainGrid.WorldToGridInteger(door.CubeGrid.GridIntegerToWorld(pos));

          if (!gridGraph.BlockedDoors.ContainsKey(pos))
          {
            gridGraph.BlockedDoors[pos] = door;
            var cubeDef = block.BlockDefinition as MyCubeBlockDefinition;
            var faceDict = AiSession.Instance.BlockFaceDictionary[cubeDef.Id];

            Matrix matrix = new Matrix
            {
              Forward = Base6Directions.GetVector(block.Orientation.Forward),
              Left = Base6Directions.GetVector(block.Orientation.Left),
              Up = Base6Directions.GetVector(block.Orientation.Up)
            };

            matrix.TransposeRotationInPlace();

            Vector3I center = cubeDef.Center;
            Vector3I.TransformNormal(ref center, ref matrix, out center);
            var adjustedPosition = block.Position - center;

            foreach (var kvp in faceDict)
            {
              var cell = kvp.Key;
              Vector3I.TransformNormal(ref cell, ref matrix, out cell);
              var position = adjustedPosition + cell;
              var mainGridPosition = gridGraph.MainGrid.WorldToGridInteger(block.CubeGrid.GridIntegerToWorld(position));

              gridGraph.BlockedDoors[mainGridPosition] = door;
            }
          }

          var pointBehind = botPosition + botMatrix.Backward * gridGraph.CellSize;
          var newPosition = gridGraph.GetLastValidNodeOnLine(pointBehind, botMatrix.Backward, 10);
          var localPoint = gridGraph.WorldToLocal(newPosition);
          _stuckTimer = 0;

          lock (_pathCollection.PathToTarget)
          {
            _pathCollection.CleanUp(true);

            var node = gridGraph.OpenTileDict[localPoint];
            _pathCollection.PathToTarget.Enqueue(node);
          }

          return false;
        }
        else if (door.IsWorking)
        {
          if (isRotaryAirlock)
          {
            var doorPos = door.Position;
            var doorWorldPos = door.CubeGrid.GridIntegerToWorld(doorPos);

            if (door.CubeGrid.EntityId != gridGraph.MainGrid.EntityId)
              doorPos = gridGraph.MainGrid.WorldToGridInteger(doorWorldPos);

            var localBot = gridGraph.MainGrid.WorldToGridInteger(botPosition);

            if (localBot != doorPos)
            {

              List<MyEntity> entList;
              if (!AiSession.Instance.EntListStack.TryPop(out entList) || entList == null)
                entList = new List<MyEntity>();
              else
                entList.Clear();

              var sphere = new BoundingSphereD(doorWorldPos, 1);
              MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entList, MyEntityQueryType.Dynamic);

              bool occupied = false;
              for (int i = 0; i < entList.Count; i++)
              {
                var ch = entList[i] as IMyCharacter;
                if (ch != null && ch.EntityId != Character.EntityId)
                {
                  var localChar = gridGraph.MainGrid.WorldToGridInteger(ch.WorldAABB.Center);
                  if (localChar == doorPos)
                  {
                    doorFound = true;
                    occupied = true;
                    break;
                  }
                }
              }

              entList.Clear();
              AiSession.Instance.EntListStack.Push(entList);

              if (occupied)
                return true;
            }

            bool botInAirlock = localBot == doorPos;
            var matchDotFwd = door.WorldMatrix.Forward.Dot(botMatrix.Forward) > 0;

            if (door.BlockDefinition.SubtypeName.EndsWith("Corner"))
            {
              var matchDotLeft = door.WorldMatrix.Left.Dot(botMatrix.Forward) > 0;

              if (botInAirlock)
              {
                if (matchDotLeft)
                {
                  if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open)
                    return false;

                  if (door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Opening)
                    door.OpenDoor();
                }
                else if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed)
                {
                  return false;
                }
                else if (door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Closing)
                {
                  door.CloseDoor();
                }
              }
              else if (matchDotFwd)
              {
                if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed)
                  return false;

                if (door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Closing)
                  door.CloseDoor();
              }
              else if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open)
              {
                return false;
              }
              else if (door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Opening)
              {
                door.OpenDoor();
              }

              doorFound = true;
              return true;
            }
            else
            {
              if (botInAirlock)
              {
                if (matchDotFwd)
                {
                  if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed)
                    return false;

                  if (door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Closing)
                    door.CloseDoor();
                }
                else if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open)
                {
                  return false;
                }
                else if (door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Opening)
                {
                  door.OpenDoor();
                }
              }
              else if (matchDotFwd)
              {
                if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open)
                  return false;

                if (door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Opening)
                  door.OpenDoor();
              }
              else if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed)
              {
                return false;
              }
              else if (door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Closing)
              {
                door.CloseDoor();
              }

              doorFound = true;
              return true;
            }
          }
          else if (door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Open)
          {
            if (door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Opening)
            {
              door.OpenDoor();
              AiSession.Instance.DoorsToClose[door.EntityId] = MyAPIGateway.Session.ElapsedPlayTime;
            }

            doorFound = true;
            return true;
          }
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Error checking for obstacles: {ex.Message}\n{ex.StackTrace}\n", MessageType.ERROR);
      }

      return false;
    }

    internal void AdjustMovementForFlight(ref Vector3D relVectorBot, ref Vector3 movement, ref Vector3D botPosition, bool towardBlock = false)
    {
      float multiplier;
      var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(botPosition, out multiplier);
      var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(botPosition, multiplier);
      var gravity = nGrav.LengthSquared() > 0 ? nGrav : aGrav;

      multiplier = 0;
      if (gravity.LengthSquared() > 0)
        multiplier = MathHelper.Clamp(gravity.Length() / 9.81f, 0, 2);

      if (Math.Sign(relVectorBot.Y) > 0)
      {
        movement.Z *= 0.5f;

        if (relVectorBot.Y > 5)
        {
          movement += Vector3.Up * 2;
        }
        else if (relVectorBot.Y > 2)
        {
          movement += Vector3.Up;
        }
        else
        {
          movement += Vector3.Up * 0.5f;
        }

        if (multiplier > 1)
          movement.Y *= multiplier;
      }
      else
      {
        var amount = MathHelper.Lerp(0.5f, 0.25f, Math.Min(multiplier, 1));
        movement += Vector3.Down * amount;
      }
    }

    internal void MoveToPoint(Vector3 movement, Vector2 rotation, float roll = 0)
    {
      if (_currentGraph?.Ready == true && PathFinderActive && !UseLadder && !_botState.IsOnLadder && !_botState.WasOnLadder)
      {
        bool swerve, doorFound;

        if (!_currentGraph.IsGridGraph)
        {
          if (_stuckTimer > 120)
          {
            if (_pathCollection.HasNode && !_currentGraph.IsGridGraph)
            {
              //var currentPosition = GetPosition();
              var currentNode = _currentGraph.WorldToLocal(GetPosition());

              var nextPosition = _pathCollection.NextNode.Position; // _currentGraph.LocalToWorld(_pathCollection.NextNode.Position) + _pathCollection.NextNode.Offset;
              var fromPosition = _pathCollection.LastNode != null ? _pathCollection.LastNode.Position : currentNode; 
              // _currentGraph.LocalToWorld(_pathCollection.LastNode.Position) + _pathCollection.LastNode.Offset : currentPosition;

              var vGrid = _currentGraph as VoxelGridMap;
              vGrid?.AddToObstacles(fromPosition, currentNode, nextPosition);
            }

            _stuckTimer = 0;
            _stuckCounter++;
            _pathCollection.CleanUp(true);
            return;
          }
        }
        else if (_stuckTimer > 180)
        {
          _stuckCounter++;
          WaitForStuckTimer = true;
        }
        else if (CheckPathForObstacles(ref _stuckTimer, out swerve, out doorFound))
        {
          if (doorFound)
          {
            movement = Vector3.Zero;
            _stuckTimer = 60;
          }
          else if (swerve)
          {
            WaitForSwerveTimer = true;
          }
        }
      }

      if (WaitForStuckTimer)
      {
        _stuckTimer = 0;

        if (++_stuckTimerReset > 60)
        {
          _stuckTimerReset = 0;
          WaitForStuckTimer = false;
        }

        if (JetpackEnabled)
        {
          if (_stuckTimerReset <= 30)
          {
            movement *= 0.5f;

            if (_stuckCounter == 1)
              movement += Vector3.Up * 0.5f;
            else if (_stuckTimer == 2)
              movement += Vector3.Down * 0.5f;
          }
          else
            movement = Vector3.Forward * 0.5f;
        }
        else
        {
          rotation *= -1;

          if (_stuckTimerReset > 40)
          {
            Character.Jump();
          }
        }
      }
      else if (WaitForSwerveTimer)
      {
        _stuckTimer = 0;

        if (++_stuckTimerReset > 10)
        {
          _stuckTimerReset = 0;
          WaitForSwerveTimer = false;
        }

        movement += new Vector3(0.75f, 0, 0.5f);
      }

      Character.MoveAndRotate(movement, rotation, roll);
    }

    internal void SimulateIdleMovement(bool getMoving, bool towardOwner = false)
    {
      if (PatrolMode || FollowMode || !AiSession.Instance.ModSaveData.AllowIdleMovement)
        return;

      _sideNode = null;

      var botPosition = GetPosition();
      var botMatrix = WorldMatrix;

      if (towardOwner)
      {
        _moveTo = null;
        _idleTimer = 0;
        _ticksSinceLastIdleTransition = 0;
      }
      else if (_moveTo.HasValue)
      {
        var vector = Vector3D.TransformNormal(_moveTo.Value - botPosition, Matrix.Transpose(botMatrix));
        var flattenedVector = new Vector3D(vector.X, 0, vector.Z);

        if (flattenedVector.LengthSquared() <= 3)
          _moveTo = null;
        else
        {
          var distFromPrev = Vector3D.DistanceSquared(_prevMoveTo.Value, botPosition);
          if (distFromPrev > 4)
          {
            _idleTimer = 0;
            _prevMoveTo = botPosition;
          }
          else
          {
            ++_idleTimer;
            if (_idleTimer >= 120)
            {
              _moveTo = null;
              _pathCollection?.CleanUp(true);
            }
          }
        }
      }

      var graphReady = _currentGraph?.Ready == true;
      if (graphReady && _pathCollection == null)
      {
        _pathCollection = AiSession.Instance.GetCollection();
        _pathCollection.Bot = this;
        _pathCollection.Graph = _currentGraph;
      }

      //if (AiSession.Instance.DrawDebug && _pathCollection != null)
      //{
      //  var start = Position;
      //  var end = _moveTo ?? start;
      //  _pathCollection.DrawFreeSpace(start, end);
      //}

      if (_moveTo == null)
      {
        if (towardOwner)
        {
          var pos = Owner.Character.WorldAABB.Center;
          if (Vector3D.DistanceSquared(botPosition, pos) <= 25)
          {
            _pathCollection?.CleanUp(true);

            if (Target.PositionsValid)
            {
              var actual = Vector3D.Normalize(Target.CurrentActualPosition - botPosition);
              MoveToPoint(botPosition + actual);
            }

            return;
          }

          if (graphReady)
          {
            var localOwner = _currentGraph.WorldToLocal(pos);
            if (_currentGraph.GetClosestValidNode(this, localOwner, out localOwner, botMatrix.Up))
              _moveTo = _currentGraph.LocalToWorld(localOwner);
          }
          else
            _moveTo = pos;
        }

        if (_moveTo == null)
        {
          if (_idlePathTimer > 300)
          {
            _idlePathTimer = 0;
            var direction = GetTravelDirection();

            if (graphReady)
            {
              if (getMoving)
              {
                if (_transitionIdle)
                {
                  Vector3D pos;
                  if (_transitionPoint == null)
                  {
                    pos = _currentGraph.OBB.Center + direction * (int)_currentGraph.OBB.HalfExtent.AbsMax() * 1.5;
                    _moveTo = null;
                  }
                  else
                  {
                    pos = _currentGraph.LocalToWorld(_transitionPoint.Position) + _transitionPoint.Offset;
                    _moveTo = pos;
                  }

                  StartCheckGraph(ref pos);
                }
                else if (Owner == null && !_transitionIdle && AiSession.Instance.ModSaveData.AllowIdleMapTransitions)
                {
                  _ticksSinceLastIdleTransition++;
                  if (_ticksSinceLastIdleTransition > 20)
                  {
                    var random = MyUtils.GetRandomInt(0, 101);
                    if (random >= 50)
                    {
                      _ticksSinceLastIdleTransition = 0;
                      _transitionIdle = true;
                    }
                  }
                }
              }

              if (!_transitionIdle)
              {
                Node moveNode;
                var pos = botPosition + direction * MyUtils.GetRandomInt(10, (int)_currentGraph.OBB.HalfExtent.AbsMax());

                if (_currentGraph.GetRandomOpenNode(this, pos, out moveNode))
                {
                  _moveTo = _currentGraph.LocalToWorld(moveNode.Position) + moveNode.Offset;
                }
              }
            }
            else
              _moveTo = botPosition + direction * MyUtils.GetRandomInt(10, 26);
          }
          else
            _stuckCounter = _stuckTimer = 0;
        }

        _prevMoveTo = botPosition;
        _idleTimer = 0;

        if (_moveTo == null)
          return;
      }

      if (graphReady)
      {
        if (AiSession.Instance.DrawDebug)
          _pathCollection?.DrawFreeSpace(botPosition, _moveTo.Value);

        if (getMoving)
        {
          var distanceToCheck = (_botState.IsRunning || _botState.IsFlying) ? 1 : 0.5f;
          if (_pathCollection.HasNode)
            CheckNode(ref distanceToCheck);

          if (_pathCollection.HasNode)
          {
            UpdatePathAndMove(ref distanceToCheck);
          }
          else if (_pathCollection.HasPath && !UseLadder)
          {
            GetNextNodeAndMove(ref distanceToCheck);
          }
          else
            FindPathForIdleMovement(_moveTo.Value);
        }
        else
          FindPathForIdleMovement(_moveTo.Value);
      }
      else
        MoveToPoint(_moveTo.Value);
    }

    internal Vector3D GetTravelDirection()
    {
      var random = MyUtils.GetRandomInt(1, 9);
      var botMatrix = WorldMatrix;

      switch (random)
      {
        case 1:
          return botMatrix.Forward;
        case 2:
          return botMatrix.Forward + botMatrix.Right;
        case 3:
          return botMatrix.Right;
        case 4:
          return botMatrix.Right + botMatrix.Backward;
        case 5:
          return botMatrix.Backward;
        case 6:
          return botMatrix.Backward + botMatrix.Left;
        case 7:
          return botMatrix.Left;
        case 8:
          return botMatrix.Left + botMatrix.Forward;
        default:
          return botMatrix.Forward;
      }
    }

    public T CastHax<T>(T typeRef, object castObj) => (T)castObj;
  }
}
