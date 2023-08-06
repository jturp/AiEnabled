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
using AiEnabled.Networking.Packets;
using AiEnabled.Particles;
using VRage.Game.Models;

namespace AiEnabled.Bots
{
  public abstract partial class BotBase
  {
    [Flags]
    enum BotInfoEnum : ulong
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
      AllowGridDamage = 0x800000000,
      IsLargeSpider = 0x1000000000,
      IsSmallSpider = 0x2000000000,
      IsWolf = 0x4000000000,
      ConfineToMap = 0x8000000000,
      HelmetEnabled = 0x10000000000,
      CanTransitionMaps = 0x20000000000,
      AllowIdleMovement = 0x40000000000,
      AllowEquipWeapon = 0x80000000000,
      GrenadeThrown = 0x100000000000,
      IsDead = 0x200000000000,
    }

    public IMyPlayer Owner;
    public IMyCharacter Character;
    public TargetInfo Target;
    public float DamageModifier;
    public int TicksBetweenProjectiles = 10;
    public long BotIdentityId;
    public MyHandItemDefinition ToolDefinition;
    public AiSession.BotType BotType;
    public AiSession.ControlInfo BotControlInfo;
    public RemoteBotAPI.RepairPriorities RepairPriorities;
    public RemoteBotAPI.TargetPriorities TargetPriorities;

    public enum BuildMode { None, Weld, Grind }
    public BuildMode CurrentBuildMode = BuildMode.Weld;

    public bool IsDead
    {
      get { return (_botInfo & BotInfoEnum.IsDead) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.IsDead;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.IsDead;
        }
      }
    }

    public bool GrenadeThrown
    {
      get { return (_botInfo & BotInfoEnum.GrenadeThrown) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.GrenadeThrown;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.GrenadeThrown;
        }
      }
    }

    public bool AllowEquipWeapon
    {
      get { return (_botInfo & BotInfoEnum.AllowEquipWeapon) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.AllowEquipWeapon;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.AllowEquipWeapon;
        }
      }
    }

    public bool AllowIdleMovement
    {
      get { return (_botInfo & BotInfoEnum.AllowIdleMovement) > 0; }
      set
      {
        if (value)
        {
          if (AiSession.Instance.ModSaveData.AllowIdleMovement)
            _botInfo |= BotInfoEnum.AllowIdleMovement;
          else
            _botInfo &= ~BotInfoEnum.AllowIdleMovement;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.AllowIdleMovement;
        }
      }
    }

    public bool CanTransitionMaps
    {
      get { return (_botInfo & BotInfoEnum.CanTransitionMaps) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.CanTransitionMaps;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.CanTransitionMaps;
        }
      }
    }

    public bool HelmetEnabled
    {
      get { return (_botInfo & BotInfoEnum.HelmetEnabled) > 0; }
      set
      {
        if (value)
        {
          var charDef = Character?.Definition as MyCharacterDefinition;
          if (AiSession.Instance?.ModSaveData != null && AiSession.Instance.ModSaveData.AllowHelmetVisorChanges && charDef?.AnimationNameToSubtypeName != null
            && charDef.AnimationNameToSubtypeName.TryGetValue("HelmetOpen", out _animation_helmetOpen) && charDef.AnimationNameToSubtypeName.TryGetValue("HelmetClose", out _animation_helmetClose))
          {
            _botInfo |= BotInfoEnum.HelmetEnabled;
          }
          else
          {
            _botInfo &= ~BotInfoEnum.HelmetEnabled;
          }
        }
        else
        {
          _botInfo &= ~BotInfoEnum.HelmetEnabled;
        }
      }
    }

    public bool ConfineToMap
    {
      get { return (_botInfo & BotInfoEnum.ConfineToMap) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.ConfineToMap;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.ConfineToMap;
        }
      }
    }

    public bool IsWolf
    {
      get { return (_botInfo & BotInfoEnum.IsWolf) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.IsWolf;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.IsWolf;
        }
      }
    }

    public bool IsSmallSpider
    {
      get { return (_botInfo & BotInfoEnum.IsSmallSpider) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.IsSmallSpider;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.IsSmallSpider;
        }
      }
    }

    public bool IsLargeSpider
    {
      get { return (_botInfo & BotInfoEnum.IsLargeSpider) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.IsLargeSpider;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.IsLargeSpider;
        }
      }
    }

    public bool CanDamageGrid
    {
      get { return (_botInfo & BotInfoEnum.AllowGridDamage) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.AllowGridDamage;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.AllowGridDamage;
        }
      }
    }

    public bool HasWeaponOrTool
    {
      get { return (_botInfo & BotInfoEnum.HasTool) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.HasTool;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.HasTool;
        }
      }
    }

    public bool HasLineOfSight 
    {
      get { return (_botInfo & BotInfoEnum.HasLOS) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.HasLOS;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.HasLOS;
        }
      }
    }

    public bool UseAPITargets 
    {
      get { return (_botInfo & BotInfoEnum.UseAPITargets) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.UseAPITargets;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.UseAPITargets;
        }
      }
    }


    internal bool SwitchWalk
    {
      get { return (_botInfo & BotInfoEnum.SwitchWalk) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.SwitchWalk;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.SwitchWalk;
        }
      }
    }

    internal bool DamagePending
    {
      get { return (_botInfo & BotInfoEnum.DamagePending) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.DamagePending;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.DamagePending;
        }
      }
    }

    internal bool BehaviorReady
    {
      get { return (_botInfo & BotInfoEnum.BehaviorReady) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.BehaviorReady;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.BehaviorReady;
        }
      }
    }

    internal bool PathFinderActive
    {
      get { return (_botInfo & BotInfoEnum.PFActive) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.PFActive;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.PFActive;
        }
      }
    }

    internal bool BotMoved
    {
      get { return (_botInfo & BotInfoEnum.BotMoved) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.BotMoved;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.BotMoved;
        }
      }
    }

    internal bool UsePathFinder
    {
      get { return (_botInfo & BotInfoEnum.UsePathfinder) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.UsePathfinder;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.UsePathfinder;
        }
      }
    }

    internal bool NextIsLadder
    {
      get { return (_botInfo & BotInfoEnum.NextIsLadder) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.NextIsLadder;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.NextIsLadder;
        }
      }
    }

    internal bool AfterNextIsLadder
    {
      get { return (_botInfo & BotInfoEnum.AfterNextIsLadder) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.AfterNextIsLadder;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.AfterNextIsLadder;
        }
      }
    }

    internal bool UseLadder
    {
      get { return (_botInfo & BotInfoEnum.UseLadder) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.UseLadder;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.UseLadder;
        }
      }
    }

    internal bool NeedsTransition
    {
      get { return (_botInfo & BotInfoEnum.NeedsTransition) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.NeedsTransition;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.NeedsTransition;
        }
      }
    }

    internal bool IsShooting
    {
      get { return (_botInfo & BotInfoEnum.IsShooting) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.IsShooting;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.IsShooting;
        }
      }
    }

    internal bool WaitForLOSTimer
    {
      get { return (_botInfo & BotInfoEnum.LOSTimer) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.LOSTimer;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.LOSTimer;
        }
      }
    }

    internal bool WaitForStuckTimer
    {
      get { return (_botInfo & BotInfoEnum.StuckTimer) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.StuckTimer;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.StuckTimer;
        }
      }
    }

    internal bool WaitForSwerveTimer
    {
      get { return (_botInfo & BotInfoEnum.SwerveTimer) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.SwerveTimer;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.SwerveTimer;
        }
      }
    }

    internal bool CheckGraphNeeded
    {
      get { return (_botInfo & BotInfoEnum.CheckGraph) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.CheckGraph;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.CheckGraph;
        }
      }
    }

    internal bool CanUseSpaceNodes
    {
      get { return (_botInfo & BotInfoEnum.SpaceNode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.SpaceNode;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.SpaceNode;
        }
      }
    }

    internal bool CanUseAirNodes
    {
      get { return (_botInfo & BotInfoEnum.AirNode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.AirNode;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.AirNode;
        }
      }
    }

    internal bool CanUseWaterNodes
    {
      get { return (_botInfo & BotInfoEnum.WaterNode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.WaterNode;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.WaterNode;
        }
      }
    }

    internal bool CanUseLadders
    {
      get { return (_botInfo & BotInfoEnum.LadderNode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.LadderNode;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.LadderNode;
        }
      }
    }

    internal bool CanUseSeats
    {
      get { return (_botInfo & BotInfoEnum.SeatNode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.SeatNode;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.SeatNode;
        }
      }
    }

    internal bool GroundNodesFirst
    {
      get { return (_botInfo & BotInfoEnum.GroundFirst) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.GroundFirst;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.GroundFirst;
        }
      }
    }

    internal bool WaterNodesOnly
    {
      get { return (_botInfo & BotInfoEnum.WaterNodeOnly) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.WaterNodeOnly;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.WaterNodeOnly;
        }
      }
    }

    internal bool RequiresJetpack
    {
      get { return (_botInfo & BotInfoEnum.JetPackReq) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.JetPackReq;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.JetPackReq;
        }
      }
    }

    internal bool JetpackEnabled
    {
      get { return (_botInfo & BotInfoEnum.JetPackEnabled) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.JetPackEnabled;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.JetPackEnabled;
        }
      }
    }

    internal bool EnableDespawnTimer
    {
      get { return (_botInfo & BotInfoEnum.Despawn) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.Despawn;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.Despawn;
        }
      }
    }

    internal bool WantsTarget
    {
      get { return (_botInfo & BotInfoEnum.WantsTarget) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.WantsTarget;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.WantsTarget;
        }
      }
    }

    internal bool AwaitingCallBack
    {
      get { return (_botInfo & BotInfoEnum.CallBack) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.CallBack;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.CallBack;
        }
      }
    }

    internal bool LastWasAirNode
    {
      get { return (_botInfo & BotInfoEnum.LastIsAirNode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.LastIsAirNode;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.LastIsAirNode;
        }
      }
    }

    internal bool ShouldLeadTargets
    {
      get { return (_botInfo & BotInfoEnum.LeadTargets) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.LeadTargets;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.LeadTargets;
        }
      }
    }


    internal bool BugZapped
    {
      get { return (_botInfo & BotInfoEnum.BuggZapped) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.BuggZapped;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.BuggZapped;
        }
      }
    }

    internal bool PatrolMode
    {
      get { return (_botInfo & BotInfoEnum.PatrolMode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.PatrolMode;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.PatrolMode;
        }
      }
    }

    internal bool FollowMode
    {
      get { return (_botInfo & BotInfoEnum.FollowMode) > 0; }
      set
      {
        if (value)
        {
          _botInfo |= BotInfoEnum.FollowMode;
        }
        else
        {
          _botInfo &= ~BotInfoEnum.FollowMode;
        }
      }
    }

    public BotState BotInfo;
    internal float _minDamage, _maxDamage, _followDistanceSqd = 10;
    internal float _blockDamagePerAttack, _blockDamagePerSecond = 100;
    internal float _shotAngleDeviationTan = 0;
    internal Vector3D? _prevMoveTo, _moveTo, _sideNode, _particleLocation;
    internal GridBase _currentGraph, _nextGraph, _previousGraph;
    internal PathCollection _pathCollection;
    internal Node _transitionPoint, _moveToNode;
    internal IMyUseObject UseObject;
    internal Task _task;
    internal Vector3D _lastEnd;
    internal Vector3I _lastCurrent, _lastPrevious, _lastEndLocal;
    internal short _patrolIndex = -1, _patrolWaitTime = 1;
    internal int? _wcWeaponAmmoCount, _wcWeaponMagsLeft;
    internal int _stuckCounter, _stuckTimer, _stuckTimerReset, _teleportCounter, _floaterCounter;
    internal int _tickCount, _xMoveTimer, _noPathCounter, _doorTgtCounter, _grenadeCounter, _grenadeChanceOffset = 25;
    internal uint _performanceTimer = 500, _performanceTimerTarget = 500;
    internal uint _pathTimer, _idleTimer, _idlePathTimer, _lowHealthTimer = 1800;
    internal uint _ticksSinceFoundTarget, _damageTicks, _despawnTicks = 25000;
    internal uint _ticksBeforeDamage = 35;
    internal uint _ticksBetweenAttacks = 300;
    internal uint _ticksSinceLastAttack = 1000;
    internal uint _ticksSinceLastDismount = 1000;
    internal float _twoDegToRads = MathHelper.ToRadians(2);
    internal List<Vector3I> _patrolList;
    internal List<MySoundPair> _attackSounds;
    internal List<string> _attackSoundStrings;
    internal MySoundPair _deathSound;
    internal string _deathSoundString;
    internal string _lootContainerSubtype;
    internal string _animation_helmetOpen;
    internal string _animation_helmetClose;
    internal MyObjectBuilder_ConsumableItem _energyOB;
    internal BotBehavior Behavior;
    internal BuildBotToolInfo _toolInfo = new BuildBotToolInfo();
    internal bool _wcShotFired, _wcWeaponReloading, _particlePacketSent;
    internal SortedDictionary<int, List<object>> _taskPriorities = new SortedDictionary<int, List<object>>();
    internal List<object> _taskPrioritiesTemp = new List<object>();
    internal ParallelTasks.Task _targetTask;
    internal Action _targetAction;
    internal string _patrolName;

    readonly List<IHitInfo> _hitList;
    Task _graphTask;
    BotInfoEnum _botInfo;
    byte _ticksSinceLastIdleTransition;
    bool _transitionIdle, _walkSwitched;
    MyFloatingObject _lastFloater;

    Action<WorkData> _graphWorkAction, _graphWorkCallBack;
    Action<WorkData> _pathWorkAction, _pathWorkCallBack;
    readonly GraphWorkData _graphWorkData;
    readonly PathWorkData _pathWorkData;
    readonly internal MyObjectBuilderType _animalBotType = MyObjectBuilderType.Parse("MyObjectBuilder_AnimalBot");

    public void SetShootInterval()
    {
      var gun = Character?.EquippedTool as IMyHandheldGunObject<MyGunBase>;
      if (gun != null)
      {
        var multiplier = 0.334f;

        var weaponItemDef = gun?.PhysicalItemDefinition as MyWeaponItemDefinition;
        if (weaponItemDef != null)
        {
          var weaponDef = MyDefinitionManager.Static.GetWeaponDefinition(weaponItemDef.WeaponDefinitionId);
          multiplier = weaponDef.DamageMultiplier;
        }

        TicksBetweenProjectiles = (int)Math.Ceiling(gun.GunBase.ShootIntervalInMiliseconds / 16.667f * 1.5f);
        DamageModifier = multiplier;
      }
      else if (ToolDefinition != null)
      {
        var physItemDef = MyDefinitionManager.Static.GetPhysicalItemDefinition(ToolDefinition.PhysicalItemId);
        var weaponItemDef = physItemDef as MyWeaponItemDefinition;

        if (weaponItemDef != null)
        {
          var weaponDef = MyDefinitionManager.Static.GetWeaponDefinition(weaponItemDef.WeaponDefinitionId);
          DamageModifier = weaponDef.DamageMultiplier;

          var ammoId = weaponDef.AmmoMagazinesId[0];
          var ammoMagDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(ammoId);
          var ammoDefinition = MyDefinitionManager.Static.GetAmmoDefinition(ammoMagDef.AmmoDefinitionId);
          var intervalMS = weaponDef.WeaponAmmoDatas[(int)ammoDefinition.AmmoType].ShootIntervalInMiliseconds;
          TicksBetweenProjectiles = (int)Math.Ceiling(intervalMS / 16.667f * 1.5f);
        }
        else
        {
          TicksBetweenProjectiles = 10;
          DamageModifier = 0.334f;
        }
      }
      else
      {
        TicksBetweenProjectiles = 10;
        DamageModifier = 1;
      }
    }

    public BotBase(IMyCharacter bot, float minDamage, float maxDamage, GridBase gridBase, AiSession.ControlInfo ctrlInfo)
    {
      Character = bot;
      BotIdentityId = bot.ControllerInfo.ControllingIdentityId;
      Target = new TargetInfo(this);
      UsePathFinder = gridBase != null;
      BotControlInfo = ctrlInfo;
      //RepairPriorities = new RemoteBotAPI.RepairPriorities();
      //TargetPriorities = new RemoteBotAPI.TargetPriorities();

      BotInfo = new BotState(this);
      _currentGraph = gridBase;
      _minDamage = minDamage;
      _maxDamage = maxDamage + 1;
      _energyOB = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_ConsumableItem>("PowerKit");
      _targetAction = new Action(SetTargetInternal);

      var subtype = bot.Definition.Id.SubtypeName;
      bool wolfBot = subtype.IndexOf("wolf", StringComparison.OrdinalIgnoreCase) >= 0 || subtype.IndexOf("hound", StringComparison.OrdinalIgnoreCase) >= 0;
      bool smallSpider = !wolfBot && subtype.IndexOf("smallspider", StringComparison.OrdinalIgnoreCase) >= 0;
      bool largeSpider = !wolfBot && !smallSpider && subtype.IndexOf("spider", StringComparison.OrdinalIgnoreCase) >= 0;
      IsWolf = wolfBot;
      IsSmallSpider = smallSpider;
      IsLargeSpider = largeSpider;
      HelmetEnabled = AiSession.Instance.ModSaveData.AllowHelmetVisorChanges;
      AllowIdleMovement = AiSession.Instance.ModSaveData.AllowIdleMovement;
      AllowEquipWeapon = true;

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

      if (!AiSession.Instance.LineListStack.TryPop(out _patrolList) || _patrolList == null)
        _patrolList = new List<Vector3I>();
      else
        _patrolList.Clear();
    }

    public void ChangeCharacter(IMyCharacter newCharacter)
    {
      var oldChar = Character;
      if (Character != null)
      {
        Character.OnClosing -= Character_OnClosing;
        Character.OnClose -= Character_OnClosing;
        Character.CharacterDied -= Character_CharacterDied;

        BotBase _;
        AiSession.Instance.Bots.TryRemove(Character.EntityId, out _);
      }

      if (newCharacter != null)
      {
        Character = newCharacter;
        newCharacter.OnClosing += Character_OnClosing;
        newCharacter.OnClose += Character_OnClosing;
        newCharacter.CharacterDied += Character_CharacterDied;

        AiSession.Instance.Bots[Character.EntityId] = this;

        if (ToolDefinition != null)
          AiSession.Instance.Scheduler.Schedule(AddWeapon);

        oldChar?.Close();
      }
      else
      {
        Close(true);
      }
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

          if (IsSmallSpider || IsLargeSpider)
            subtype = "SpaceSpider";
          else if (IsWolf)
            subtype = "Wolf";

          var botDef = new MyDefinitionId(_animalBotType, subtype);
          var agentDef = MyDefinitionManager.Static.GetBotDefinition(botDef) as MyAgentDefinition;
          var lootContainer = agentDef?.InventoryContainerTypeId.SubtypeName ?? "RobotLoot";
          container = MyDefinitionManager.Static.GetContainerTypeDefinition(lootContainer);
        }

        if (container != null)
        {
          inventory.GenerateContent(container);
        }
      }

      if (AiSession.Instance.ModSaveData.DisableCharacterCollisionOnBotDeath)
        AiSession.Instance.Scheduler.Schedule(() => InitDeadBodyPhysics(bot), 100);

      PlayDeathSound();
      CleanUp(true);
    }

    void InitDeadBodyPhysics(IMyCharacter bot)
    {
      try
      {
        if (bot != null && !bot.Closed)
        {
          Vector3 linearVel = Vector3.Zero;
          Vector3 angularVel = Vector3.Zero;
          Vector3 extents = bot.LocalAABB.HalfExtents * 0.9f;
          float mass = 500;
          MyStringHash material = MyStringHash.GetOrCompute("Character");

          if (bot.Components.Has<MyCharacterRagdollComponent>())
          {
            var ragDollComp = bot.Components.Get<MyCharacterRagdollComponent>();
            ragDollComp.NeedsUpdateBeforeSimulation = true;
            ragDollComp.NeedsUpdateBeforeSimulation100 = true;
            ragDollComp.NeedsUpdateSimulation = false;

            //bot.Components.Remove<MyCharacterRagdollComponent>();
          }

          if (bot.Physics != null)
          {
            linearVel = bot.Physics.LinearVelocity;
            angularVel = bot.Physics.AngularVelocity;
            mass = bot.Physics.Mass;
            material = bot.Physics.MaterialType;

            bot.Physics.Enabled = false;
            bot.Physics.Close();
            bot.Physics = null;
          }
          else if (_currentGraph?.IsGridGraph == true)
          {
            var gridGraph = _currentGraph as CubeGridMap;
            if (gridGraph?.MainGrid?.Physics?.IsStatic == false)
              linearVel = gridGraph.MainGrid.Physics.LinearVelocity;
          }

          var settings = new PhysicsSettings()
          {
            Entity = bot,
            CollisionLayer = 10,
            IsPhantom = false,
            AngularDamping = 2f,
            LinearDamping = 0.7f,
            RigidBodyFlags = RigidBodyFlag.RBF_DEFAULT,
            LocalCenter = bot.LocalAABB.Center,
            WorldMatrix = bot.WorldMatrix,
            MaterialType = material,
            Mass = MyAPIGateway.Physics.CreateMassForBox(extents, mass)
          };

          MyAPIGateway.Physics.CreateBoxPhysics(settings, bot.PositionComp.LocalAABB.HalfExtents * 0.9f, 0);
          if (bot.Physics != null)
          {
            bot.Physics.SetSpeeds(linearVel, angularVel);
            bot.Physics.Friction = 0.5f;

            float interference;
            var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(bot.WorldAABB.Center, out interference);
            if (nGrav.LengthSquared() > 0)
              bot.Physics.Gravity = nGrav;
            else
              bot.Physics.Gravity = MyAPIGateway.Physics.CalculateArtificialGravityAt(bot.WorldAABB.Center, interference);
          }
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in BotBase.InitDeadBodyPhysics: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    private void Character_OnClosing(IMyEntity bot)
    {
      CleanUp();
    }

    internal virtual void Close(bool cleanConfig = false, bool removeBot = true)
    {
      CleanUp(cleanConfig, removeBot);

      if (removeBot && Character != null)
      {
        var seat = Character.Parent as IMyCockpit;
        if (seat != null)
          seat.RemovePilot();

        Character.Close();
      }
    }

    internal virtual void CleanUp(bool cleanConfig = false, bool removeBot = true)
    {
      IsDead = true;

      if (Character != null)
      {
        if (ToolDefinition != null && AiSession.Instance.WcAPILoaded)
        {
          AiSession.Instance.WcAPI.ShootRequestHandler(Character.EntityId, true, WCShootCallback);
        }

        Character.OnClosing -= Character_OnClosing;
        Character.OnClose -= Character_OnClosing;
        Character.CharacterDied -= Character_CharacterDied;

        if (Owner != null)
        {
          MyVisualScriptLogicProvider.SetHighlight(Character.Name, false, -1, playerId: Owner.IdentityId);

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
                var packet2 = new ParticlePacket(Character.EntityId, Particles.ParticleInfoBase.ParticleType.Weld, remove: true);

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
      {
        _pathCollection.OnGridBaseClosing();
        _pathCollection.ClearObstacles(true);
        AiSession.Instance.ReturnCollection(_pathCollection);
      }

      if (_hitList != null)
      {
        _hitList.Clear();
        AiSession.Instance.HitListStack.Push(_hitList);
      }

      if (_patrolList != null)
      {
        _patrolList.Clear();
        AiSession.Instance.LineListStack.Push(_patrolList);
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
      RepairPriorities?.PriorityTypes?.Clear();
      TargetPriorities?.PriorityTypes?.Clear();
      _taskPriorities?.Clear();
      _taskPrioritiesTemp?.Clear();

      Target = null;
      Behavior = null;
      RepairPriorities = null;
      TargetPriorities = null;

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
      _moveToNode = null;
      _taskPriorities = null;
      _taskPrioritiesTemp = null;
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

    internal bool WCShootCallback(Vector3D scopePos, Vector3D scopeDirection, int requestState, bool hasLos, object target, int currentAmmo, int remainingMags, int requestStage)
    {
      var stage = (WcApi.EventTriggers)requestStage;
      var state = (WcApi.ShootState)requestState;

      if (!hasLos || stage == WcApi.EventTriggers.Reloading || (stage == WcApi.EventTriggers.StopFiring && state == WcApi.ShootState.EventStart) || currentAmmo == 0 || target == null)
        _wcShotFired = false;

      if (stage == WcApi.EventTriggers.Reloading)
      {
        _wcWeaponReloading = state == WcApi.ShootState.EventStart;
      }

      _wcWeaponAmmoCount = currentAmmo;
      _wcWeaponMagsLeft = remainingMags;

      return hasLos;
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

    internal void PlaySoundServer(string sound, Vector3D position, bool stop = false)
    {
      if (MyAPIGateway.Multiplayer.MultiplayerActive)
      {
        var packet = new SoundPacket(sound, position, stop);
        AiSession.Instance.Network.RelayToClients(packet);
      }

      if (MyAPIGateway.Session.Player != null)
      {
        AiSession.Instance.PlaySoundAtPosition(position, sound, stop);
      }
    }

    public Vector3D GetPosition()
    {
      if (Character != null)
      {
        var center = Character.WorldAABB.Center;
        var subtype = Character.Definition.Id.SubtypeId;

        if (subtype == AiSession.Instance.RoboDogSubtype)
          center += Character.WorldMatrix.Up * 0.75;
        else if (subtype == AiSession.Instance.PlushieSubtype || IsWolf)
          center += Character.WorldMatrix.Up * 0.5;
        else if (IsSmallSpider)
          center += Character.WorldMatrix.Up * 0.25;
        else
          center += Character.WorldMatrix.Up * 0.1;

        return center;
      }

      return Vector3D.Zero;
    }

    public MatrixD WorldMatrix => Character?.WorldMatrix ?? MatrixD.Identity;

    internal void GiveControl(IMyEntityController controller) => controller.TakeControl(Character);

    internal void AdjustPatrolPointsForNewGraph(GridBase oldMap, GridBase newMap)
    {
      if (_patrolList?.Count > 0 && oldMap != null && newMap != null)
      {
        for (int i = 0; i < _patrolList.Count; i++)
        {
          var world = oldMap.LocalToWorld(_patrolList[i]);
          _patrolList[i] = newMap.WorldToLocal(world);
        }
      }
    }

    internal void UpdatePatrolPoints(List<Vector3D> waypoints, string name)
    {
      _patrolName = null;
      _patrolList.Clear();
      _patrolIndex = -1;
      _patrolWaitTime = 1;

      if (waypoints?.Count > 0)
      {
        _patrolName = name;

        for (int i = 0; i < waypoints.Count; i++)
        {
          var waypoint = waypoints[i] + WorldMatrix.Down;
          var checkPoint = waypoint;

          if (_currentGraph != null)
          {
            Node gridNode;
            var gridPoint = _currentGraph.WorldToLocal(waypoint);
            if (_currentGraph.TryGetNodeForPosition(gridPoint, out gridNode))
              checkPoint = _currentGraph.LocalToWorld(gridNode.Position) + gridNode.Offset;

            if (GridBase.PointInsideVoxel(checkPoint, _currentGraph.RootVoxel))
              waypoint += WorldMatrix.Up * _currentGraph.CellSize;
          }

          var wp = _currentGraph.WorldToLocal(waypoint);
          _patrolList.Add(wp);
        }
      }

      if (_patrolList.Count > 1)
      {
        var rBot = this as RepairBot;
        rBot?.UpdatePatrolOBBCache();
      }
    }

    internal void UpdatePatrolPoints(List<Vector3I> waypoints, string name)
    {
      _patrolName = null;
      _patrolList.Clear();
      _patrolIndex = -1;
      _patrolWaitTime = 1;

      if (waypoints?.Count > 0)
      {
        _patrolName = name;
        _patrolList.AddList(waypoints);
        PatrolMode = true;
      }

      if (_patrolList.Count > 1)
      {
        var rBot = this as RepairBot;
        rBot?.UpdatePatrolOBBCache();
      }
    }

    internal void UpdatePatrolPoints(List<SerializableVector3I> waypoints, string name)
    {
      _patrolName = null;
      _patrolList.Clear();
      _patrolIndex = -1;
      _patrolWaitTime = 1;

      if (waypoints?.Count > 0)
      {
        _patrolName = name;
        for (int i = 0; i < waypoints.Count; i++)
          _patrolList.Add(waypoints[i]);

        PatrolMode = true;
      }

      if (_patrolList.Count > 1)
      {
        var rBot = this as RepairBot;
        rBot?.UpdatePatrolOBBCache();
      }
    }

    Vector3I? _lastPatrolPoint;

    internal Vector3I? GetNextPatrolPoint()
    {
      if (_patrolList == null || _patrolList.Count == 0)
      {
        return null;
      }

      _patrolWaitTime = 1;
      var num = ++_patrolIndex % _patrolList.Count;
      var waypoint = _patrolList[num];

      for (int i = num + 1; i < _patrolList.Count; i++)
      {
        if (_patrolList[i] == waypoint)
        {
          _patrolIndex++;
          _patrolWaitTime++;
        }
        else
          break;
      }

      if (_lastPatrolPoint.HasValue && waypoint == _lastPatrolPoint.Value)
      {
        var worldPoint = _currentGraph.LocalToWorld(waypoint);
        if (Vector3D.DistanceSquared(worldPoint, BotInfo.CurrentBotPositionActual) < 1)
          waypoint = _currentGraph.WorldToLocal(BotInfo.CurrentBotPositionActual);
      }

      _lastPatrolPoint = waypoint;
      return waypoint;
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
        var botPosition = BotInfo.CurrentBotPositionActual;

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
          CleanPath();

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

    internal virtual void CleanPath()
    {
      _pathCollection?.CleanUp(true);
      _transitionPoint = null;
      _stuckCounter = 0;
      _stuckTimer = 0;
      NeedsTransition = false;
    }

    internal virtual bool Update()
    {
      if (Character == null || Character.MarkedForClose)
        return false;

      ++_tickCount;
      ++_ticksSinceLastAttack;
      ++_xMoveTimer;
      ++_pathTimer;
      ++_ticksSinceFoundTarget;
      ++_ticksSinceLastDismount;
      ++_lowHealthTimer;
      ++_idlePathTimer;
      ++_performanceTimer;

      _walkSwitched = false;

      if (GrenadeThrown && ++_grenadeCounter > 150)
        GrenadeThrown = false;

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
              var botIdentityId = Owner?.IdentityId ?? BotIdentityId;
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
      BotInfo?.UpdateBotState();

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
      var jetEnabled = jetComp?.TurnedOn ?? false;
      JetpackEnabled = jetEnabled;

      if (jetEnabled)
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

        if (!inSeat && _currentGraph.Ready && !UseLadder && !BotInfo.IsOnLadder && !BotInfo.WasOnLadder
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

        if (HasWeaponOrTool && !inSeat && !GrenadeThrown && AllowEquipWeapon && ToolDefinition != null)
        {
          bool canEquip = true;
          if (Target.Entity != null && Target.Entity == Owner?.Character)
            canEquip = _performanceTimer > _performanceTimerTarget;

          var tool = Character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
          if (canEquip && tool == null)
          {
            var charController = Character as Sandbox.Game.Entities.IMyControllableEntity;
            if (charController?.CanSwitchToWeapon(ToolDefinition.PhysicalItemId) == true)
            {
              charController.SwitchToWeapon(ToolDefinition.PhysicalItemId);

              var gun = Character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
              gun?.OnControlAcquired(Character);
            }
          }
        }

        if (!UseAPITargets)
          SetTarget();

        if (checkAll100)
        {
          CheckGraphNeeded = true;

          if (_currentGraph != null && _currentGraph.IsValid && Character != null && !IsDead)
          {
            if ((JetpackEnabled || (BotInfo.CurrentGravityAtBotPosition_Nat.LengthSquared() < 0.05 && BotInfo.CurrentGravityAtBotPosition_Art.LengthSquared() < 0.05))
              && Math.Abs(AiUtils.GetAngleBetween(Character.WorldMatrix.Up, _currentGraph.WorldMatrix.Up)) > MathHelper.ToRadians(3))
            {
              var matrix = _currentGraph.WorldMatrix;
              var charMatrix = Character.WorldMatrix;
              var newMatrix = MatrixD.AlignRotationToAxes(ref charMatrix, ref matrix);
              newMatrix.Translation = Character.WorldMatrix.Translation;

              Character.SetWorldMatrix(newMatrix);
              Character.Physics.SetSpeeds((Vector3)newMatrix.Up, Vector3.Zero); // typically we're stuck when this happens so push bot up to get feet unstuck
            }
          }

          if (Owner?.Character != null)
          {
            if (Owner.Character.EnabledLights != Character.EnabledLights)
              Character.SwitchLights();

            if (CanUseSeats && !UseAPITargets && !PatrolMode && Owner.Character.Parent is IMyCockpit && !(Character.Parent is IMyCockpit)
              && Vector3D.DistanceSquared(BotInfo.CurrentBotPositionActual, Owner.Character.WorldAABB.Center) <= 10000)
              AiSession.Instance.PlayerEnteredCockpit(null, Owner.IdentityId, null);
          }
          else if (Character.EnabledLights)
            Character.SwitchLights();
        }
      }

      if (_tickCount % 150 == 0)
      {
        if (!jetEnabled && BotInfo.CurrentGravityAtBotPosition_Nat.LengthSquared() == 0 && BotInfo.CurrentGravityAtBotPosition_Art.LengthSquared() == 0)
          TrySwitchJetpack(true);

        if (HelmetEnabled)
        {
          var oxyComp = Character.Components.Get<MyCharacterOxygenComponent>();
          if (oxyComp != null)
          {
            var oxygenOK = _currentGraph?.IsPositionAirtight(Character.WorldAABB.Center) ?? false;
            if (oxygenOK && Character.EnabledHelmet)
            {
              oxyComp.SwitchHelmet();

              if (MyAPIGateway.Multiplayer.MultiplayerActive)
              {
                var pkt = new HelmetChangePacket(Character.EntityId);
                AiSession.Instance.Network.RelayToClients(pkt);
              }
            }
            else if (!oxygenOK && !Character.EnabledHelmet)
            {
              oxyComp.SwitchHelmet();

              if (MyAPIGateway.Multiplayer.MultiplayerActive)
              {
                var pkt = new HelmetChangePacket(Character.EntityId);
                AiSession.Instance.Network.RelayToClients(pkt);
              }
            }
          }
        }

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
          else if (this is NeutralBotBase || this is ScavengerBot)
          {
            BehaviorReady = false;
            UseBehavior();
          }
          else if (AiSession.Instance?.GlobalSpeakTimer > 1000)
          {
            AiSession.Instance.GlobalSpeakTimer = 0;
            BehaviorReady = false;

            var num = (inSeat && AiSession.Instance.ModSaveData.AllowBotMusic) ? MyUtils.GetRandomInt(0, 10) : 0;

            if (num > 7)
              Behavior?.Sing();
            else
              UseBehavior();
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

    internal virtual void UseBehavior(bool force = false)
    {
      Behavior?.Speak();

      if (AiSession.Instance.GrenadesEnabled)
      {
        GrenadeThrown = BotFactory.ThrowGrenade(this);
        _grenadeCounter = 0;
      }
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
        var botPosition = BotInfo.CurrentBotPositionActual;
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
      var grid = gridGraph?.MainGrid;

      if (grid?.Physics == null || grid.IsStatic)
        return;

      var controlEnt = Character as Sandbox.Game.Entities.IMyControllableEntity;
      if (controlEnt == null || controlEnt.RelativeDampeningEntity?.EntityId == grid.EntityId)
        return;

      controlEnt.RelativeDampeningEntity = grid;
    }

    internal virtual bool DoDamage(float amount = 0)
    {
      IMyDestroyableObject destroyable;
      var slim = Target.Entity as IMySlimBlock;
      if (slim != null)
      {
        destroyable = slim;

        if (slim.FatBlock != null)
        {
          PlaySoundServer("ImpMetalMetalCat3", slim.FatBlock.EntityId);
        }
        else
        {
          Vector3D position;
          slim.ComputeWorldCenter(out position);
          PlaySoundServer("ImpMetalMetalCat3", position);
        }
      }
      else
      {
        var cube = Target.Entity as IMyCubeBlock;
        if (cube != null)
        {
          slim = cube.SlimBlock;
          destroyable = slim;
          PlaySoundServer("ImpMetalMetalCat3", cube.EntityId);
        }
        else
        {
          destroyable = Target.Entity as IMyDestroyableObject;
        }
      }

      if (destroyable == null || !destroyable.UseDamageSystem || destroyable.Integrity <= 0)
        return false;

      var character = Target.Entity as IMyCharacter;
      bool isCharacter = character != null;
      var rand = amount > 0 ? amount : isCharacter ? MyUtils.GetRandomFloat(_minDamage, _maxDamage) : _blockDamagePerAttack;

      BotBase botTarget = null;
      bool isPlayer = false;

      if (isCharacter)
      {
        if (character.Parent is IMyShipController)
        {
          var p = MyAPIGateway.Players.GetPlayerControllingEntity(character.Parent);
          isPlayer = p != null && AiSession.Instance.Players.ContainsKey(p.IdentityId);
        }
        else
        {
          isPlayer = AiSession.Instance.Players.ContainsKey(character.ControllerInfo.ControllingIdentityId);
        }
      }

      if (slim != null || isPlayer)
      {
        rand *= AiSession.Instance.ModSaveData.BotWeaponDamageModifier;
      }
      else if (isCharacter && AiSession.Instance.Bots.TryGetValue(character.EntityId, out botTarget) && botTarget?.Owner != null)
      {
        rand *= AiSession.Instance.ModSaveData.BotWeaponDamageModifier;
      }

      destroyable.DoDamage(rand, MyStringHash.GetOrCompute("Punch"), true);

      if (isCharacter && botTarget != null)
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

        var neutralBot = botTarget as NeutralBotBase;
        if (neutralBot != null && neutralBot.Target.Entity == null)
        {
          neutralBot.SetHostile(Character);
        }
      }

      return isCharacter;
    }

    internal virtual bool IsInRangeOfTarget()
    {
      if (Target?.HasTarget != true || Vector3D.IsZero(BotInfo.CurrentBotPositionActual))
        return false;

      return Target.IsFriendly() || Target.GetDistanceSquared() < 650000;
    }

    public virtual void AddWeapon()
    {
      if (ToolDefinition?.PhysicalItemId == null)
      {
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING: ToolDefinition.PhysicalItemId was NULL!", MessageType.WARNING);
        return;
      }

      var inventory = Character?.GetInventory();
      if (inventory == null)
      {
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING: Inventory was NULL!", MessageType.WARNING);
        return;
      }

      var toolDefinitionId = ToolDefinition.PhysicalItemId;

      if (inventory.CanItemsBeAdded(1, toolDefinitionId))
      {
        var toolItem = (MyObjectBuilder_PhysicalGunObject)MyObjectBuilderSerializer.CreateNewObject(toolDefinitionId);
        inventory.AddItems(1, toolItem);

        if (ToolDefinition.WeaponType != MyItemWeaponType.None)
        {
          string ammoSubtype = null;

          List<MyTuple<int, MyTuple<MyDefinitionId, string, string, bool>>> magList;
          if (AiSession.Instance.WcAPILoaded && AiSession.Instance.NpcSafeCoreWeaponMagazines.TryGetValue(toolDefinitionId, out magList))
          {
            ammoSubtype = magList[0].Item2.Item1.SubtypeName;
            _wcWeaponAmmoCount = 1;
            _wcWeaponMagsLeft = 1;
          }
          else
          {
            _wcWeaponAmmoCount = null;
            _wcWeaponMagsLeft = null;

            var weaponItemDef = MyDefinitionManager.Static.GetPhysicalItemDefinition(toolDefinitionId) as MyWeaponItemDefinition;
            if (weaponItemDef != null)
            {
              var weaponDef = MyDefinitionManager.Static.GetWeaponDefinition(weaponItemDef.WeaponDefinitionId);
              ammoSubtype = weaponDef?.AmmoMagazinesId?.Length > 0 ? weaponDef.AmmoMagazinesId[0].SubtypeName : null;
            }
            else
            {
              AiSession.Instance.Logger.Log($"WeaponItemDef was null for {toolDefinitionId}");
            }
          }

          if (ammoSubtype == null)
          {
            AiSession.Instance.Logger.Log($"AmmoSubtype was still null");

            if (ToolDefinition.WeaponType == MyItemWeaponType.Rifle)
            {
              ammoSubtype = "NATO_5p56x45mm";
            }
            else if (ToolDefinition.WeaponType == MyItemWeaponType.RocketLauncher)
            {
              ammoSubtype = "Missile200mm";
            }
            else if (ToolDefinition.PhysicalItemId.SubtypeName.IndexOf("auto", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              ammoSubtype = ToolDefinition.PhysicalItemId.SubtypeName.StartsWith("Full") ? "FullAutoPistolMagazine" : "SemiAutoPistolMagazine";
            }
            else
            {
              ammoSubtype = "ElitePistolMagazine";
            }
          }

          var ammoDefinition = new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), ammoSubtype);
          var amountThatFits = ((MyInventory)inventory).ComputeAmountThatFits(ammoDefinition);
          var maxAmount = Owner != null ? 25 : 10;
          var amount = Math.Min((int)amountThatFits, maxAmount);

          if (inventory.CanItemsBeAdded(amount, ammoDefinition))
          {
            var ammo = (MyObjectBuilder_AmmoMagazine)MyObjectBuilderSerializer.CreateNewObject(ammoDefinition);
            inventory.AddItems(amount, ammo);

            var charController = Character as Sandbox.Game.Entities.IMyControllableEntity;
            if (charController.CanSwitchToWeapon(toolDefinitionId))
            {
              if (!(Character.Parent is IMyCockpit))
              {
                charController.SwitchToWeapon(toolDefinitionId);

                var gun = Character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
                gun?.OnControlAcquired(Character);
              }

              HasWeaponOrTool = true;
            }
            else
              AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING! Added weapon and ammo but unable to switch to weapon!", MessageType.WARNING);
          }
          else
            AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING! Added weapon but unable to add ammo!", MessageType.WARNING);
        }
        else
        {
          var charController = Character as Sandbox.Game.Entities.IMyControllableEntity;
          if (charController.CanSwitchToWeapon(toolDefinitionId))
          {
            if (!(Character.Parent is IMyCockpit))
            {
              charController.SwitchToWeapon(toolDefinitionId);

              var gun = Character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
              gun?.OnControlAcquired(Character);
            }

            HasWeaponOrTool = true;
          }
          else
            AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING! Added welder but unable to switch to it!", MessageType.WARNING);
        }
      }
      else
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING! Unable to add welder to inventory!", MessageType.WARNING);

      SetShootInterval();
    }

    public virtual void EquipWeapon()
    {
      var rBot = this as RepairBot;
      rBot?.UpdateWeaponInfo();

      if (ToolDefinition == null)
        return;
      
      var inventory = Character?.GetInventory() as MyInventory;
      if (inventory == null)
        return;

      var weaponDefinition = ToolDefinition.PhysicalItemId;
      if (!inventory.ContainItems(1, weaponDefinition))
      {
        var controlEnt = Character as Sandbox.Game.Entities.IMyControllableEntity;
        controlEnt.SwitchToWeapon(null);
        return;
      }

      List<MyTuple<int, MyTuple<MyDefinitionId, string, string, bool>>> magList;
      if (AiSession.Instance.WcAPILoaded && AiSession.Instance.NpcSafeCoreWeaponMagazines.TryGetValue(weaponDefinition, out magList))
      {
        var ammoDef = magList[0].Item2.Item1;

        if (inventory != null && inventory.ContainItems(1, ammoDef))
        {
          _wcWeaponAmmoCount = 1;
          _wcWeaponMagsLeft = 1;
        }
        else
        {
          _wcWeaponAmmoCount = 0;
          _wcWeaponMagsLeft = 0;
        }
      }
      else
      {
        _wcWeaponAmmoCount = null;
        _wcWeaponMagsLeft = null;
      }

      var charController = Character as Sandbox.Game.Entities.IMyControllableEntity;
      if (charController?.CanSwitchToWeapon(weaponDefinition) == true)
      {
        if (!(Character.Parent is IMyCockpit))
        {
          charController.SwitchToWeapon(weaponDefinition);

          var gun = Character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
          gun?.OnControlAcquired(Character);
        }

        HasWeaponOrTool = true;
        SetShootInterval();
      }
      else
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.EquipWeapon: WARNING! Unable to switch to weapon ({weaponDefinition})!", MessageType.WARNING);
    }

    public void EnsureWeaponValidity()
    {
      var inventory = Character?.GetInventory() as MyInventory;
      var weaponDefinition = ToolDefinition?.PhysicalItemId;
      if (inventory == null || weaponDefinition == null || !inventory.ContainItems(1, weaponDefinition.Value))
      {
        ToolDefinition = null;
        var controlEnt = Character as Sandbox.Game.Entities.IMyControllableEntity;
        controlEnt?.SwitchToWeapon(null);
      }
    }

    void UpdatePatrol()
    {
      if (Target.Entity != null)
        Target.RemoveTarget();

      if (Target.Override.HasValue)
        return;

      _patrolWaitTime--;

      if (_patrolWaitTime <= 0)
      {
        var patrolPoint = GetNextPatrolPoint();

        if (patrolPoint.HasValue)
        {
          var patrolPointWorld = _currentGraph.LocalToWorld(patrolPoint.Value);
          Target.SetOverride(patrolPointWorld);
        }
      }
    }

    public virtual void SetTarget()
    {
      if (_currentGraph == null || !_currentGraph.Ready)
        return;

      if (_targetTask.Exceptions != null)
      {
        AiSession.Instance.Logger.ClearCached();
        AiSession.Instance.Logger.AddLine($"Exceptions found during {GetType().Name}.SetTarget task!\n");
        foreach (var ex in _targetTask.Exceptions)
          AiSession.Instance.Logger.AddLine($" -> {ex.ToString()}\n");

        AiSession.Instance.Logger.LogAll();
        MyAPIGateway.Utilities.ShowNotification($"Exception during {GetType().Name}.SetTarget task!");
      }

      if (_targetTask.IsComplete)
      {
        _targetTask = MyAPIGateway.Parallel.Start(_targetAction);
      }

      // Testing only!
      //_targetAction?.Invoke();
    }

    internal virtual void SetTargetInternal()
    {
      if (!WantsTarget)
      {
        if (!UseAPITargets && PatrolMode && _patrolList?.Count > 0)
        {
          UpdatePatrol();
        }

        return;
      }

      var botPosition = BotInfo.CurrentBotPositionActual;
      var curCharTgt = Target.Entity as IMyCharacter;
      if (Target.IsDestroyed())
      {
        Target.RemoveTarget();
      }
      else if (Target.Entity is IMyDoor)
      {
        ++_doorTgtCounter;
        if (_doorTgtCounter <= 8)
          return;

        _doorTgtCounter = 0;
        Target.RemoveTarget();
      }
      else if (Target.Entity != null)
      {
        // if the current target is viable there's not reason to keep trying to switch targets

        bool allowReturn = ToolDefinition == null || ToolDefinition.WeaponType == MyItemWeaponType.None;
        if (allowReturn && HasLineOfSight)
        { 
          var ent = Target.Entity as IMyEntity;
          var cube = Target.Entity as IMyCubeBlock;
          var slim = cube?.SlimBlock;
          if (slim == null)
            slim = Target.Entity as IMySlimBlock;

          if ((ent == null && slim != null && !slim.IsDestroyed) || (ent != null && !ent.MarkedForClose && ent.EntityId != Owner?.Character?.EntityId))
          {
            Vector3D entCenter;
            if (ent != null)
              entCenter = ent.GetPosition();
            else if (cube != null)
              entCenter = cube.GetPosition();
            else
              entCenter = slim.CubeGrid.GridIntegerToWorld(slim.Position);

            if (slim != null)
            {
              if (_pathCollection?.CheckIfBlockedObstacle(slim) == true)
              {
                allowReturn = false;
              }
            }
            else
            {
              var player = Target.Player ?? MyAPIGateway.Players.GetPlayerControllingEntity(ent as IMyCharacter);
              if (player != null && player.IdentityId != Owner?.IdentityId)
              {
                MyAdminSettingsEnum adminSettings;
                if (MyAPIGateway.Session.TryGetAdminSettings(player.SteamUserId, out adminSettings))
                {
                  if ((adminSettings & MyAdminSettingsEnum.Untargetable) != 0)
                  {
                    allowReturn = false;
                    Target.RemoveTarget();
                  }
                }
              }
            }

            if (allowReturn)
            {
              var maxRange = (double)AiSession.Instance.ModSaveData.MaxBotHuntingDistanceEnemy * 0.5;
              if (Vector3D.DistanceSquared(entCenter, botPosition) < maxRange * maxRange)
              {
                if (cube != null)
                {
                  if (cube.IsFunctional && !cube.SlimBlock.IsBlockUnbuilt())
                    return;
                }
                else
                  return;
              }
            }
          }
        }
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

      List<IMySlimBlock> blockList;
      if (!AiSession.Instance.SlimListStack.TryPop(out blockList) || blockList == null)
        blockList = new List<IMySlimBlock>();
      else
        blockList.Clear();

      HashSet<long> checkedGridIDs;
      if (!AiSession.Instance.GridCheckHashStack.TryPop(out checkedGridIDs) || checkedGridIDs == null)
        checkedGridIDs = new HashSet<long>();
      else
        checkedGridIDs.Clear();

      List<MyLineSegmentOverlapResult<MyEntity>> resultList;
      if (!AiSession.Instance.OverlapResultListStack.TryPop(out resultList))
        resultList = new List<MyLineSegmentOverlapResult<MyEntity>>();
      else
        resultList.Clear();

      List<Vector3I> cellList;
      if (!AiSession.Instance.LineListStack.TryPop(out cellList))
        cellList = new List<Vector3I>();
      else
        cellList.Clear();

      var onPatrol = PatrolMode && _patrolList?.Count > 0;
      var huntingDistance = AiSession.Instance.ModSaveData.MaxBotHuntingDistanceEnemy;
      var sphere = new BoundingSphereD(botPosition, huntingDistance);
      var blockDestroEnabled = MyAPIGateway.Session.SessionSettings.DestructibleBlocks;
      var allowGridCheck = blockDestroEnabled && CanDamageGrid;

      List<BotBase> helpers = null;
      if (Owner != null)
        AiSession.Instance.PlayerToHelperDict.TryGetValue(Owner.IdentityId, out helpers);  

      var botMatrix = WorldMatrix;
      var muzzlePosition = botPosition + botMatrix.Up * 0.4; // close to the muzzle height
      bool hasWeapon = HasWeaponOrTool == true && ToolDefinition.WeaponType != MyItemWeaponType.None;

      var queryType = blockDestroEnabled ? MyEntityQueryType.Both : MyEntityQueryType.Dynamic;
      MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entList, queryType);

      object tgt = null;
      double distanceToLastResortTarget = curCharTgt == null ? double.MaxValue : Vector3D.DistanceSquared(curCharTgt.WorldAABB.Center, botPosition);

      _taskPrioritiesTemp.Clear();
      for (int i = 0; i < entList.Count; i++)
      {
        var ent = entList[i];
        if (ent == null || ent.MarkedForClose)
          continue;

        var tgtPosition = ent.PositionComp.WorldAABB.Center;

        var ch = ent as IMyCharacter;
        if (ch != null)
        {
          if (ch.IsDead || ch.MarkedForClose || ch.EntityId == Character.EntityId)
            continue;

          long ownerIdentityId = ch.ControllerInfo.ControllingIdentityId;
          BotBase bot;
          if (AiSession.Instance.Bots.TryGetValue(ch.EntityId, out bot))
          {
            if (bot == null || bot.IsDead)
              continue;

            ownerIdentityId = bot.Owner?.IdentityId ?? bot.BotIdentityId;
          }
          else if (ch.IsPlayer)
          {
            if (ch.Parent is IMyShipController)
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

          var relation = MyIDModule.GetRelationPlayerPlayer(ownerIdentityId, BotIdentityId);
          if (relation == MyRelationsBetweenPlayers.Allies || relation == MyRelationsBetweenPlayers.Self)
            continue;
          else if (relation == MyRelationsBetweenPlayers.Neutral && !AiSession.Instance.ModSaveData.AllowNeutralTargets)
            continue;

          if (onPatrol)
          {
            _taskPrioritiesTemp.Add(ent);
          }
          else
          {
            var ignoreEnts = new MyEntity[helpers?.Count + 1 ?? 2];
            ignoreEnts[0] = ent;

            if (helpers?.Count > 0)
            {
              for (int h = 0; h < helpers.Count; h++)
                ignoreEnts[h + 1] = (MyEntity)helpers[h].Character;
            }
            else
            {
              ignoreEnts[1] = (MyEntity)Character;
            }

            tgtPosition = ch.GetHeadMatrix(true).Translation;

            if (AiUtils.CheckLineOfSight(ref muzzlePosition, ref tgtPosition, cellList, resultList, _currentGraph?.RootVoxel, ignoreEnts))
            {
              _taskPrioritiesTemp.Add(ent);
            }
            else
            {
              var dist = Vector3D.DistanceSquared(tgtPosition, botPosition);
              if (dist < distanceToLastResortTarget && dist < huntingDistance * huntingDistance)
                curCharTgt = ch;
            }
          }
        }
        else if (allowGridCheck)
        {
          var grid = ent as MyCubeGrid;
          if (grid?.Physics != null && !grid.IsPreview && !grid.MarkedForClose && !checkedGridIDs.Contains(grid.EntityId))
          {
            gridGroups.Clear();
            grid.GetGridGroup(GridLinkTypeEnum.Mechanical).GetGrids(gridGroups);

            foreach (var g in gridGroups)
            {
              var myGrid = g as MyCubeGrid;
              if (myGrid == null || myGrid.IsPreview || myGrid.MarkedForClose)
                continue;

              foreach (var cpit in myGrid.OccupiedBlocks)
              {
                if (cpit.Pilot != null)
                  entList.Add(cpit.Pilot);
              }

              checkedGridIDs.Add(g.EntityId);
              long myGridOwner;
              try
              {
                myGridOwner = myGrid.BigOwners?.Count > 0 ? myGrid.BigOwners[0] : myGrid.SmallOwners?.Count > 0 ? myGrid.SmallOwners[0] : 0L;
              }
              catch
              {
                myGridOwner = 0L;
              }

              var relation = MyIDModule.GetRelationPlayerPlayer(myGridOwner, BotIdentityId, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
              if (myGridOwner > 0 && (relation == MyRelationsBetweenPlayers.Allies || relation == MyRelationsBetweenPlayers.Self))
                continue;
              else if ((myGridOwner == 0 || relation == MyRelationsBetweenPlayers.Neutral) && !AiSession.Instance.ModSaveData.AllowNeutralTargets)
                continue;

              blockList.Clear();
              g.GetBlocks(blockList);

              for (int k = blockList.Count - 1; k >= 0; k--)
              {
                var block = blockList[k];
                if (block?.CubeGrid == null || block.IsDestroyed || block.CubeGrid.EntityId != g.EntityId)
                {
                  blockList.RemoveAtFast(k);
                  continue;
                }

                var fat = block.FatBlock;
                if (fat != null)
                {
                  tgtPosition = block.FatBlock.WorldAABB.Center;

                  if (fat is IMyAirtightHangarDoor)
                    tgtPosition += fat.WorldMatrix.Down * g.GridSize;
                }
                else
                {
                  block.ComputeWorldCenter(out tgtPosition);
                }

                cellList.Clear();
                g.RayCastCells(muzzlePosition, tgtPosition, cellList);

                var localEnd = g.WorldToGridInteger(tgtPosition);
                var endBlock = g.GetCubeBlock(localEnd);
                var allowedDistance = g.GridSizeEnum == MyCubeSize.Large ? 5 : 10;
                var line = new LineD(muzzlePosition, tgtPosition);
                bool add = true;

                foreach (var cell in cellList)
                {
                  var otherBlock = g.GetCubeBlock(cell);
                  if (otherBlock != null && cell != localEnd && otherBlock != endBlock)
                  {
                    var otherFat = otherBlock.FatBlock;
                    if (otherFat != null)
                    {
                      MyIntersectionResultLineTriangleEx? hit;
                      if (otherFat.GetIntersectionWithLine(ref line, out hit, IntersectionFlags.ALL_TRIANGLES) && hit.HasValue)
                      {
                        if (!hasWeapon || Vector3D.DistanceSquared(hit.Value.IntersectionPointInWorldSpace, tgtPosition) > allowedDistance * allowedDistance)
                        {
                          add = false;
                          break;
                        }
                      }
                    }
                    else if (!hasWeapon || Vector3D.DistanceSquared(grid.GridIntegerToWorld(cell), tgtPosition) > allowedDistance * allowedDistance)
                    {
                      add = false;
                      break;
                    }
                  }
                }

                if (!add)
                {
                  blockList.RemoveAtFast(k);
                }
              }

              blockList.ShellSort(botPosition);
              _taskPrioritiesTemp.AddRange(blockList);
            }
          }
        }
      }

      blockList.Clear();
      AiSession.Instance.SlimListStack.Push(blockList);

      _taskPrioritiesTemp.PrioritySort(_taskPriorities, TargetPriorities, botPosition);
      bool damageToDisable = TargetPriorities.DamageToDisable;
      var huntDistanceSqd = huntingDistance * huntingDistance;
      var graph = _currentGraph as CubeGridMap;

      var gravityNormalized = BotInfo.CurrentGravityAtBotPosition_Nat.LengthSquared() > 0 ? BotInfo.CurrentGravityAtBotPosition_Nat : BotInfo.CurrentGravityAtBotPosition_Art;
      if (gravityNormalized.LengthSquared() > 0)
        gravityNormalized.Normalize();

      bool useCurrent = false;
      int currentPriority = int.MaxValue;
      if (Target.Entity != null && TargetPriorities != null && HasLineOfSight)
        currentPriority = TargetPriorities.GetBlockPriority(Target.Entity);

      foreach (var priKvp in _taskPriorities)
      {
        if (priKvp.Key >= currentPriority)
        {
          useCurrent = true;
          break;
        }

        for (int j = 0; j < priKvp.Value.Count; j++)
        {
          var obj = priKvp.Value[j];
          var ch = obj as IMyCharacter;
          if (ch != null)
          {
            if (!ch.IsDead && !ch.MarkedForClose)
            {
              tgt = ch;
              break;
            }
          }
          else
          {
            var cube = obj as IMyCubeBlock;
            var slim = cube?.SlimBlock;
            if (slim == null)
              slim = obj as IMySlimBlock;

            if (slim != null && !slim.IsDestroyed)
            {
              if (damageToDisable)
              {
                var funcBlock = slim.FatBlock as IMyFunctionalBlock;
                if (funcBlock != null && !funcBlock.IsFunctional)
                  continue;
                else if (slim.FatBlock is IMyDoor && slim.IsBlockUnbuilt())
                  continue;
              }

              Vector3D slimWorld;
              if (slim.FatBlock != null)
                slimWorld = slim.FatBlock.GetPosition();
              else
                slimWorld = slim.CubeGrid.GridIntegerToWorld(slim.Position);

              if (Vector3D.DistanceSquared(botPosition, slimWorld) > huntDistanceSqd)
                continue;

              if (_pathCollection?.CheckIfBlockedObstacle(slim) == true)
                continue;

              var node = slim.Position;
              if (slim.CubeGrid.EntityId != graph?.MainGrid.EntityId)
              {
                if (slim.CubeGrid.GridSizeEnum == MyCubeSize.Small)
                {
                  if (gravityNormalized.LengthSquared() > 0)
                    slimWorld -= gravityNormalized;
                  else
                    slimWorld += _currentGraph?.WorldMatrix.Up ?? WorldMatrix.Up;
                }

                node = _currentGraph.WorldToLocal(slimWorld);
              }

              //if (_currentGraph.InBounds(node))
              //{
              //  if (!_currentGraph.GetClosestValidNode(this, node, out node, isSlimBlock: true, allowAirNodes: CanUseAirNodes))
              //  {
              //    _pathCollection?.AddBlockedObstacle(slim);
              //    continue;
              //  }
              //}
              //else
              {
                var otherGraph = AiSession.Instance.GetGridGraph((MyCubeGrid)slim.CubeGrid, WorldMatrix);
                if (otherGraph != null && otherGraph.Ready)
                {
                  Vector3I testNode = slim.Position;
                  if (!otherGraph.GetClosestValidNode(this, testNode, out testNode, isSlimBlock: true, allowAirNodes: CanUseAirNodes))
                    continue;

                  node = _currentGraph.WorldToLocal(otherGraph.LocalToWorld(testNode));
                }
              }

              if (!_currentGraph.IsObstacle(node, this, true))
              {
                tgt = obj;
                break;
              }
            }
          }
        }

        if (tgt != null || useCurrent)
          break;
      }

      blockTargets.Clear();
      gridGroups.Clear();
      entList.Clear();
      checkedGridIDs.Clear();
      resultList.Clear();
      cellList.Clear();

      AiSession.Instance.EntListStack.Push(blockTargets);
      AiSession.Instance.GridGroupListStack.Push(gridGroups);
      AiSession.Instance.EntListStack.Push(entList);
      AiSession.Instance.GridCheckHashStack.Push(checkedGridIDs);
      AiSession.Instance.OverlapResultListStack.Push(resultList);
      AiSession.Instance.LineListStack.Push(cellList);

      if (useCurrent)
        return;

      if (tgt == null)
      {
        if (onPatrol)
        {
          UpdatePatrol();
        }
        else
        {
          Target.RemoveTarget();
        }

        if (curCharTgt != null && Vector3D.DistanceSquared(curCharTgt.WorldAABB.Center, botPosition) < huntingDistance * huntingDistance)
        {
          tgt = curCharTgt;
        }
        else
        {
          return;
        }
      }

      if (onPatrol && Target.Override.HasValue)
      {
        _patrolIndex = Math.Max((short)-1, (short)(_patrolIndex - 1));
        Target.RemoveOverride(false);
      }

      var tgtChar = tgt as IMyCharacter;
      var parent = (tgtChar != null && tgtChar.Parent != null) ? tgtChar.Parent : tgt;
      if (ReferenceEquals(Target.Entity, parent))
        return;

      if (tgtChar == null && tgt != null)
      {
        if (!_currentGraph.IsGridGraph)
        {
          CheckGraphNeeded = true;
        }
        else
        {
          var cube = tgt as IMyCubeBlock;
          var slim = cube?.SlimBlock;
          if (slim == null)
            slim = tgt as IMySlimBlock;

          if (slim != null)
          {
            Vector3D slimWorld;
            slim.ComputeWorldCenter(out slimWorld);

            if (!_currentGraph.IsPositionValid(slimWorld))
              CheckGraphNeeded = true;
          }
        }
      }

      Target.SetTarget(null, parent);
      CleanPath();

      var seat = Character.Parent as IMyCockpit;
      if (seat != null && Owner == null && Target.Entity != null)
      {
        seat.RemovePilot();
      }
    }

    internal void TrySwitchWalk()
    {
      if (_walkSwitched)
        return;

      _walkSwitched = true;

      if (!IsWolf)
      {
        if (PatrolMode && Target.Entity == null && AiSession.Instance.ModSaveData.EnforceWalkingOnPatrol)
        {
          if (BotInfo.IsRunning)
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
    }

    internal bool CheckGraphValidity(Vector3D targetPosition, ref bool force, out MyCubeGrid newGrid, out Vector3D newGraphPosition, out Vector3D intermediary, out bool botInNewBox)
    {
      bool result = false;
      newGrid = null;
      botInNewBox = false;
      newGraphPosition = targetPosition;
      intermediary = targetPosition;
      var botPosition = BotInfo.CurrentBotPositionActual;

      bool positionValid = _currentGraph?.IsPositionValid(targetPosition) == true;
      if (positionValid && !_currentGraph.IsGridGraph && !(this is RepairBot))
      {
        if (Target.IsCubeBlock || Target.IsSlimBlock)
          positionValid = false;
      }

      bool getNewGraph = !positionValid && !force;

      if (getNewGraph && _currentGraph != null)
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
            maxLength = (VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultCellSize);

          newGraphPosition = graphCenter + normal * maxLength;

          if (_currentGraph.RootVoxel is MyPlanet)
          {
            var surfacePoint = _currentGraph.GetClosestSurfacePointFast(null, newGraphPosition, _currentGraph.WorldMatrix.Up);

            if (surfacePoint.HasValue)
              newGraphPosition = surfacePoint.Value;

            // TODO: Need to check if in voxel here ???
          }
        }
        else
        {
          maxLength = 2 * (VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultCellSize) - (3 * _currentGraph.CellSize);
          lengthToTop = VoxelGridMap.DefaultHalfSize;

          newGraphPosition = graphCenter + normal * maxLength;

          var intVecDir = Base6Directions.GetIntVector(dir);
          var dotUp = Vector3I.Up.Dot(ref intVecDir);

          bool targetAboveBox = false;
          bool targetBelowBox = false;

          Vector3D projUp = AiUtils.Project(vectorGraphToTarget, graphUp);
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
        }

        //if (_currentGraph.IsGridGraph)
        //{
        //  if (!_currentGraph.GetEdgeDistanceInDirection(normal, out maxLength))
        //    maxLength = 2 * (VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultCellSize) - (3 * _currentGraph.CellSize);
        //  else
        //    maxLength += (VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultCellSize) - (3 * _currentGraph.CellSize);

        //  if (!_currentGraph.GetEdgeDistanceInDirection(graphUp, out lengthToTop))
        //    lengthToTop = VoxelGridMap.DefaultHalfSize;
        //}
        //else
        //{
        //  maxLength = 2 * (VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultCellSize) - (3 * _currentGraph.CellSize);
        //  lengthToTop = VoxelGridMap.DefaultHalfSize;
        //}

        //newGraphPosition = graphCenter + normal * maxLength;

        //var intVecDir = Base6Directions.GetIntVector(dir);
        //var dotUp = Vector3I.Up.Dot(ref intVecDir);

        //bool targetAboveBox = false;
        //bool targetBelowBox = false;

        //Vector3D projUp = VectorUtils.Project(vectorGraphToTarget, graphUp);
        //if (projUp.LengthSquared() > lengthToTop * lengthToTop)
        //{
        //  var projGraphUp = projUp.Dot(ref graphUp);
        //  targetAboveBox = projGraphUp > 0;
        //  targetBelowBox = projGraphUp < 0;
        //}

        //var pointAboveNext = newGraphPosition + (graphUp * lengthToTop * 0.9);
        //var pointAboveThis = graphCenter + (graphUp * lengthToTop * 0.9) + (normal * maxLength * 0.75);
        //var pointBelowThis = graphCenter - (graphUp * lengthToTop * 0.9) + (normal * maxLength * 0.5);

        //if (targetAboveBox && dotUp <= 0 && GridBase.PointInsideVoxel(pointAboveNext, _currentGraph.RootVoxel))
        //{
        //  // the target is going to be above the current map box
        //  maxLength = lengthToTop + (VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultCellSize) - (3 * _currentGraph.CellSize);

        //  if (GridBase.PointInsideVoxel(pointAboveThis, _currentGraph.RootVoxel))
        //  {
        //    newGraphPosition = graphCenter + graphUp * maxLength;
        //  }
        //  else
        //  {
        //    newGraphPosition += graphUp * maxLength;
        //  }
        //}
        //else if (targetBelowBox && dotUp >= 0 && !GridBase.PointInsideVoxel(pointBelowThis, _currentGraph.RootVoxel))
        //{
        //  // the target is going to be below the current map box
        //  maxLength = lengthToTop + (VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultCellSize) - (3 * _currentGraph.CellSize);
        //  newGraphPosition = graphCenter - graphUp * maxLength;
        //}

        var vectorBotToTgt = targetPosition - botPosition;
        if (vectorBotToTgt.LengthSquared() > 125 * 125)
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

      double distanceTargetToBot = Vector3D.Distance(botPosition, newGraphPosition);
      foreach (var overlapResult in rayEntities)
      {
        var grid = overlapResult.Element as MyCubeGrid;
        if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose || grid.Closed)
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

        var halfAABB = Character.WorldAABB.HalfExtents;
        var botHead = grid.WorldToGridInteger(botPosition + halfAABB);
        var botFeet = grid.WorldToGridInteger(botPosition - halfAABB);

        var boxI = new BoundingBoxI(min, max);
        boxI.Include(botHead);
        boxI.Include(botFeet);

        min = boxI.Min;
        max = boxI.Max;

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
        biggest = GridBase.GetLargestGridForMap(grid) as MyCubeGrid;

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

        box.Inflate(1);
        var center = biggest.GridIntegerToWorld(box.Center);
        var halfExtents = box.HalfExtents * biggest.GridSize + Vector3.Half * biggest.GridSize;
        var upDir = biggest.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
        var upVec = Vector3I.Abs(Base6Directions.GetIntVector(upDir));
        halfExtents += upVec * 3 * biggest.GridSize;

        var quat = Quaternion.CreateFromRotationMatrix(biggest.WorldMatrix);
        var obb = new MyOrientedBoundingBoxD(center, halfExtents, quat);

        var botAlreadyIn = obb.Contains(ref botPosition) || obb.Contains(ref newGraphPosition);

        if (!botAlreadyIn && obb.IntersectsOrContains(ref lineToTarget) == null)
          continue;

        var addition = biggest.Physics.IsStatic ? 10 : 5;
        obb.HalfExtent = ((Vector3)(box.HalfExtents + addition) + Vector3.Half) * biggest.GridSize;

        if (botAlreadyIn || obb.Contains(ref newGraphPosition) || obb.Contains(ref botPosition))
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

      if (newGrid != null && newGrid.EntityId != gridGraph?.MainGrid?.EntityId && newGridOBB != null)
      {
        botInNewBox = newGridOBB.Value.Contains(ref botPosition);

        if (!botInNewBox && _currentGraph != null)
        {
          var curOBB = _currentGraph.OBB;
          if (!newGridOBB.Value.Intersects(ref curOBB))
          {
            newGrid = null;
          }
        }
      }
      else if (positionValid)
      {
        result = true;
      }
      else if (newGrid == null && getNewGraph && _currentGraph != null)
      {
        if (_currentGraph.IsInBufferZone(botPosition))
          botInNewBox = true;
      }

      return result;
    }

    internal bool CheckIfCloseEnoughToAct(ref Vector3D targetPosition, ref Vector3D goToPosition, out bool shouldReturn)
    {
      shouldReturn = false;
      var botPos = BotInfo.CurrentBotPositionAdjusted;
      var localBot = _currentGraph.WorldToLocal(botPos);

      Vector3I localTgt;
      var character = Target.Entity as IMyCharacter;
      if (character != null && character.EntityId == Owner?.Character?.EntityId)
        localTgt = _currentGraph.WorldToLocal(goToPosition);
      else
        localTgt = _currentGraph.WorldToLocal(targetPosition);
      
      var manhattanDist = Vector3I.DistanceManhattan(localTgt, localBot);

      if (targetPosition == Target.Override)
      {
        bool onLadder = BotInfo.IsOnLadder;

        if (_currentGraph.IsGridGraph)
        {
          var gridGraph = _currentGraph as CubeGridMap;
          var localTarget = gridGraph.WorldToLocal(targetPosition);

          if (PatrolMode)
          {
            Node node;

            if (_pathCollection?.NextNode != null && _pathCollection.NextNode.Position == localTarget)
              node = _pathCollection.NextNode;
            else
              gridGraph.TryGetNodeForPosition(localTarget, out node);

            if (node != null)
            {
              var checkPosition = _currentGraph.LocalToWorld(node.Position) + node.Offset;
              var relVector = Vector3D.TransformNormal(checkPosition - botPos, MatrixD.Transpose(WorldMatrix));
              var flattenedVector = new Vector3D(relVector.X, 0, relVector.Z);
              var flattenedLengthSquared = flattenedVector.LengthSquared();

              shouldReturn = flattenedLengthSquared < 0.25 && Math.Abs(relVector.Y) < 0.5;

              if (shouldReturn && JetpackEnabled && Character.Physics.LinearVelocity.LengthSquared() > 0.1f)
                Character.Physics.SetSpeeds(Character.Physics.LinearVelocity * 0.2f, Vector3.Zero);

              return shouldReturn;
            }
          }

          var cube = gridGraph.GetBlockAtPosition(localTarget);
          var seat = cube?.FatBlock as IMyCockpit;
          if (seat != null && !onLadder)
          {
            if (CanUseSeats && seat.Pilot == null && (localTgt - localBot).AbsMax() < 2 && manhattanDist <= 2)
            {
              var cPit = seat as MyCockpit;
              if (cPit != null)
              {
                var seatCube = seat as MyCubeBlock;
                var shareMode = seatCube.IDModule?.ShareMode ?? MyOwnershipShareModeEnum.All;
                bool changeBack = false;
                bool ignoreShareMode = false;

                if (shareMode != MyOwnershipShareModeEnum.All)
                {
                  var seatRelation = seatCube.IDModule.GetUserRelationToOwner(BotIdentityId);
                  if (seatRelation == MyRelationsBetweenPlayerAndBlock.FactionShare)
                  {
                    ignoreShareMode = true;

                    if (seatCube.IDModule.ShareMode != MyOwnershipShareModeEnum.Faction)
                    {
                      seatCube.IDModule.ShareMode = MyOwnershipShareModeEnum.Faction;
                      changeBack = true;
                    }
                  }
                  else
                  {
                    var owner = Owner?.IdentityId ?? BotIdentityId;
                    var gridOwner = seat.CubeGrid.BigOwners?.Count > 0 ? seat.CubeGrid.BigOwners[0] : seat.CubeGrid.SmallOwners?.Count > 0 ? seat.CubeGrid.SmallOwners[0] : seat.SlimBlock.BuiltBy;

                    var relation = MyIDModule.GetRelationPlayerPlayer(owner, gridOwner, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
                    if (relation != MyRelationsBetweenPlayers.Enemies)
                    {
                      changeBack = true;
                      seatCube.IDModule.ShareMode = MyOwnershipShareModeEnum.All;
                    }
                  }
                }

                if (ignoreShareMode || seatCube.IDModule == null || seatCube.IDModule.ShareMode == MyOwnershipShareModeEnum.All)
                {
                  var relativePosition = Vector3D.Rotate(botPos - seat.GetPosition(), MatrixD.Transpose(seat.WorldMatrix));
                  AiSession.Instance.BotToSeatRelativePosition[Character.EntityId] = relativePosition;

                  seat.AttachPilot(this.Character);
                }

                if (changeBack)
                  AiSession.Instance.BotToSeatShareMode[Character.EntityId] = shareMode;

                shouldReturn = true;
                return true;
              }
            }

            return manhattanDist < 2;
          }
        }

        var distance = Vector3D.DistanceSquared(botPos, targetPosition);
        return !onLadder && distance < 1.75;
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
        else if (IsLargeSpider)
        {
          checkDistance = 5;
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
            if (BotInfo.IsFlying && Owner?.Character != null && Target.Entity == Owner.Character)
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

            var tgtDoor = Target.Entity as IMyDoor;
            if (tgtDoor == null)
            {
              var tgtSlim = Target.Entity as IMySlimBlock;
              tgtDoor = tgtSlim?.FatBlock as IMyDoor;
            }

            if (tgtDoor != null && Vector3D.IsZero(doorPos - tgtDoor.WorldAABB.Center))
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
          if (hitGrid != null && (Target.IsCubeBlock || Target.IsSlimBlock || Target.IsInventory))
          {
            var localPos = hitGrid.WorldToGridInteger(hit.Position);
            var cube = hitGrid.GetCubeBlock(localPos);
            if (cube == null)
            {
              var fixedPos = hit.Position - hit.Normal * hitGrid.GridSize * 0.2f;
              localPos = hitGrid.WorldToGridInteger(fixedPos);
              cube = hitGrid.GetCubeBlock(localPos);

              if (cube == null)
              {
                fixedPos = hit.Position + hit.Normal * hitGrid.GridSize * 0.2f;
                localPos = hitGrid.WorldToGridInteger(fixedPos);
                cube = hitGrid.GetCubeBlock(localPos);

                if (cube == null)
                {
                  // we hit a grid but can't find the cube?
                  continue;
                }
              }
            }

            var targetCube = Target.IsInventory ? Target.Inventory : Target.IsCubeBlock ? (Target.Entity as IMyCubeBlock).SlimBlock : Target.Entity as IMySlimBlock;
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
              else
              {
                Vector3 targetExtents;
                targetCube.ComputeScaledHalfExtents(out targetExtents);
                var ext = targetExtents.AbsMax() * 2;

                if ((cube.Position - targetCube.Position).RectangularLength() <= ext / targetCube.CubeGrid.GridSize)
                {
                  // Just in case we are positioned too close to the block 
                  // and the ray clips through the corner of a neighboring block

                  var worldCube = cube.CubeGrid.GridIntegerToWorld(cube.Position);
                  result = Vector3D.DistanceSquared(hit.Position, worldCube) < ext;
                }
                else if (cube.FatBlock is IMyMechanicalConnectionBlock && targetCube.FatBlock is IMyAttachableTopBlock)
                {
                  var mechBlock = cube.FatBlock as IMyMechanicalConnectionBlock;
                  var topBlock = targetCube.FatBlock as IMyAttachableTopBlock;
                  result = mechBlock.Top == topBlock;
                }
                else
                  result = false;
              }
            }
            else
              result = false;

            break;
          }
          
          if (Target.IsFloater)
          {
            var floaterPosition = Target.CurrentGoToPosition;
            var distanceToFloater = Vector3D.DistanceSquared(floaterPosition, hit.Position);
            result = distanceToFloater < 2.25;
            break;
          }

          var voxelBase = hitEnt as MyVoxelBase;
          if (voxelBase != null && (Target.IsCubeBlock || Target.IsSlimBlock || Target.IsInventory))
          {
            var slim = Target.IsCubeBlock ? (Target.Entity as IMyCubeBlock).SlimBlock : Target.IsInventory ? Target.Inventory : Target.Entity as IMySlimBlock;
            if (slim != null)
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
          
          result = HasLineOfSight;
          break;
        }

        hitlist.Clear();
        AiSession.Instance.HitListStack.Push(hitlist);

        return result;
      }

      return true; // should this be false ??
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

      Vector3I start = _currentGraph.WorldToLocal(BotInfo.CurrentBotPositionActual);
      bool startDenied = _pathCollection.DeniedDoors.ContainsKey(start);
      if (!_currentGraph.GetClosestValidNode(this, start, out start, currentIsDenied: startDenied))
      {
        if (!BotInfo.IsJumping && ++_teleportCounter > 5)
        {
          _teleportCounter = 0;

          if (BotInfo.IsFalling && (BotInfo.CurrentGravityAtBotPosition_Nat.LengthSquared() > 0 || BotInfo.CurrentGravityAtBotPosition_Art.LengthSquared() > 0))
            CleanPath();
          else
            _currentGraph.TeleportNearby(this);
        }
        else
          CleanPath();

        _idlePathTimer = 0;
        return;
      }

      _teleportCounter = 0;
      Vector3I goal = _currentGraph.WorldToLocal(moveTo);
      bool goalDenied = _pathCollection.DeniedDoors.ContainsKey(goal);
      if (!_currentGraph.GetClosestValidNode(this, goal, out goal, currentIsDenied: goalDenied) || start == goal)
      {
        _moveTo = null;
        return;
      }

      _lastEnd = moveTo;
      CleanPath();

      _pathWorkData.PathStart = start;
      _pathWorkData.PathEnd = goal;
      _pathWorkData.IsIntendedGoal = Owner != null || !Target.HasTarget;
      _pathCollection.PathTimer.Restart();

      _task = MyAPIGateway.Parallel.StartBackground(_pathWorkAction, _pathWorkCallBack, _pathWorkData);
    }

    internal void CheckLineOfSight()
    {
      if (AwaitingCallBack || Character == null || Character.MarkedForClose || Character.IsDead || IsDead)
        return;

      if (!HasWeaponOrTool || Target?.Entity == null)
      {
        HasLineOfSight = false;
        return;
      }

      IMyCharacter tgtChar = null;
      IMyAirtightHangarDoor hangar = null;
      IMySlimBlock slim = null;

      Vector3D tgtPosition;
      var targetEnt = Target.Entity as IMyEntity;
      if (targetEnt == null)
      {
        slim = Target.Entity as IMySlimBlock;
        if (slim == null)
        {
          HasLineOfSight = false;
          return;
        }
        else
        {
          slim.ComputeWorldCenter(out tgtPosition);
        }
      }
      else
      {
        tgtChar = targetEnt as IMyCharacter;
        hangar = targetEnt as IMyAirtightHangarDoor;
        tgtPosition = targetEnt.WorldAABB.Center;
      }

      var botMatrix = WorldMatrix;
      var botPosition = BotInfo.CurrentBotPositionActual;
      var muzzlePosition = botPosition + botMatrix.Up * 0.4; // close to the muzzle height

      if (tgtChar != null)
      {
        tgtPosition = tgtChar.GetHeadMatrix(true).Translation;
      }
      else if (hangar != null)
      { 
        tgtPosition += hangar.WorldMatrix.Down * hangar.CubeGrid.GridSize;
      }

      var vectorToTgt = tgtPosition - muzzlePosition;
      var dotFwd = vectorToTgt.Dot(botMatrix.Forward);
      if (dotFwd <= 0)
      {
        HasLineOfSight = false;
        return;
      }

      var angle = AiUtils.GetAngleBetween(botMatrix.Forward, vectorToTgt);
      if (Math.Abs(angle) > AiUtils.PiOver3)
      {
        if (hangar != null)
        {
          // Just in case the bot happens to be standing in front of the tip of the hangar
          tgtPosition += hangar.WorldMatrix.Down * hangar.CubeGrid.GridSize;
          angle = AiUtils.GetAngleBetween(botMatrix.Forward, tgtPosition - muzzlePosition);
          if (Math.Abs(angle) > AiUtils.PiOver3)
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

      AwaitingCallBack = true;
      _hitList.Clear();

      MyAPIGateway.Physics.CastRayParallel(ref muzzlePosition, ref tgtPosition, _hitList, CollisionLayers.CharacterCollisionLayer, RayBlockedCallback);
    }

    void RayBlockedCallback(List<IHitInfo> hitList)
    {
      if (Character == null || Character.IsDead || Character.MarkedForClose || IsDead)
      {
        AwaitingCallBack = false;
        HasLineOfSight = false;
        return;
      }

      var targetEnt = Target?.Entity as IMyEntity;
      var character = targetEnt as IMyCharacter;
      var slim = Target?.Entity as IMySlimBlock;
      if (slim == null && (targetEnt == null || character?.IsDead == true))
      {
        AwaitingCallBack = false;
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
      var door = targetEnt as IMyDoor;
      var slimGrid = slim?.CubeGrid;
      var checkSlim = targetEnt == null && slimGrid != null;

      int helperCount = helpers?.Count ?? 0;
      HasLineOfSight = true;

      foreach (var hitInfo in hitList)
      {
        var hitEnt = hitInfo?.HitEntity;
        if (hitEnt == null || hitEnt.MarkedForClose)
          continue;

        if (hitEnt.EntityId == targetEnt?.EntityId)
          break;

        if (hitEnt.EntityId == Character.EntityId)
          continue;

        if (checkSlim && hitEnt.EntityId == slimGrid.EntityId)
        {
          var hitPos = hitInfo.Position - hitInfo.Normal * 0.2f;
          var slimGridLocal = slimGrid.WorldToGridInteger(hitPos);

          if (slimGridLocal == slim.Position || slimGrid.GetCubeBlock(slimGridLocal)?.Position == slim.Position)
            break;
        }

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

          var allowedDistance = cube.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 5 : 10;
          if (Vector3D.DistanceSquared(hitInfo.Position, cube.PositionComp.WorldAABB.Center) <= allowedDistance * allowedDistance)
            break;
        }

        HasLineOfSight = false;
        break;
      }

      if (!HasLineOfSight)
        WaitForLOSTimer = true;

      AwaitingCallBack = false;
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
        
        if (transitionOk && nextGraphOK && !_nextGraph.IsGridGraph)
        {
          var tp = _currentGraph.LocalToWorld(_transitionPoint.Position);
          var sphere = new BoundingSphereD(tp, 3);
          List<MyEntity> entList;
          if (!AiSession.Instance.EntListStack.TryPop(out entList) || entList == null)
            entList = new List<MyEntity>();
          else
            entList.Clear();

          MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entList);

          for (int i = 0; i < entList.Count; i++)
          {
            var grid = entList[i] as MyCubeGrid;
            if (grid != null && !grid.IsPreview && !grid.MarkedForClose && grid.GridSizeEnum == MyCubeSize.Large && grid.BlocksCount >= 5)
            {
              transitionOk = false;
              break;
            }
          }

          entList.Clear();
          AiSession.Instance.EntListStack.Push(entList);
        }

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
          var botPosition = BotInfo.CurrentBotPositionActual;

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
            if (localBot != _transitionPoint.Position && !gridGraph.IsInBufferZone(botPosition))
            {
              switchNow = false;
            }
          }

          if (switchNow)
          {
            AdjustPatrolPointsForNewGraph(_currentGraph, _nextGraph);

            _pathCollection?.OnGridBaseClosing();
            _previousGraph = _currentGraph;
            _currentGraph = _nextGraph;
            _nextGraph = null;
            _transitionPoint = null;
            _transitionIdle = false;
            NeedsTransition = false;
            _pathTimer = 101;

            if (_pathCollection != null)
            {
              _currentGraph.HookEventsForPathCollection(_pathCollection);
              _pathCollection.ClearObstacles();
              CleanPath();
            }
          }
        }
      }
      else if (!CheckGraphValidity(targetPosition, ref force, out newGrid, out newGraphPosition, out intermediatePosition, out botInNewBox))
      {
        NeedsTransition = !force;
        var botPosition = BotInfo.CurrentBotPositionActual;
        var botMatrix = _currentGraph.WorldMatrix;

        if (newGrid != null || _currentGraph.IsGridGraph || !_currentGraph.IsPositionValid(newGraphPosition))
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

            if (!botInNewBox && (!gridGraph.IsInBufferZone(botPosition) || _nextGraph?.IsPositionValid(botPosition) != true))
            {
              switchNow = false;
              Node tgtPoint = null;

              if (_nextGraph != null)
              {
                var direction = _currentGraph.WorldMatrix.GetClosestDirection(_nextGraph.OBB.Center - _currentGraph.OBB.Center);
                tgtPoint = gridGraph.GetBufferZoneTargetPositionFromPrunik(ref _nextGraph.OBB, ref direction, ref targetPosition, this);

                if (tgtPoint == null)
                {
                  var midPoint = (_currentGraph.OBB.Center + _nextGraph.OBB.Center) * 0.5;
                  _nextGraph = AiSession.Instance.GetVoxelGraph(midPoint, WorldMatrix);

                  if (_nextGraph != null)
                    tgtPoint = gridGraph.GetBufferZoneTargetPositionFromPrunik(ref _nextGraph.OBB, ref direction, ref targetPosition, this);
                }
              }

              if (tgtPoint == null)
              {
                var bufferZonePosition = gridGraph.GetBufferZoneTargetPosition(targetPosition, center);

                if (bufferZonePosition.HasValue)
                {
                  if (!_currentGraph.IsPositionUsable(this, bufferZonePosition.Value))
                  {
                    if (_currentGraph.RootVoxel is MyPlanet)
                    {
                      bufferZonePosition = gridGraph.GetClosestSurfacePointFast(this, bufferZonePosition.Value, botMatrix.Up);
                    }
                    else
                    {
                      bufferZonePosition = null;
                    }
                  }

                  var checkPos = bufferZonePosition ?? Vector3D.Zero;
                  if (bufferZonePosition.HasValue && _currentGraph.OBB.Contains(ref checkPos))
                  {
                    var localNode = _currentGraph.WorldToLocal(bufferZonePosition.Value);
                    if (_currentGraph.GetClosestValidNode(this, localNode, out localNode))
                    {
                      _currentGraph.TryGetNodeForPosition(localNode, out tgtPoint);
                    }
                  }
                }
              }

              if (tgtPoint == null || !NeedsTransition)
              {
                CleanPath();
                _nextGraph = null;
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
            Node tgtPoint = null;

            if (!botInNewBox)
            {
              switchNow = false;
              var direction = _currentGraph.WorldMatrix.GetClosestDirection(_nextGraph.OBB.Center - _currentGraph.OBB.Center);
              tgtPoint = _currentGraph.GetBufferZoneTargetPositionFromPrunik(ref _nextGraph.OBB, ref direction, ref targetPosition, this);

              if (tgtPoint == null || nextIsPrev)
              {
                var midPoint = (_currentGraph.OBB.Center + _nextGraph.OBB.Center) * 0.5;

                if (_nextGraph.RootVoxel != null)
                {
                  var nextOBB = _nextGraph.OBB;
                  var box = new BoundingBoxD(-nextOBB.HalfExtent, nextOBB.HalfExtent);
                  var tuple = _nextGraph.RootVoxel.GetVoxelContentInBoundingBox_Fast(box, _nextGraph.WorldMatrix);

                  if (tuple.Item2 > 0.6)
                  {
                    var vectorToNext = _nextGraph.OBB.Center - _currentGraph.OBB.Center;
                    var dir = _currentGraph.WorldMatrix.GetClosestDirection(vectorToNext);

                    var checkDirection = (dir >= Base6Directions.Direction.Up) ? _currentGraph.WorldMatrix.Forward : _currentGraph.WorldMatrix.Up;
                    double distanceToNext;
                    if (!_currentGraph.GetEdgeDistanceInDirection(checkDirection, out distanceToNext))
                      distanceToNext = VoxelGridMap.DefaultHalfSize - 3 * VoxelGridMap.DefaultCellSize;

                    midPoint = _currentGraph.OBB.Center + checkDirection * distanceToNext;
                    midPoint += checkDirection * (VoxelGridMap.DefaultHalfSize - 3 * VoxelGridMap.DefaultCellSize);
                  }
                }

                _nextGraph = AiSession.Instance.GetVoxelGraph(midPoint, WorldMatrix, returnFirstFound: !nextIsPrev);

                if (_nextGraph != null)
                {
                  if (_nextGraph.OBB.Contains(ref botPosition))
                  {
                    var localBot = _currentGraph.WorldToLocal(botPosition);
                    if (_currentGraph.GetClosestValidNode(this, localBot, out localBot))
                      _currentGraph.TryGetNodeForPosition(localBot, out tgtPoint);
                  }
                  else
                  {
                    tgtPoint = _currentGraph.GetBufferZoneTargetPositionFromPrunik(ref _nextGraph.OBB, ref direction, ref targetPosition, this);
                  }
                }
              }
            }
            else if (nextIsPrev)
            {
              switchNow = false;
              var direction = _currentGraph.WorldMatrix.GetClosestDirection(_nextGraph.OBB.Center - _currentGraph.OBB.Center);
              var midPoint = (_currentGraph.OBB.Center + _nextGraph.OBB.Center) * 0.5;
              _nextGraph = AiSession.Instance.GetVoxelGraph(midPoint, WorldMatrix, returnFirstFound: false);

              if (_nextGraph != null && !_nextGraph.OBB.Contains(ref botPosition))
                tgtPoint = _currentGraph.GetBufferZoneTargetPositionFromPrunik(ref _nextGraph.OBB, ref direction, ref targetPosition, this);
            }

            if (!switchNow)
            {
              if (tgtPoint == null || !NeedsTransition)
              {
                _nextGraph = null;
                CleanPath();
              }
              else
              {
                _transitionPoint = tgtPoint;
              }
            }
          }
          else if (NeedsTransition)
          {
            var intermediateNode = _currentGraph.WorldToLocal(intermediatePosition);
            if (_currentGraph.GetClosestValidNode(this, intermediateNode, out intermediateNode))
            {
              _currentGraph.TryGetNodeForPosition(intermediateNode, out _transitionPoint);
            }
          }
        }

        if (switchNow && _nextGraph != null)
        {
          AdjustPatrolPointsForNewGraph(_currentGraph, _nextGraph);

          _pathCollection?.OnGridBaseClosing();
          _previousGraph = _currentGraph;
          _currentGraph = _nextGraph;
          _nextGraph = null;
          _transitionPoint = null;
          NeedsTransition = false;
          _pathTimer = 101;

          if (_pathCollection != null)
          {
            _currentGraph.HookEventsForPathCollection(_pathCollection);
            _pathCollection.ClearObstacles();
            CleanPath();
          }
        }
      }
    }

    internal bool TrySwitchJetpack(bool enable)
    {
      var jetComp = Character?.Components?.Get<MyCharacterJetpackComponent>();
      if (jetComp != null)
      {
        enable &= MyAPIGateway.Session.SessionSettings.EnableJetpack;

        if (Owner != null)
          enable &= AiSession.Instance.ModSaveData.AllowHelpersToFly;
        else if (this is NeutralBotBase)
          enable &= AiSession.Instance.ModSaveData.AllowNeutralsToFly;
        else
          enable &= AiSession.Instance.ModSaveData.AllowEnemiesToFly;

        enable |= RequiresJetpack;

        if (jetComp.TurnedOn)
        {
          if (!enable)
            jetComp.TurnOnJetpack(false);
        }
        else if (enable)
          jetComp.TurnOnJetpack(true);

        JetpackEnabled = jetComp.TurnedOn;

        return true;
      }

      JetpackEnabled = false;
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
            _nextGraph = null;
            _pathCollection?.OnGridBaseClosing();
            _currentGraph = AiSession.Instance.GetVoxelGraph(BotInfo.CurrentBotPositionActual, WorldMatrix, true);

            if (_pathCollection != null)
            {
              CleanPath();
              _pathCollection.ClearObstacles();
              _currentGraph?.HookEventsForPathCollection(_pathCollection);
            }
          }

          return;
        }

        if (_pathCollection == null)
        {
          _pathCollection = AiSession.Instance.GetCollection();
          _pathCollection.Bot = this;
          _currentGraph.HookEventsForPathCollection(_pathCollection);
        }

        #region debugOnly
        if (AiSession.Instance.DrawDebug && _currentGraph.IsValid) // && Owner != null)
          _pathCollection.DrawFreeSpace(BotInfo.CurrentBotPositionActual, gotoPosition);
        #endregion

        if (CheckGraphNeeded)
        {
          StartCheckGraph(ref gotoPosition);
          return;
        }

        if (_currentGraph.Dirty)
        {
          CleanPath();
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

        if (CheckIfShouldWait())
        {
          _stuckTimer = 0;
          _stuckCounter = 0;
          return;
        }

        bool returnNow;
        if (CheckIfCloseEnoughToAct(ref actualPosition, ref gotoPosition, out returnNow))
        {
          CleanPath();

          if (Target.Override.HasValue)
          {
            if (actualPosition == Target.Override)
              Target.RemoveOverride(true);
          }

          if (!returnNow)
          {
            double checkDistance;
            var checkCube = Target.IsSlimBlock || Target.IsInventory || Target.IsCubeBlock;
            if (checkCube)
            {
              var slim = Target.IsInventory ? Target.Inventory : Target.Entity as IMySlimBlock;
              if (slim != null)
              {
                if (slim.FatBlock != null)
                {
                  checkDistance = slim.FatBlock.PositionComp.LocalAABB.HalfExtents.AbsMax() + 2f;
                }
                else
                {
                  BoundingBoxD box;
                  slim.GetWorldBoundingBox(out box, true);
                  checkDistance = (float)box.HalfExtents.AbsMax() + 2f;
                }
              }
              else
              {
                var cube = Target.Entity as IMyCubeBlock;
                if (cube != null)
                {
                  checkDistance = cube.PositionComp.LocalAABB.HalfExtents.AbsMax() + 2f;
                }
                else
                {
                  checkDistance = 2f;
                }
              }

              checkDistance *= checkDistance;
            }
            else if (Target.IsFriendly())
            {
              checkDistance = _followDistanceSqd;
            }
            else if (Target.IsFloater)
            {
              checkDistance = 8;
            }
            else if (IsLargeSpider)
            {
              checkDistance = 5;
            }
            else
            {
              checkDistance = 4;
            }

            MoveToPoint(gotoPosition, Target.Entity != null, checkDistance);
          }

          return;
        }

        var distanceToCheck = 0.5f;
        var targetLocal = _currentGraph.WorldToLocal(gotoPosition);
        bool targetMoved = (_lastEndLocal != Vector3I.Zero && _lastEndLocal != targetLocal)
          || (!Vector3D.IsZero(_lastEnd) && Vector3D.DistanceSquared(gotoPosition, _lastEnd) > 4);

        if (_pathCollection.Dirty && !_pathCollection.Locked)
        {
          CleanPath();
        }

        if (_pathCollection.HasNode)
        {
          CheckNode(ref distanceToCheck);
        }

        if (UseLadder || BotInfo.IsOnLadder || BotInfo.WasOnLadder)
        {
          _stuckTimer = 0;

          if (UseLadder)
          {
            bool forceUse = false;
            if (_pathCollection.HasNode && (NextIsLadder || (AfterNextIsLadder && _pathCollection.PathToTarget.Count > 0)))
            {
              var node = NextIsLadder ? _pathCollection.NextNode.Position : _pathCollection.PathToTarget.Peek().Position;
              var worldNode = _currentGraph.LocalToWorld(node);
              var rotated = Vector3D.Rotate(worldNode - BotInfo.CurrentBotPositionActual, MatrixD.Transpose(WorldMatrix));
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
          else if (BotInfo.WasOnLadder && BotInfo.IsFalling)
          {
            _stuckTimerReset = 0;
            NextIsLadder = false;
            AfterNextIsLadder = false;
            WaitForStuckTimer = false;
            CleanPath();
          }
        }

        if (!_pathCollection.HasNode && !UseLadder && BotInfo.IsOnLadder)
          PathFinderActive = false;

        bool hasPath = _pathCollection.HasPath;
        bool hasNode = _pathCollection.HasNode;
        bool hasNeither = !hasPath && !hasNode;

        if (BotMoved || targetMoved || hasNeither)
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
            if (hasNode || (NeedsTransition && _transitionPoint != null))
            {
              UpdatePathAndMove(ref distanceToCheck);
            }
            else if (hasPath && !UseLadder)
            {
              GetNextNodeAndMove(ref distanceToCheck);
            }
            else if (hasNeither && PatrolMode && Target.Override.HasValue)
            {
              if (Vector3D.Distance(Target.Override.Value, BotInfo.CurrentBotPositionAdjusted) < 2.5)
                UpdatePathAndMove(ref distanceToCheck, true);
            }
            else if (!hasPath && !_pathCollection.Locked && !Target.IsSlimBlock && (!NeedsTransition || _transitionPoint == null))
            {
              SimulateIdleMovement(false, Owner?.Character?.IsDead == false);
            }
            else if (!_pathCollection.HasNode && _sideNode.HasValue)
            {
              MoveToPoint(gotoPosition);
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
      bool counterOverTwo = _stuckCounter > 2;
      if (counterOverTwo || _pathCollection.UpdateDistanceToNextNode())
      {
        _stuckTimer = 0;
        _stuckCounter = 0;
        _sideNode = null;

        BotMoved = true;
        NextIsLadder = false;
        AfterNextIsLadder = false;
        UseLadder = false;

        if (counterOverTwo)
        {
          var current = _currentGraph.WorldToLocal(BotInfo.CurrentBotPositionActual);
          var lastNode = _pathCollection.LastNode;
          var nextNode = _pathCollection.NextNode;

          bool isGridGraph = _currentGraph.IsGridGraph;
          bool addTo = !isGridGraph;

          if (isGridGraph)
          {
            Node currentNode;
            addTo = lastNode?.IsGridNodePlanetTile == true || nextNode?.IsGridNodePlanetTile == true 
              || _currentGraph.TryGetNodeForPosition(current, out currentNode) && currentNode?.IsGridNodePlanetTile == true;
          }

          if (addTo && _pathCollection != null)
          {
            List<IHitInfo> hitInfoList;
            if (!AiSession.Instance.HitListStack.TryPop(out hitInfoList) || hitInfoList == null)
              hitInfoList = new List<IHitInfo>();
            else
              hitInfoList.Clear();

            var botPosition = GetPosition();
            MyAPIGateway.Physics.CastRay(botPosition, botPosition + WorldMatrix.Forward * 2, hitInfoList, CollisionLayers.CharacterCollisionLayer);

            for (int i = 0; i < hitInfoList.Count; i++)
            {
              var hitInfo = hitInfoList[i];
              if (hitInfo?.HitEntity == null || hitInfo.HitEntity.EntityId == Character.EntityId)
                continue;

              var ch = hitInfo.HitEntity as IMyCharacter;
              if (ch != null)
              {
                addTo = false;
                break;
              }
            }

            hitInfoList.Clear();
            AiSession.Instance.HitListStack.Push(hitInfoList);

            var last = lastNode != null ? lastNode.Position : current;
            var next = nextNode != null ? nextNode.Position : current;

            if (addTo && _currentGraph.AddToObstacles(last, current, next))
            {
              _pathCollection.Obstacles[next] = new byte();
            }
          }
        }

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

        var vector = worldNode - BotInfo.CurrentBotPositionAdjusted;
        var relVector = Vector3D.TransformNormal(vector, MatrixD.Transpose(WorldMatrix));
        var flattenedVector = new Vector3D(relVector.X, 0, relVector.Z);
        var flattenedLengthSquared = flattenedVector.LengthSquared();
        //var check = 1.1 * distanceToCheck;

        if (flattenedLengthSquared < distanceToCheck && Math.Abs(relVector.Y) < distanceToCheck)
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
      var botPosition = Character.WorldAABB.Center;

      Vector3I start = _currentGraph.WorldToLocal(botPosition);
      if (!_currentGraph.GetClosestValidNode(this, start, out start))
      {
        CleanPath();

        if (!BotInfo.IsJumping && ++_teleportCounter > 3)
        {
          _teleportCounter = 0;

          if (!ConfineToMap && !_currentGraph.IsPositionValid(botPosition))
          {
            StartCheckGraph(ref botPosition, true);
          }
          else if (!BotInfo.IsFalling || (BotInfo.CurrentGravityAtBotPosition_Nat.LengthSquared() <= 0 && BotInfo.CurrentGravityAtBotPosition_Art.LengthSquared() <= 0))
          {
            _currentGraph.TeleportNearby(this);
          }
        }

        return;
      }

      _teleportCounter = 0;
      var isSlimTarget = Target.IsSlimBlock;
      var isSlimBlock = isSlimTarget;

      Vector3D exit;
      Vector3I goal, adjustedGoal;
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

      if (!isSlimBlock && _currentGraph.IsGridGraph && !_currentGraph.IsOpenTile(goal))
      {
        var gridGraph = _currentGraph as CubeGridMap;
        var cube = gridGraph?.GetBlockAtPosition(goal);
        isSlimBlock = cube != null && !(cube.FatBlock is IMyTextPanel)
          && (cube.BlockDefinition as MyCubeBlockDefinition)?.HasPhysics == true
          && !AiSession.Instance.PassageBlockDefinitions.Contains(cube.BlockDefinition.Id) 
          && !AiSession.Instance.CatwalkBlockDefinitions.Contains(cube.BlockDefinition.Id)
          && !AiSession.Instance.RailingBlockDefinitions.ContainsItem(cube.BlockDefinition.Id)
          && !AiSession.Instance.FlatWindowDefinitions.ContainsItem(cube.BlockDefinition.Id)
          && cube.BlockDefinition.Id.SubtypeName != "LargeBlockKitchen"
          && !cube.BlockDefinition.Id.SubtypeName.StartsWith("LargeBlockBarCounter")
          && !cube.BlockDefinition.Id.SubtypeName.StartsWith("LargeBlockLocker")
          && !cube.BlockDefinition.Id.SubtypeName.EndsWith("PassageStairs_Large");
      }

      adjustedGoal = goal;
      Node goalNode;
      bool found = false;
      if (isCharacter && tgtCharacter.EntityId == Owner?.Character?.EntityId && _currentGraph.TryGetNodeForPosition(goal, out goalNode) && goalNode.IsAirNode)
      {
        var ownerPos = _currentGraph.WorldToLocal(Owner.Character.WorldAABB.Center);
        var isDenied = ownerPos == goal;

        found = _currentGraph.GetClosestValidNode(this, goalNode.Position, out adjustedGoal, WorldMatrix.Up, currentIsDenied: isDenied);
      }

      bool isInventory = Target.IsInventory;
      bool isFloater = Target.IsFloater;

      if (!found && !_currentGraph.GetClosestValidNode(this, goal, out adjustedGoal, isSlimBlock: isSlimBlock))
      {
        bool goBack = true;
        if (isCharacter)
        {
          var newGoal = _currentGraph.WorldToLocal(targetPosition);
          goBack = !_currentGraph.GetClosestValidNode(this, newGoal, out adjustedGoal);
        }

        if (goBack)
        {
          if (Target.Override.HasValue && exit == Target.Override)
            Target.RemoveOverride(false);

          bool markObstacle = false;
          bool allowObstacle = !_currentGraph.Dirty && !_currentGraph.Remake && !_currentGraph.NeedsBlockUpdate && !_currentGraph.NeedsVoxelUpdate;
          if (NeedsTransition)
          {
            if (allowObstacle)
              markObstacle = true;

            NeedsTransition = false;
            _transitionPoint = null;
          }

          if (isFloater || ((isSlimTarget || isInventory) && BlockIsOnSameGrid()))
          {
            var floater = Target.Entity as MyFloatingObject;
            Target.RemoveTarget();

            if (floater != null && this is RepairBot && Vector3D.DistanceSquared(botPosition, targetPosition) <= 10)
            {
              if (_lastFloater == null || _lastFloater.EntityId != floater.EntityId)
              {
                _floaterCounter = 0;
                _lastFloater = floater;
              }
              else if (++_floaterCounter > 3)
              {
                _floaterCounter = 0;
                var inv = Character.GetInventory() as MyInventory;
                if (inv != null && !inv.IsFull)
                {
                  var item = floater.Item;
                  var amount = MyFixedPoint.Min(item.Amount, inv.ComputeAmountThatFits(item.Content.GetId()));
                  if (amount > 0 && inv.AddItems(amount, item.Content))
                  {
                    floater.Close();
                    return;
                  }
                }
              }
            }

            if (allowObstacle)  
              markObstacle = true;
          }

          if (markObstacle)
          {
            _currentGraph.TempBlockedNodes[goal] = new byte();

            if (goal != adjustedGoal)
              _currentGraph.TempBlockedNodes[adjustedGoal] = new byte();
          }

          return;
        }
      }

      _lastFloater = null;
      _floaterCounter = 0;

      if (start == adjustedGoal)
      {
        if (isSlimTarget || isInventory || isFloater)
        {
          CleanPath();

          Vector3I position;
          Vector3 offset;
          MyCubeGrid grid = null;
          IMySlimBlock block = null;

          if (isSlimTarget || isInventory)
          {
            block = isSlimTarget ? Target.Entity as IMySlimBlock : Target.Inventory;
            grid = block.CubeGrid as MyCubeGrid;
            var worldPostion = grid.GridIntegerToWorld(block.Position);
            position = _currentGraph.WorldToLocal(worldPostion);

            if (grid.GridSizeEnum == MyCubeSize.Large)
              offset = Vector3.Zero;
            else
              offset = (Vector3)(worldPostion - _currentGraph.LocalToWorld(position));
          }
          else // isFloater
          {
            var floaterWorld = Target.CurrentGoToPosition;
            position = _currentGraph.WorldToLocal(floaterWorld);
            offset = (Vector3)(floaterWorld - _currentGraph.LocalToWorld(position));
          }

          lock (_pathCollection.PathToTarget)
          {
            TempNode temp;
            if (!AiSession.Instance.TempNodeStack.TryPop(out temp))
              temp = new TempNode();

            temp.Update(position, offset, null, NodeType.None, 0, grid, block);
            _pathCollection.PathToTarget.Enqueue(temp);
          }
        }

        return;
      }

      _lastEnd = targetPosition;
      _lastEndLocal = _currentGraph.WorldToLocal(targetPosition);
      _pathCollection.CleanUp();

      _pathWorkData.PathStart = start;
      _pathWorkData.PathEnd = adjustedGoal;
      _pathWorkData.IsIntendedGoal = true;
      _pathCollection.PathTimer.Restart();
      _task = MyAPIGateway.Parallel.StartBackground(_pathWorkAction, _pathWorkCallBack, _pathWorkData);

      // testing only
      //_pathCollection.PathTimer.Stop();
      //FindPath(_pathWorkData);
      //Reset();
    }

    bool BlockIsOnSameGrid()
    {
      if (_currentGraph == null || !_currentGraph.IsGridGraph)
        return false;

      var gridGraph = _currentGraph as CubeGridMap;
      var grid = gridGraph.MainGrid;
      if (grid == null)
        return false;

      var block = Target.Entity as IMyCubeBlock;
      var slim = block?.SlimBlock;
      if (slim == null)
        slim = Target.Entity as IMySlimBlock;

      return slim != null && slim.CubeGrid.IsSameConstructAs(grid);
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
        var distanceToTgtSqd = Vector3D.DistanceSquared(BotInfo.CurrentBotPositionActual, targetPosition);
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

            if (BotInfo.IsFalling)
            {
              CleanPath();
              return true;
            }

            if (!force)
            {
              needToWait = true;
              return false;
            }
          }
        }

        if (!BotInfo.IsOnLadder && (force || BotInfo.IsFalling))
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
      var vectorToLadder = adjustedLadderPosition - BotInfo.CurrentBotPositionAdjusted;
      var rotatedVector = Vector3D.Rotate(vectorToLadder, botMatrixT);

      if (force || BotInfo.IsFalling || (rotatedVector.Z < 0 && Math.Abs(rotatedVector.X) < 0.5))
      {
        if (UseObject != null && !BotInfo.IsOnLadder)
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

    internal void DismountLadder(Vector3D worldNode, Vector3D botPosition)
    {
      Character.Use();
      UseLadder = false;
      NextIsLadder = false;
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
          Character.SetPosition(worldNode + WorldMatrix.Down);
        }
      }
    }

    void UpdatePathAndMove(ref float distanceToCheck, bool patrol = false)
    {
      PathFinderActive = true;
      Vector3D worldNode;

      if (patrol)
      {
        worldNode = Target.Override.Value;
        distanceToCheck = 0.25f;
      }
      else
      {
        Node pNode = _pathCollection.NextNode ?? _transitionPoint;
        worldNode = _currentGraph.LocalToWorld(pNode.Position) + pNode.Offset;
      }

      var botPosition = BotInfo.CurrentBotPositionAdjusted;
      if (!NextIsLadder && BotInfo.IsOnLadder && BotInfo.GoingDownLadder)
      {
        DismountLadder(worldNode, botPosition);
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
      var botPosition = BotInfo.CurrentBotPositionActual;
      bool useLadderNow, findNewPath, wait, nextIsAirNode, nextIsLadder, afterNextIsLadder;
      _pathCollection.GetNextNode(botPosition, BotInfo.IsOnLadder, _transitionPoint != null,
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
          {
            if (BotInfo.CurrentGravityAtBotPosition_Nat.LengthSquared() > 0 || BotInfo.CurrentGravityAtBotPosition_Art.LengthSquared() > 0)
              TrySwitchJetpack(false);
          }
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

        var gridGraph = _currentGraph as CubeGridMap;
        var checkDoors = gridGraph != null && stuckTimer > 60;
        var botPosition = BotInfo.CurrentBotPositionActual;
        var botMatrix = WorldMatrix;

        List<IHitInfo> hitlist;
        if (!AiSession.Instance.HitListStack.TryPop(out hitlist) || hitlist == null)
          hitlist = new List<IHitInfo>();
        else
          hitlist.Clear();

        MyAPIGateway.Physics.CastRay(botPosition, botPosition + botMatrix.Forward * 3, hitlist, CollisionLayers.CharacterCollisionLayer);

        bool canUseDoors = !(this is NeutralBotBase) || AiSession.Instance.ModSaveData.AllowNeutralsToOpenDoors;
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

          if (!checkDoors || !canUseDoors)
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
        bool isPassageAirlock = !isRotaryAirlock && door.BlockDefinition.SubtypeName.StartsWith("PassageAirlock");

        if (!isRotaryAirlock && !isPassageAirlock && door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open)
        {
          //if (door.IsWorking)
          //  AiSession.Instance.DoorsToClose[door.EntityId] = MyAPIGateway.Session.ElapsedPlayTime;

          return true;
        }

        bool hasAccess = AiSession.Instance.EconomyGrids.Contains(door.CubeGrid.EntityId);

        if (!hasAccess)
        {
          var doorOwner = door.CubeGrid.BigOwners.Count > 0 ? door.CubeGrid.BigOwners[0] : door.CubeGrid.SmallOwners.Count > 0 ? door.CubeGrid.SmallOwners[0] : door.OwnerId;

          if (Owner != null)
          {
            var botOwner = Owner.IdentityId;
            var relation = MyIDModule.GetRelationPlayerPlayer(botOwner, doorOwner, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);

            hasAccess = ((MyDoorBase)door).AnyoneCanUse || relation != MyRelationsBetweenPlayers.Enemies || (doorOwner == 0L && door.HasPlayerAccess(BotIdentityId));
          }
          else
          {
            var botOwner = BotIdentityId;
            var relation = MyIDModule.GetRelationPlayerPlayer(botOwner, doorOwner, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);

            hasAccess = relation != MyRelationsBetweenPlayers.Enemies || (doorOwner == 0L && door.HasPlayerAccess(BotIdentityId));
          }
        }

        if (!hasAccess || this is CreatureBot)
        {
          if (Owner != null || !WantsTarget || !CanDamageGrid || this is NomadBot)
          {
            _pathCollection.DeniedDoors[door.Position] = door;
          }
          else if (!hasAccess)
          {
            // assume enemy, attack door!
            if (Target.Entity == null || !ReferenceEquals(Target.Entity, door))
            {
              _doorTgtCounter = 0;
              CleanPath();
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
            CleanPath();

            var node = gridGraph.OpenTileDict[localPoint];
            _pathCollection.PathToTarget.Enqueue(node);
          }

          return false;
        }
        else if (door.IsWorking)
        {
          if (isRotaryAirlock || isPassageAirlock)
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
                  if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed || door.IsFullyClosed)
                    return false;

                  if (door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Closing)
                    door.CloseDoor();
                }
                else if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open)
                {
                  return false;
                }
                else if (isPassageAirlock && ((MyAdvancedDoor)door).FullyOpen)
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

                if (isPassageAirlock && ((MyAdvancedDoor)door).FullyOpen)
                  return false;

                if (door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Opening)
                  door.OpenDoor();
              }
              else if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed || door.IsFullyClosed)
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
      var gravity = BotInfo.CurrentGravityAtBotPosition_Nat.LengthSquared() > 0 ? BotInfo.CurrentGravityAtBotPosition_Nat : BotInfo.CurrentGravityAtBotPosition_Art;

      float multiplier = 0;
      if (gravity.LengthSquared() > 0)
        multiplier = MathHelper.Clamp((float)gravity.Length() / 9.81f, 0, 2);

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
      if (_currentGraph?.Ready == true && PathFinderActive && !UseLadder && !BotInfo.IsOnLadder && !BotInfo.WasOnLadder)
      {
        bool swerve, doorFound;
        bool isGridGraph = _currentGraph.IsGridGraph;

        if (_stuckTimer > 120)
        {
          var current = _currentGraph.WorldToLocal(BotInfo.CurrentBotPositionActual);
          var lastNode = _pathCollection.LastNode;
          var nextNode = _pathCollection.NextNode;

          bool addTo = !isGridGraph;

          if (isGridGraph)
          {
            Node currentNode;
            addTo = lastNode?.IsGridNodePlanetTile == true || nextNode?.IsGridNodePlanetTile == true
              || _currentGraph.TryGetNodeForPosition(current, out currentNode) && currentNode?.IsGridNodePlanetTile == true;
          }

          if (addTo && _pathCollection != null)
          {
            List<IHitInfo> hitInfoList;
            if (!AiSession.Instance.HitListStack.TryPop(out hitInfoList) || hitInfoList == null)
              hitInfoList = new List<IHitInfo>();
            else
              hitInfoList.Clear();

            var botPosition = GetPosition();
            MyAPIGateway.Physics.CastRay(botPosition, botPosition + WorldMatrix.Forward * 2, hitInfoList, CollisionLayers.CharacterCollisionLayer);

            for (int i = 0; i < hitInfoList.Count; i++)
            {
              var hitInfo = hitInfoList[i];
              if (hitInfo?.HitEntity == null || hitInfo.HitEntity.EntityId == Character.EntityId)
                continue;

              var ch = hitInfo.HitEntity as IMyCharacter;
              if (ch != null)
              {
                addTo = false;
                break;
              }
            }

            hitInfoList.Clear();
            AiSession.Instance.HitListStack.Push(hitInfoList);

            var last = lastNode != null ? lastNode.Position : current;
            var next = nextNode != null ? nextNode.Position : current;

            if (addTo && _currentGraph.AddToObstacles(last, current, next))
            {
              _pathCollection.Obstacles[next] = new byte();
              CleanPath();
            }

            return;
          }
        }

        if (_stuckTimer > 180)
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

    internal virtual void SimulateIdleMovement(bool getMoving, bool towardOwner = false, double distanceCheck = 3)
    {
      if (PatrolMode || FollowMode || !AllowIdleMovement)
        return;

      if (Target != null && Target.HasTarget && _pathCollection != null && (_pathCollection.HasNode || _pathCollection.HasPath))
        return;

      _sideNode = null;

      var botPosition = BotInfo.CurrentBotPositionActual;
      var botMatrix = WorldMatrix;

      if (towardOwner)
      {
        _moveTo = null;
        _moveToNode = null;

        _idleTimer = 0;
        _ticksSinceLastIdleTransition = 0;
      }
      else if (_moveTo.HasValue) 
      {
        if (_moveToNode != null && _currentGraph != null)
        {
          _moveTo = _currentGraph.LocalToWorld(_moveToNode.Position) + _moveToNode.Offset;
        }

        var vector = Vector3D.TransformNormal(_moveTo.Value - botPosition, Matrix.Transpose(botMatrix));
        var flattenedVector = new Vector3D(vector.X, 0, vector.Z);

        if (flattenedVector.LengthSquared() <= distanceCheck)
        {
          _moveTo = null;
          _moveToNode = null;
        }
        else if (_prevMoveTo.HasValue)
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
            if (_idleTimer >= 180)
            {
              _moveTo = null;
              _moveToNode = null;
              CleanPath();
            }
          }
        }
      }

      var graphReady = _currentGraph?.Ready == true && _currentGraph.IsValid;
      if (graphReady && _pathCollection == null)
      {
        _pathCollection = AiSession.Instance.GetCollection();
        _pathCollection.Bot = this;
        _currentGraph.HookEventsForPathCollection(_pathCollection);
      }

      //if (AiSession.Instance.DrawDebug && _pathCollection != null)
      //{
      //  var start = Position;
      //  var end = _moveTo ?? start;
      //  _pathCollection.DrawFreeSpace(start, end);
      //}

      if (_moveTo == null)
      {
        _moveToNode = null;

        if (towardOwner)
        {
          var pos = Owner.Character.WorldAABB.Center;
          if (Vector3D.DistanceSquared(botPosition, pos) <= 25)
          {
            CleanPath();

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
            {
              _moveTo = _currentGraph.LocalToWorld(localOwner);

              Node n;
              if (_currentGraph.TryGetNodeForPosition(localOwner, out n) && n != null)
              {
                _moveToNode = n;
                _moveTo = _moveTo.Value + n.Offset;
              }
            }
            else
            {
              _moveToNode = null;
              _moveTo = null;
            }
          }
          else
          {
            _moveTo = pos;
          }
        }

        if (_moveTo == null)
        {
          _moveToNode = null;

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
                    _moveToNode = _transitionPoint;
                  }

                  StartCheckGraph(ref pos);
                }
                else if (Owner == null && !_transitionIdle && CanTransitionMaps && AiSession.Instance.ModSaveData.AllowIdleMapTransitions)
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
                  _moveToNode = moveNode;
                }
              }
            }
            else
            {
              _moveTo = botPosition + direction * MyUtils.GetRandomInt(10, 26);
              _moveToNode = null;
            }
          }
          else
            _stuckCounter = _stuckTimer = 0;
        }

        _prevMoveTo = botPosition;
        _idleTimer = 0;

        if (_moveTo == null)
        {
          _moveToNode = null;
          return;
        }
      }

      if (graphReady)
      {
        if (AiSession.Instance.DrawDebug)
          _pathCollection?.DrawFreeSpace(botPosition, _moveTo.Value);

        if (getMoving)
        {
          var distanceToCheck = (BotInfo.IsRunning || BotInfo.IsFlying) ? 1 : 0.5f;
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
        MoveToPoint(_moveTo.Value, distanceCheck: Math.Min(1, distanceCheck));
    }

    internal Vector3D GetClearTravelDirection()
    {
      var botMatrix = WorldMatrix;
      var botPosition = BotInfo.CurrentBotPositionAdjusted;

      IHitInfo hit;
      var direction = botMatrix.Backward;
      var checkPosition = botPosition + direction * 5;

      if (!MyAPIGateway.Physics.CastRay(botPosition + direction * 0.25, checkPosition, out hit, CollisionLayers.CharacterCollisionLayer))
        return direction;

      direction = botMatrix.Backward + botMatrix.Left;
      checkPosition = botPosition + direction * 5;

      if (!MyAPIGateway.Physics.CastRay(botPosition + direction * 0.25, checkPosition, out hit, CollisionLayers.CharacterCollisionLayer))
        return direction;

      direction = botMatrix.Left;
      checkPosition = botPosition + direction * 5;

      if (!MyAPIGateway.Physics.CastRay(botPosition + direction * 0.25, checkPosition, out hit, CollisionLayers.CharacterCollisionLayer))
        return direction;

      direction = botMatrix.Left + botMatrix.Forward;
      checkPosition = botPosition + direction * 5;

      if (!MyAPIGateway.Physics.CastRay(botPosition + direction * 0.25, checkPosition, out hit, CollisionLayers.CharacterCollisionLayer))
        return direction;

      direction = botMatrix.Forward;
      checkPosition = botPosition + direction * 5;

      if (!MyAPIGateway.Physics.CastRay(botPosition + direction * 0.25, checkPosition, out hit, CollisionLayers.CharacterCollisionLayer))
        return direction;

      direction = botMatrix.Forward + botMatrix.Right;
      checkPosition = botPosition + direction * 5;

      if (!MyAPIGateway.Physics.CastRay(botPosition + direction * 0.25, checkPosition, out hit, CollisionLayers.CharacterCollisionLayer))
        return direction;

      direction = botMatrix.Right;
      checkPosition = botPosition + direction * 5;

      if (!MyAPIGateway.Physics.CastRay(botPosition + direction * 0.25, checkPosition, out hit, CollisionLayers.CharacterCollisionLayer))
        return direction;

      return botMatrix.Right + botMatrix.Backward;
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

    internal bool RepairBlockManually(IMySlimBlock block, IMyProjector projector, Dictionary<string, int> missingComps, List<MyEntity> entList, BuildBotToolInfo toolInfo)
    {
      var inv = Character?.GetInventory();
      if (inv == null || block == null)
        return false;

      var isProjection = projector?.CanBuild(block, true) == BuildCheckResult.OK;
      if (!isProjection)
      {
        var gridGraph = _currentGraph as CubeGridMap;
        if (gridGraph != null)
        {
          missingComps.Clear();
          block.GetMissingComponents(missingComps);

          if (missingComps.Count == 0 && !block.HasDeformation && block.GetBlockHealth(entList) >= 1)
            return false;
        }
      }
      else if (projector != null)
      {
        projector.Build(block, Owner.IdentityId, Owner.Character.EntityId, false, Owner.IdentityId);

        var cubeDef = block.BlockDefinition as MyCubeBlockDefinition;
        var compDef = cubeDef.Components[0].Definition.Id;
        var obj = MyObjectBuilderSerializer.CreateNewObject(compDef.TypeId, compDef.SubtypeName) as MyObjectBuilder_PhysicalObject;
        inv.RemoveItemsOfType(1, obj);

        Vector3? ignoreColor = null;
        Vector3? grindColor = null;
        var modData = AiSession.Instance.ModSaveData.PlayerHelperData;
        for (int i = 0; i < modData.Count; i++)
        {
          var data = modData[i];
          if (data.OwnerIdentityId == Owner.IdentityId)
          {
            ignoreColor = data.RepairBotIgnoreColorMask;
            grindColor = data.RepairBotGrindColorMask;
            break;
          }
        }

        if (ignoreColor.HasValue || grindColor.HasValue)
        {
          var color = MyColorPickerConstants.HSVOffsetToHSV(block.ColorMaskHSV) * new Vector3(360, 100, 100);
          if (ignoreColor.HasValue && Vector3.IsZero(ignoreColor.Value - color, 1E-2f))
            return false;
          else if (grindColor.HasValue && Vector3.IsZero(grindColor.Value - color, 1E-2f))
            return false;
        }
      }
      else
      {
        return false;
      }

      if (inv.ItemCount > 0)
        block.MoveItemsToConstructionStockpile(inv);

      var weldAmount = toolInfo.WeldAmount;
      var boneFixAmount = toolInfo.BoneFixAmount;

      if (AiSession.Instance.ModSaveData.ObeyProjectionIntegrityForRepairs)
      {
        var realGrid = block.CubeGrid as MyCubeGrid;
        if (realGrid?.Projector == null)
        {
          entList.Clear();
          var worldPosition = block.CubeGrid.GridIntegerToWorld(block.Position);
          var sphere = new BoundingSphereD(worldPosition, block.CubeGrid.GridSize * 0.5);
          MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entList);

          for (int i = 0; i < entList.Count; i++)
          {
            var projGrid = entList[i] as MyCubeGrid;
            if (projGrid?.Projector != null)
            {
              var projectedPosition = projGrid.WorldToGridInteger(worldPosition);
              var projectedBlock = projGrid.GetCubeBlock(projectedPosition) as IMySlimBlock;

              var blockDef = (MyCubeBlockDefinition)block.BlockDefinition;
              var weldIntegrityAmount = weldAmount / blockDef.IntegrityPointsPerSec;
              if (projectedBlock?.BlockDefinition.Id == blockDef.Id && block.BuildIntegrity + weldIntegrityAmount > projectedBlock.BuildIntegrity)
              {
                weldAmount = projectedBlock.BuildIntegrity - block.BuildIntegrity;
                break;
              }
            }
          }

          if (weldAmount <= 0)
            return false;
        }
      }

      block.IncreaseMountLevel(weldAmount, BotIdentityId, null, boneFixAmount);

      if (isProjection)
      {
        var projGrid = block.CubeGrid as MyCubeGrid;
        var realGrid = projector.CubeGrid as MyCubeGrid;

        if (projGrid != null && realGrid != null)
        {
          var localToWorld = projGrid.GridIntegerToWorld(block.Position);
          var worldToLocal = realGrid.WorldToGridInteger(localToWorld);

          var newBlock = realGrid.GetCubeBlock(worldToLocal) as IMySlimBlock;
          if (newBlock != null)
            Target.SetTarget(Owner, newBlock);
        }
      }

      return true;
    }

    internal bool GrindBlockManually(IMySlimBlock block, BuildBotToolInfo toolInfo)
    {
      var inv = Character?.GetInventory();
      if (inv == null || block == null)
        return false;

      block.DecreaseMountLevel(toolInfo.GrindAmount, inv);
      block.MoveItemsFromConstructionStockpile(inv);

      if (block.IsDestroyed && (block.StockpileEmpty || Owner == null))
      {
        block.SpawnConstructionStockpile();
        block.CubeGrid.RazeBlock(block.Min);
      }

      return true;
    }

    internal void CheckBlockTarget(ref bool isTgt, ref bool shouldAttack, ref Vector3 movement, ref Vector2 rotation, ref double distanceCheck)
    {
      bool checkSlim = ToolDefinition != null && ToolDefinition.WeaponType == MyItemWeaponType.None;
      bool slimTgt = Target.IsSlimBlock || Target.IsCubeBlock;

      if (checkSlim && slimTgt)
      {
        var directToBlock = isTgt && slimTgt;
        bool ignoreRotation = false;

        var botPosition = BotInfo.CurrentBotPositionAdjusted;
        var actualPosition = Target.CurrentActualPosition;
        var distance = Vector3D.DistanceSquared(actualPosition, botPosition);

        var cube = Target.Entity as IMyCubeBlock;
        var slim = cube?.SlimBlock;

        if (slim == null)
          slim = Target.Entity as IMySlimBlock;

        if (_currentGraph.IsGridGraph)
        {
          if (directToBlock)
          {
            if (movement.Z != 0 || movement.Y != 0)
            {
              if (cube != null)
              {
                distanceCheck = cube.PositionComp.LocalAABB.HalfExtents.AbsMax() + 4f;
                distanceCheck *= distanceCheck;
              }
              else if (slim != null)
              {
                if (slim.FatBlock != null)
                {
                  distanceCheck = slim.FatBlock.PositionComp.LocalAABB.HalfExtents.AbsMax() + 4f;
                  distanceCheck *= distanceCheck;
                }
                else
                {
                  BoundingBoxD box;
                  slim.GetWorldBoundingBox(out box, true);
                  distanceCheck = (float)box.HalfExtents.AbsMax() + 4f;
                  distanceCheck *= distanceCheck;
                }
              }
              else
              {
                distanceCheck = 10;
              }

              if (distance <= distanceCheck)
              {
                movement.Y = 0;
                movement.Z = 0;
              }
            }
            else if (rotation.Y != 0)
            {
              var graph = _currentGraph as CubeGridMap;
              var localTgt = graph.WorldToLocal(actualPosition);
              var localBot = graph.WorldToLocal(botPosition);
              var dMan = Vector3I.DistanceManhattan(localBot, localTgt);

              if (dMan < 2)
              {
                var diff = localTgt - localBot;
                var upDir = graph.MainGrid.WorldMatrix.GetClosestDirection(Character.WorldMatrix.Up);
                var botUp = Base6Directions.GetIntVector(upDir);

                if (botUp.Dot(ref diff) != 0)
                  rotation = Vector2.Zero;
              }
              else if (dMan < 3)
              {
                ignoreRotation = true;
              }
            }
          }
        }
        else if (movement.Z != 0 || movement.Y != 0)
        {
          distanceCheck = 10;
          if (distance <= distanceCheck)
          {
            movement.Y = 0;
            movement.Z = 0;
          }
        }

        var notMoving = Vector3.IsZero(ref movement);
        var notRotating = ignoreRotation || Vector2.IsZero(ref rotation);

        if (notMoving && notRotating && distance <= distanceCheck)
        {
          shouldAttack = false;

          if (slim != null)
          {
            if (((byte)MySessionComponentSafeZones.AllowedActions & 16) != 0)
            {
              if (_particleLocation.HasValue)
              {
                var localParticle = slim.CubeGrid.WorldToGridInteger(_particleLocation.Value);
                if (localParticle != slim.Position)
                {
                  _particleLocation = null;
                  _particlePacketSent = false;
                  var pType = CurrentBuildMode == BuildMode.Weld ? ParticleInfoBase.ParticleType.Weld : ParticleInfoBase.ParticleType.Grind;
                  var packet = new ParticlePacket(Character.EntityId, pType, remove: true);

                  if (MyAPIGateway.Session.Player != null)
                    packet.Received(AiSession.Instance.Network);

                  if (AiSession.Instance.IsServer && MyAPIGateway.Multiplayer.MultiplayerActive)
                    AiSession.Instance.Network.RelayToClients(packet);
                }
              }

              if (GrindBlockManually(slim, _toolInfo))
              {
                ParticlePacket pkt;
                if (!_particlePacketSent && !BugZapped)
                {
                  _particleLocation = slim.CubeGrid.GridIntegerToWorld(slim.Position);
                  _particlePacketSent = true;
                  var terminal = slim?.FatBlock as IMyTerminalBlock;
                  pkt = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Grind, terminal?.EntityId ?? 0L, slim.CubeGrid.EntityId, slim.Position, true);

                  if (MyAPIGateway.Session.Player != null)
                    pkt.Received(AiSession.Instance.Network);

                  if (MyAPIGateway.Multiplayer.MultiplayerActive)
                    AiSession.Instance.Network.RelayToClients(pkt);
                }
              }
            }
          }
        }
        else
        {
          if (CurrentBuildMode == BuildMode.Grind && Target.IsSlimBlock && Vector3D.DistanceSquared(actualPosition, botPosition) <= 10)
          {
            slim?.FixBones(0, float.MaxValue);
          }

          if (!notMoving && _particlePacketSent)
          {
            _particleLocation = null;
            _particlePacketSent = false;
            var pType = CurrentBuildMode == BuildMode.Weld ? ParticleInfoBase.ParticleType.Weld : ParticleInfoBase.ParticleType.Grind;
            var packet = new ParticlePacket(Character.EntityId, pType, remove: true);

            if (MyAPIGateway.Session.Player != null)
              packet.Received(AiSession.Instance.Network);

            if (AiSession.Instance.IsServer && MyAPIGateway.Multiplayer.MultiplayerActive)
              AiSession.Instance.Network.RelayToClients(packet);
          }
        }
      }
      else if (_particlePacketSent)
      {
        _particleLocation = null;
        _particlePacketSent = false;
        var packet = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Weld, remove: true);

        if (MyAPIGateway.Session.Player != null)
          packet.Received(AiSession.Instance.Network);

        if (AiSession.Instance.IsServer && MyAPIGateway.Multiplayer.MultiplayerActive)
          AiSession.Instance.Network.RelayToClients(packet);
      }
    }

    bool _waiting;

    bool CheckIfShouldWait()
    {
      if (Owner?.Character == null || Owner.Character.IsDead || Target.Entity != Owner.Character)
      {
        _waiting = false;
        return false;
      }

      var ch = Owner.Character;
      var position = Character.WorldAABB.Center;
      var playerPosition = ch.WorldAABB.Center;

      var vector = playerPosition - position;
      var relativeVector = Vector3D.Rotate(vector, MatrixD.Transpose(Character.WorldMatrix));

      if (Math.Abs(relativeVector.Y) > 3 || ch.WorldMatrix.Forward.Dot(vector) > 0 || vector.LengthSquared() > 10)
      {
        _waiting = false;
        return false;
      }

      IHitInfo hit;
      var direction = Vector3D.Normalize(vector);
      MyAPIGateway.Physics.CastRay(position + direction * 0.25, ch.WorldAABB.Center, out hit);

      var hitEnt = hit?.HitEntity as IMyCharacter;
      if (hitEnt == null || hitEnt.EntityId != ch.EntityId)
      {
        _waiting = false;
        return false;
      }

      if (!_waiting)
      {
        _waiting = true;

        var rand = MyUtils.GetRandomInt(AiSession.Instance.HelperAnimations.Count);
        var action = AiSession.Instance.HelperAnimations[rand];
        Behavior.Perform(action);
        _performanceTimer = 0;
        _performanceTimerTarget = action == "CheckWrist" ? 600u : action == "Stretching" ? 500u : 300u;
      }

      var projUp = AiUtils.Project(vector, Character.WorldMatrix.Up);
      var reject = vector - projUp;
      var angleRads = AiUtils.GetAngleBetween(Character.WorldMatrix.Forward, reject);

      if (relativeVector.Z > 0 || Math.Abs(angleRads) > MathHelper.ToRadians(3))
      {
        var rotation = new Vector2(0, (float)angleRads * Math.Sign(relativeVector.X) * 25);
        Character.MoveAndRotate(Vector3.Zero, rotation, 0);
      }

      return true;
    }
  }
}
