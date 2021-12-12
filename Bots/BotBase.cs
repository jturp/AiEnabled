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
using AiEnabled.ModFiles.Parallel;

namespace AiEnabled.Bots
{
  public abstract partial class BotBase
  {
    public IMyPlayer Owner;
    public IMyCharacter Character;
    public TargetInfo Target;
    public bool HasWeaponOrTool, HasLineOfSight, UseAPITargets;
    public float DamageModifier;
    public int TicksBetweenProjectiles = 10;
    public string ToolSubtype;

    internal float _minDamage, _maxDamage, _followDistanceSqd = 10;
    internal float _blockDamagePerAttack, _blockDamagePerSecond = 100;
    internal Vector3D? _prevMoveTo, _moveTo, _sideNode;
    internal GridBase _currentGraph, _nextGraph;
    internal PathCollection _pathCollection;
    internal Node _transitionPoint;
    internal BotState _botState;
    internal IMyUseObject UseObject;
    internal Task _task;
    internal Vector3D _lastEnd;
    internal Vector3I _lastCurrent, _lastPrevious, _lastEndLocal;
    internal int _stuckCounter, _stuckTimer, _stuckTimerReset;
    internal int _tickCount, _xMoveTimer, _noPathCounter, _doorTgtCounter;
    internal uint _pathTimer, _idleTimer, _lowHealthTimer = 1800;
    internal uint _ticksSinceFoundTarget, _damageTicks;
    internal uint _ticksBeforeDamage = 35;
    internal uint _ticksBetweenAttacks = 300;
    internal uint _ticksSinceLastAttack = 1000;
    internal uint _ticksSinceLastDismount = 1000;
    internal bool _switchWalk, _damagePending, _behaviorReady;
    internal bool _pathFinderActive, _botMoved, _usePathFinder;
    internal bool _nextIsLadder, _afterNextIsLadder, _useLadder;
    internal bool _needsTransition, _idleMovementActive, _isShooting;
    internal bool _waitForLOSTimer, _waitForStuckTimer, _waitForSwerveTimer, _checkGraph;
    internal bool _canUseSpaceNodes, _canUseAirNodes, _canUseWaterNodes;
    internal bool _canUseLadders, _canUseSeats;
    internal bool _groundNodesFirst, _waterNodesOnly, _requiresJetpack, _jetpackEnabled;
    internal bool _enableDespawnTimer = true;
    internal bool _taskComplete = true;
    internal bool _wantsTarget = true;
    internal List<MySoundPair> _attackSounds;
    internal List<string> _attackSoundStrings;
    internal MySoundPair _deathSound;
    internal string _deathSoundString;
    internal MyObjectBuilder_ConsumableItem _energyOB;
    internal BotBehavior Behavior;
    internal HashSet<IMyCubeGrid> _gridGroups1 = new HashSet<IMyCubeGrid>();
    internal HashSet<IMyCubeGrid> _gridGroups2 = new HashSet<IMyCubeGrid>();
    internal HashSet<IMyCubeGrid> _gridGroups3 = new HashSet<IMyCubeGrid>();
    internal List<IMyLargeTurretBase> _gridTurrets = new List<IMyLargeTurretBase>();
    internal HashSet<long> _checkedGridIDs = new HashSet<long>();
    internal List<MyEntity> _entities = new List<MyEntity>();
    internal List<MyLineSegmentOverlapResult<MyEntity>> _rayEntities = new List<MyLineSegmentOverlapResult<MyEntity>>();

    HashSet<Vector3I> _tempNodes = new HashSet<Vector3I>();
    List<Vector3I> _lineList = new List<Vector3I>();
    List<IHitInfo> _hitList = new List<IHitInfo>();
    List<IHitInfo> _hitList2 = new List<IHitInfo>();
    bool _awaitingCallBack;
    Task _graphTask;

    Action<WorkData> _graphWorkAction, _graphWorkCallBack;
    Action<WorkData> _pathWorkAction, _pathWorkCallBack;
    GraphWorkData _graphWorkData = new GraphWorkData();
    PathWorkData _pathWorkData = new PathWorkData();

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

        TicksBetweenProjectiles = (int)Math.Ceiling(gun.GunBase.ShootIntervalInMiliseconds / 16.667f);
        DamageModifier = multiplier * 3;
      }
    }

    public BotBase(IMyCharacter bot, float minDamage, float maxDamage, GridBase gridBase)
    {
      Character = bot;
      Target = new TargetInfo(this);
      _botState = new BotState(this);
      _currentGraph = gridBase;
      _usePathFinder = gridBase != null;
      _minDamage = minDamage;
      _maxDamage = maxDamage + 1;
      _energyOB = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_ConsumableItem>("Powerkit");

      Character.OnClosing += Character_OnClosing;
      Character.CharacterDied += Character_CharacterDied;

      _graphWorkAction = new Action<WorkData>(CheckGraph);
      _graphWorkCallBack = new Action<WorkData>(CheckGraphComplete);
      _pathWorkAction = new Action<WorkData>(FindPath);
      _pathWorkCallBack = new Action<WorkData>(FindPathCallBack);
    }

    private void Character_CharacterDied(IMyCharacter bot)
    {
      IsDead = true;

      var inventory = bot?.GetInventory() as MyInventory;
      if (inventory != null)
      {
        var botDef = MyDefinitionId.Parse("MyObjectBuilder_AnimalBot/" + bot.Definition.Id.SubtypeName);
        var agentDef = MyDefinitionManager.Static.GetBotDefinition(botDef) as MyAgentDefinition;
        var lootContainer = agentDef?.InventoryContainerTypeId.SubtypeName ?? "DroidLoot";
        var container = MyDefinitionManager.Static.GetContainerTypeDefinition(lootContainer);

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
      IsDead = true;
      CleanUp();
    }

    internal virtual void Close(bool cleanConfig = false)
    {
      CleanUp(cleanConfig);
      Character?.Close();
    }

    internal virtual void CleanUp(bool cleanConfig = false)
    {
      if (Character != null)
      {
        Character.OnClosing -= Character_OnClosing;
        Character.CharacterDied -= Character_CharacterDied;

        if (Owner != null)
        {
          MyVisualScriptLogicProvider.SetHighlight(Character.Name, false, playerId: Owner.IdentityId);

          if (!MyAPIGateway.Utilities.IsDedicated && AiSession.Instance.CommandMenu?.ActiveBot?.EntityId == Character.EntityId)
          {
            AiSession.Instance.CommandMenu.CloseMenu();
            AiSession.Instance.CommandMenu.CloseInteractMessage();
            AiSession.Instance.CommandMenu.UpdateBlacklist(true);
          }

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

          if (this is RepairBot)
          {
            if (Target.IsSlimBlock)
            {
              var packet2 = new ParticlePacket(Character.EntityId, Particles.ParticleInfoBase.ParticleType.Builder, remove: true);
              packet.Received(AiSession.Instance.Network);

              if (AiSession.Instance.IsServer && MyAPIGateway.Multiplayer.MultiplayerActive)
                AiSession.Instance.Network.RelayToClients(packet2);
            }
          }
        }

        AiSession.Instance.RemoveBot(Character.EntityId, Owner?.IdentityId ?? 0L, cleanConfig);
      }

      if (_pathCollection != null)
        AiSession.Instance.ReturnCollection(_pathCollection);

      Target?.Close();
      Behavior?.Close();
      _lineList?.Clear();
      _entities?.Clear();
      _hitList?.Clear();
      _hitList2?.Clear();
      _rayEntities?.Clear();
      _gridGroups1?.Clear();
      _gridGroups2?.Clear();
      _gridGroups3?.Clear();
      _gridTurrets?.Clear();
      _checkedGridIDs?.Clear();

      Target = null;
      Behavior = null;
      _currentGraph = null;
      _pathCollection = null;
      _lineList = null;
      _entities = null;
      _hitList = null;
      _hitList2 = null;
      _rayEntities = null;
      _gridGroups1 = null;
      _gridGroups2 = null;
      _gridGroups3 = null;
      _gridTurrets = null;
      _checkedGridIDs = null;
      _pathWorkData = null;
      _pathWorkAction = null;
      _pathWorkCallBack = null;
      _graphWorkAction = null;
      _graphWorkCallBack = null;
      _graphWorkData = null;
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
      _damagePending = true;

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

    public bool IsDead { get; private set; }
    public Vector3D Position => Character != null ? Character.WorldAABB.Center : Vector3D.Zero;
    public MatrixD WorldMatrix => Character?.WorldMatrix ?? MatrixD.Identity;
    internal void GiveControl(IMyEntityController controller) => controller.TakeControl(Character);

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

        if (_currentGraph.IsGridGraph)
        {
          var gridGraph = _currentGraph as CubeGridMap;

          var ownerPos = Owner?.Character?.WorldAABB.Center ?? Vector3D.Zero;
          var localOwner = gridGraph.WorldToLocal(ownerPos);

          if (Owner?.Character == null || !gridGraph.OpenTileDict.TryGetValue(localOwner, out pn))
          {
            worldPos = gridGraph.GetLastValidNodeOnLine(Position, direction, distance, false);
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
          var pos = Position + direction * distance;

          if (!_currentGraph.GetRandomOpenNode(this, pos, out pn))
          {
            var point = _currentGraph.GetClosestSurfacePointFast(this, pos, WorldMatrix.Up);
            var localPoint = _currentGraph.WorldToLocal(point);
            pn = new Node(localPoint, null, null, point);
          }
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

      Behavior?.Update();

      if (Character != null)
      {
        Character.Flags &= ~EntityFlags.NeedsUpdate100;

        var jetComp = Character.Components?.Get<MyCharacterJetpackComponent>();
        _jetpackEnabled = jetComp?.TurnedOn ?? false;

        if (_jetpackEnabled)
          SetVelocity();

        _botState.UpdateBotState();
      }

      if (_enableDespawnTimer && _ticksSinceFoundTarget > 15000)
      {
        if (UseAPITargets)
        {
          _ticksSinceFoundTarget = 0;
        }
        else
        {
          Close();
          return false;
        }
      }

      bool inSeat = Character?.Parent is IMyCockpit;
      bool collectionOK = _pathCollection != null;

      if (collectionOK)
        _pathCollection.IdlePathTimer++;

      if (_currentGraph != null)
      {
        _currentGraph.LastActiveTicks = 0;
        _currentGraph.IsActive = true;

        if (!inSeat && _currentGraph.Ready && !_useLadder && !_botState.IsOnLadder && !_botState.WasOnLadder
          && collectionOK && (_pathCollection.HasPath || _pathCollection.HasNode))
          ++_stuckTimer;
      }

      bool isTick100 = _tickCount % 100 == 0;
      bool checkAll100 = !inSeat && isTick100;
      bool tgtDead = Target.IsDestroyed();
      if (checkAll100 || tgtDead)
      {
        if (tgtDead)
          _sideNode = null;

        if (HasWeaponOrTool)
        {
          var tool = Character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
          if (tool == null)
          {
            var charController = Character as Sandbox.Game.Entities.IMyControllableEntity;
            charController?.SwitchToWeapon(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), ToolSubtype));
          }
        }

        if (!UseAPITargets)
          SetTarget();

        if (checkAll100)
        {
          _checkGraph = true;

          if (Owner?.Character != null)
          {
            if (Owner.Character.EnabledLights != Character.EnabledLights)
              Character.SwitchLights();

            if (_canUseSeats && !UseAPITargets && Owner.Character.Parent is IMyCockpit)
              AiSession.Instance.PlayerEnteredCockpit(null, Owner.IdentityId, null);
          }
          else if (Character.EnabledLights)
            Character.SwitchLights();
        }
      }

      if (_tickCount % 150 == 0)
      {
        _switchWalk = !inSeat;

        bool lowHealth = false;
        if (Owner != null && _lowHealthTimer > 1800 && !(this is ScavengerBot))
        {
          var statComp = Character.Components?.Get<MyEntityStatComponent>() as MyCharacterStatComponent;
          var healthRatio = statComp?.HealthRatio ?? 1f;
          lowHealth = healthRatio < 0.25f;
        }

        if (_behaviorReady)
        {
          if (lowHealth)
          {
            _behaviorReady = false;
            _lowHealthTimer = 0;
            AiSession.Instance.GlobalSpeakTimer = 0;
            Behavior.Speak("CriticalBatteries");
          }
          else if (this is NomadBot)
          {
            _behaviorReady = false;
            UseBehavior();
          }
          else if (AiSession.Instance?.GlobalSpeakTimer > 1000 && (inSeat || (Target.Entity != null && Target.GetDistanceSquared() < 2500 && !Target.IsFriendly())))
          {
            AiSession.Instance.GlobalSpeakTimer = 0;
            _behaviorReady = false;

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
        _behaviorReady = true;

      if (_damagePending)
        UpdateDamagePending();

      return true;
    }

    internal virtual void UseBehavior()
    {
      Behavior?.Speak();
    }

    void SetVelocity()
    {
      var maxVelocity = 4f;

      if (_currentGraph != null)
      {
        var desiredVelocity = _currentGraph.Planet == null ? 25f : 10f;
        var ch = Target.Entity as IMyCharacter;
        bool isOwner = ch != null && ch.EntityId == Owner?.Character?.EntityId;

        if (_pathCollection != null && _pathCollection.HasNode)
        {
          var pathNode = _pathCollection.NextNode;
          var tgtPos = pathNode.SurfacePosition ?? _currentGraph.LocalToWorld(pathNode.Position);

          var distanceTo = Vector3D.DistanceSquared(tgtPos, Position);
          var ratio = (float)MathHelper.Clamp(distanceTo / 25, 0, 1);
          maxVelocity = MathHelper.Lerp(4f, desiredVelocity, ratio);
        }
        else
        {
          var tgtEnt = Target.Entity as IMyEntity;
          if (isOwner)
          {
            var minDistance = _followDistanceSqd * 2;
            var distanceTo = Vector3D.DistanceSquared(tgtEnt.WorldAABB.Center, Position) - minDistance;
            var ratio = (float)MathHelper.Clamp(distanceTo / minDistance, 0, 1);
            maxVelocity = MathHelper.Lerp(4f, 25f, ratio);
          }
          else if (Target.Entity != null)
          {
            Vector3D tgtPos;
            Vector3D actualPos;
            if (tgtEnt != null)
            {
              tgtPos = tgtEnt.WorldAABB.Center;
            }
            else
            {
              Target.GetTargetPosition(out tgtPos, out actualPos);
            }

            var distanceTo = Vector3D.DistanceSquared(tgtPos, Position);
            var ratio = (float)MathHelper.Clamp(distanceTo / 25, 0, 1);
            maxVelocity = MathHelper.Lerp(4f, desiredVelocity, ratio);
          }
        }
      }

      var velocity = Character.Physics?.LinearVelocity ?? Vector3.Zero;

      if (Vector3.IsZero(velocity))
        return;

      var gridLinearVelocity = Vector3.Zero;

      if (_currentGraph.IsGridGraph)
      {
        var grid = ((CubeGridMap)_currentGraph).Grid;
        if (!grid.IsStatic)
        {
          gridLinearVelocity = grid.Physics.LinearVelocity;
          velocity -= gridLinearVelocity;
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
        _damagePending = false;
        DoDamage();
      }
    }

    internal virtual void UpdateRelativeDampening()
    {
      if (_currentGraph?.IsGridGraph != true)
        return;

      var gridGraph = _currentGraph as CubeGridMap;
      var grid = gridGraph.Grid;

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
      destroyable.DoDamage(rand, MyStringHash.GetOrCompute("Punch"), true);

      if (!isCharacter)
        return;

      BotBase botTarget;
      if (AiSession.Instance.Bots.TryGetValue(character.EntityId, out botTarget) && botTarget != null)
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
      }
    }

    internal virtual bool IsInRangeOfTarget()
    {
      if (Target?.HasTarget != true || Vector3D.IsZero(Position))
        return false;

      return Target.IsFriendly() || Target.GetDistanceSquared() < 650000;
    }

    public virtual void AddWeapon() { }

    public virtual void SetTarget()
    {
      if (!_wantsTarget)
        return;

      _entities.Clear();
      _checkedGridIDs.Clear();

      var sphere = new BoundingSphereD(Position, 300);
      var blockDestroEnabled = MyAPIGateway.Session.SessionSettings.DestructibleBlocks;
      var queryType = blockDestroEnabled ? MyEntityQueryType.Both : MyEntityQueryType.Dynamic;
      MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, _entities, queryType);

      if (Target.IsDestroyed())
        Target.RemoveTarget();
      else if (Target.Entity is IMyDoor)
      {
        ++_doorTgtCounter;
        if (_doorTgtCounter <= 8)
          return;

        Target.RemoveTarget();
      }

      IMyEntity tgt = null;
      var botPosition = Position;
      var distance = double.MaxValue;
      _entities.ShellSort(botPosition);

      for (int i = 0; i < _entities.Count; i++)
      {
        var ent = _entities[i];
        if (ent?.MarkedForClose != false)
          continue;

        long entOwnerId;
        var ch = ent as IMyCharacter;
        var grid = ent as MyCubeGrid;

        if (ch != null)
        {
          if (ch.IsDead || ch.MarkedForClose || ch.EntityId == Character.EntityId)
            continue;

          if (!ch.IsBot && string.IsNullOrWhiteSpace(ch.DisplayName))
          {
            var subtype = ch.Definition.Id.SubtypeName;
            if (!subtype.StartsWith("space_spider", StringComparison.OrdinalIgnoreCase))
              continue;
          }
        }
        else if (grid?.Physics != null && !grid.IsPreview && !grid.MarkedForClose && !_checkedGridIDs.Contains(grid.EntityId))
        {
          _gridTurrets.Clear();
          _gridGroups1.Clear();
          MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Logical, _gridGroups1);

          foreach (var g in _gridGroups1)
          {
            _checkedGridIDs.Add(g.EntityId);
            var myGrid = g as MyCubeGrid;

            foreach (var cpit in myGrid.OccupiedBlocks)
            {
              if (cpit.Pilot != null)
                _entities.Add(cpit.Pilot);
            }

            if (HasWeaponOrTool && blockDestroEnabled)
            {
              var thisGridOwner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : 0;

              if (thisGridOwner == 0 || myGrid.BlocksCount > grid.BlocksCount)
              {
                if (myGrid.BigOwners?.Count > 0 || myGrid.SmallOwners?.Count > 0)
                  grid = myGrid;
              }

              var gatlings = myGrid.BlocksCounters.GetValueOrDefault(typeof(MyObjectBuilder_LargeGatlingTurret), 0);
              var missiles = myGrid.BlocksCounters.GetValueOrDefault(typeof(MyObjectBuilder_LargeMissileTurret), 0);
              var interiors = myGrid.BlocksCounters.GetValueOrDefault(typeof(MyObjectBuilder_InteriorTurret), 0);

              if (gatlings > 0 || missiles > 0 || interiors > 0)
              {
                var blocks = myGrid.GetFatBlocks();
                for (int j = 0; j < blocks.Count; j++)
                {
                  var block = blocks[j] as IMyLargeTurretBase;
                  if (block != null && !block.MarkedForClose && block.IsWorking)
                    _gridTurrets.Add(block);
                }
              }
            }
          }

          if (_gridTurrets.Count > 0)
          {
            //var player = MyAPIGateway.Players.GetPlayerControllingEntity(grid);
            //if (player != null)
            //  entOwnerId = player.IdentityId;
            /*else*/
            if (grid.BigOwners?.Count > 0)
            entOwnerId = grid.BigOwners[0];
            else if (grid.SmallOwners?.Count > 0)
              entOwnerId = grid.SmallOwners[0];
            else
              continue;

            var relation = MyIDModule.GetRelationPlayerPlayer(entOwnerId, Character.ControllerInfo.ControllingIdentityId);
            if (relation == MyRelationsBetweenPlayers.Allies || relation == MyRelationsBetweenPlayers.Self)
              continue;

            //if (_gridTurrets.Count == 0 && player?.Character?.IsDead == false)
            //{
            //  ent = player.Character as MyEntity;
            //}
            //else
            var dToTurret = double.MaxValue;
            MyEntity turretEnt = null;

            // check for turrets only
            for (int j = _gridTurrets.Count - 1; j >= 0; j--)
            {
              var turret = _gridTurrets[j];
              if (turret == null)
                continue;

              var d = Vector3D.DistanceSquared(turret.GetPosition(), botPosition);
              if (d < dToTurret)
              {
                turretEnt = turret as MyEntity;
                dToTurret = d;
              }
            }

            if (turretEnt == null)
              continue;

            ent = turretEnt;
          }
          else
            continue;
        }
        else
          continue;

        var dSqd = Vector3D.DistanceSquared(ent.PositionComp.WorldAABB.Center, botPosition);
        if (dSqd < distance)
        {
          tgt = ent;
          distance = dSqd;
        }

        break;
      }

      if (tgt == null)
      {
        Target.RemoveTarget();
        return;
      }
      else if (tgt is IMyCharacter)
      {
        var ch = tgt as IMyCharacter;
        List<BotBase> helpers;
        if (!(ch.Parent is IMyCockpit) && AiSession.Instance.PlayerToHelperDict.TryGetValue(ch.ControllerInfo.ControllingIdentityId, out helpers))
        {
          foreach (var bot in helpers)
          {
            if (bot.IsDead)
              continue;

            var d = Vector3D.DistanceSquared(bot.Position, botPosition);
            if (d < distance * 0.6)
            {
              tgt = bot.Character;
              distance = d;
            }
          }
        }
      }

      var parent = (tgt is IMyCharacter && tgt.Parent != null) ? tgt.Parent : tgt;
      if (ReferenceEquals(Target.Entity, parent))
        return;

      Target.SetTarget(parent);
      _pathCollection?.CleanUp(true);
    }

    internal void TrySwitchWalk()
    {
      if (_switchWalk)
      {
        _switchWalk = false;

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
      var botPosition = Position;

      bool positionValid = _currentGraph.IsPositionValid(targetPosition);
      if (!positionValid && !force) 
      {
        double maxLength, lengthToTop;
        var graphCenter = _currentGraph.OBB.Center;
        var vectorGraphToTarget = targetPosition - graphCenter;
        var dir = _currentGraph.WorldMatrix.GetClosestDirection(vectorGraphToTarget);
        var normal = _currentGraph.WorldMatrix.GetDirectionVector(dir);
        var graphUp = (dir == Base6Directions.Direction.Up) ? _currentGraph.WorldMatrix.Backward : _currentGraph.WorldMatrix.Up;

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

        if (targetAboveBox && dotUp <= 0 && GridBase.PointInsideVoxel(pointAboveNext, _currentGraph.Planet))
        {
          //AiSession.Instance.Logger.Log($"Moving next graph UP one level");
          // the target is going to be above the current map box
          maxLength = lengthToTop + (VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultCellSize) - (3 * _currentGraph.CellSize);

          if (GridBase.PointInsideVoxel(pointAboveThis, _currentGraph.Planet))
          {
            newGraphPosition = graphCenter + graphUp * maxLength;
          }
          else
          {
            newGraphPosition += graphUp * maxLength;
          }
        }
        else if (targetBelowBox && dotUp >= 0 && !GridBase.PointInsideVoxel(pointBelowThis, _currentGraph.Planet))
        {
          //AiSession.Instance.Logger.Log($"Moving next graph DOWN one level");
          // the target is going to be below the current map box
          maxLength = lengthToTop + (VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultCellSize) - (3 * _currentGraph.CellSize);
          newGraphPosition = graphCenter - graphUp * maxLength;
        }
        //else
        //{
        //  var vectorNextToTgt = targetPosition - newGraphPosition;
        //  var dirFromNext = _currentGraph.WorldMatrix.GetClosestDirection(vectorNextToTgt);

        //  if (dirFromNext != dir && dirFromNext != Base6Directions.Direction.Up && dirFromNext != Base6Directions.Direction.Down)
        //  {
        //    var normalFromNext = _currentGraph.WorldMatrix.GetDirectionVector(dirFromNext);

        //    var projectedFromNext = VectorUtils.Project(vectorNextToTgt, normalFromNext);
        //    if (projectedFromNext.LengthSquared() > maxLength * maxLength)
        //    {
        //      var adjacentPosition = newGraphPosition + (normalFromNext * maxLength) + (graphUp * lengthToTop * 0.9);

        //      if (!GridBase.PointInsideVoxel(adjacentPosition, _currentGraph.Planet))
        //      {
        //        AiSession.Instance.Logger.Log($"Moving next graph over one level");
        //        newGraphPosition += normalFromNext * maxLength;
        //      }
        //    }
        //  }          
        //}

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

      _checkedGridIDs.Clear();
      _rayEntities.Clear();

      var lineToNewGraph = new LineD(botPosition, newGraphPosition);
      var lineToTarget = new LineD(botPosition, targetPosition);
      MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref lineToNewGraph, _rayEntities);

      MyCubeGrid bigGrid = null;
      MyOrientedBoundingBoxD? newGridOBB = null;

      var gridGraph = _currentGraph as CubeGridMap;
      if (gridGraph?.Grid != null && !gridGraph.Grid.MarkedForClose)
      {
        bigGrid = gridGraph.Grid;
      }

      double distanceTargetToBot = Vector3D.DistanceSquared(botPosition, newGraphPosition);
      foreach (var overlapResult in _rayEntities)
      {
        var grid = overlapResult.Element as MyCubeGrid;
        if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose)
        {
          continue;
        }

        if (grid.GridSize < 1 || grid.EntityId == bigGrid?.EntityId || grid.BlocksCount <= 5 || _checkedGridIDs.Contains(grid.EntityId))
        {
          continue;
        }

        _gridGroups2.Clear();
        MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Logical, _gridGroups2);

        MyCubeGrid biggest = grid;

        foreach (MyCubeGrid g in _gridGroups2)
        {
          if (g != null && !g.MarkedForClose && g.GridSize > 1 && g.BlocksCount > biggest.BlocksCount)
            biggest = g;
        }

        _checkedGridIDs.Add(biggest.EntityId);
        BoundingBoxI box = new BoundingBoxI(biggest.Min, biggest.Max);

        foreach (var g in _gridGroups2)
        {
          if (g == null || g.MarkedForClose || g.EntityId == biggest.EntityId)
            continue;

          _checkedGridIDs.Add(g.EntityId);
          var min = biggest.WorldToGridInteger(g.GridIntegerToWorld(g.Min));
          var max = biggest.WorldToGridInteger(g.GridIntegerToWorld(g.Max));

          box = box.Include(ref min);
          box = box.Include(ref max);
        }

        var addition = biggest.Physics.IsStatic ? 10 : 5;
        var center = biggest.GridIntegerToWorld(box.Center);
        var halfExtents = box.HalfExtents * biggest.GridSize;
        var quat = Quaternion.CreateFromRotationMatrix(biggest.WorldMatrix);
        var obb = new MyOrientedBoundingBoxD(center, halfExtents, quat);

        if (obb.Intersects(ref lineToTarget) == null)
          continue;

        obb.HalfExtent = (box.HalfExtents + addition) * biggest.GridSize;

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

      if (newGrid != null)
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

    internal bool CheckIfCloseEnoughToAct(ref Vector3D targetPosition, ref Vector3D goToPosition, out bool shouldReturn)
    {
      shouldReturn = false;
      var botPos = Position;
      var localBot = _currentGraph.WorldToLocal(botPos);
      var localTgt = _currentGraph.WorldToLocal(targetPosition);
      var manhattanDist = Vector3I.DistanceManhattan(localTgt, localBot);

      if (targetPosition == Target.Override)
      {
        if (_currentGraph.IsGridGraph)
        {
          var gridGraph = _currentGraph as CubeGridMap;
          var cube = gridGraph.Grid.GetCubeBlock(gridGraph.Grid.WorldToGridInteger(targetPosition)) as IMySlimBlock;
          var seat = cube?.FatBlock as IMyCockpit;
          if (seat != null)
          {
            if (_canUseSeats && seat.Pilot == null && manhattanDist < 2)
            {
              var useObj = _pathCollection.GetBlockUseObject(seat as MyCubeBlock);
              if (useObj != null)
              {
                var relativePosition = Vector3D.Rotate(botPos - seat.GetPosition(), MatrixD.Transpose(seat.WorldMatrix));
                AiSession.Instance.BotToSeatRelativePosition[Character.EntityId] = relativePosition;
                useObj.Use(UseActionEnum.Manipulate, Character);
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
        float checkDistance = isFriendly ? 4 : 2;

        if (manhattanDist > checkDistance || _pathCollection.PathToTarget.Count > checkDistance)
          return false;

        if (isFriendly)
          checkDistance = _followDistanceSqd;
        else if (Target.IsSlimBlock || Target.IsCubeBlock)
          checkDistance = 8;
        else
          checkDistance = 4;

        var tgtPos = Target.IsSlimBlock ? _currentGraph.LocalToWorld(localTgt) : targetPosition;
        var dSquared = Vector3D.DistanceSquared(tgtPos, botPos);

        if (dSquared > checkDistance)
        {
          return false;
        }
      }

      if (Target.Entity != null)
      {
        _hitList2.Clear();
        MyAPIGateway.Physics.CastRay(botPos, targetPosition, _hitList2, CollisionLayers.CharacterCollisionLayer);

        for (int i = 0; i < _hitList2.Count; i++)
        {
          var hit = _hitList2[i];
          var hitEnt = hit?.HitEntity;

          if (hitEnt == null || hitEnt.EntityId == Character.EntityId)
            continue;

          if (hitEnt == Target.Entity)
          {
            if (_botState.IsFlying && Owner?.Character != null && Target.Entity == Owner.Character)
            {
              var vector = Vector3D.Rotate(targetPosition - botPos, MatrixD.Transpose(WorldMatrix));
              return vector.Y < 1;
            }

            return true;
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
              if (!string.IsNullOrWhiteSpace(ToolSubtype)
                && ToolSubtype.IndexOf("grinder", StringComparison.OrdinalIgnoreCase) < 0
                && ToolSubtype.IndexOf("welder", StringComparison.OrdinalIgnoreCase) < 0)
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

              return true;
            }
          }

          var hitGrid = hitEnt as IMyCubeGrid;
          if (hitGrid != null && (Target.IsCubeBlock || Target.IsSlimBlock))
          {
            var localPos = hitGrid.WorldToGridInteger(hit.Position);
            var cube = hitGrid.GetCubeBlock(localPos);
            if (cube == null)
            {
              var fixedPos = hit.Position - hit.Normal * hitGrid.GridSize * 0.5f;
              localPos = hitGrid.WorldToGridInteger(fixedPos);
              cube = hitGrid.GetCubeBlock(localPos);
            }

            var targetCube = Target.IsCubeBlock ? (Target.Entity as IMyCubeBlock)?.SlimBlock : Target.Entity as IMySlimBlock;
            return cube != null && targetCube != null && cube.Position == targetCube.Position;
          }
          
          if (Target.IsFloater)
          {
            var floater = Target.Entity as MyFloatingObject;
            var floaterPosition = floater?.PositionComp.WorldAABB.Center ?? Vector3D.PositiveInfinity;
            var distanceToFloater = Vector3D.DistanceSquared(floaterPosition, hit.Position);
            return distanceToFloater < 1;
          }
          
          return HasLineOfSight;
        }
      }

      return true;
    }

    internal void CheckDeniedDoors()
    {
      if (_pathCollection == null)
        return;

      _tempNodes.Clear();
      foreach (var kvp in _pathCollection.DeniedDoors)
      {
        var door = kvp.Value;
        if (door == null || door.Closed || door.MarkedForClose || door.SlimBlock.IsDestroyed)
        {
          _tempNodes.Add(kvp.Key);
          continue;
        }

        var def = door.SlimBlock.BlockDefinition as MyCubeBlockDefinition;
        if (door.SlimBlock.BuildLevelRatio < def.CriticalIntegrityRatio)
          _tempNodes.Add(kvp.Key);
      }

      foreach (var node in _tempNodes)
        _pathCollection.DeniedDoors.Remove(node);
    }

    internal void FindPathForIdleMovement(Vector3D moveTo)
    {
      if (_pathCollection.Locked)
        return;

      _stuckTimer = 0;
      _stuckCounter = 0;
      _botMoved = false;

      Vector3I start = _currentGraph.WorldToLocal(Position);
      bool startDenied = _pathCollection.DeniedDoors.ContainsKey(start);
      if (!_currentGraph.GetClosestValidNode(this, start, out start, currentIsDenied: startDenied))
      {
        var pn = _currentGraph.GetReturnHomePoint(this);
        _moveTo = pn.SurfacePosition ?? _currentGraph.LocalToWorld(pn.Position);
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
      _idleMovementActive = true;
      _pathCollection.CleanUp(true);

      _pathWorkData.PathStart = start;
      _pathWorkData.PathEnd = goal;
      _pathWorkData.IsIntendedGoal = Owner != null || !Target.HasTarget;
      _pathCollection.PathTimer.Restart();

      _task = MyAPIGateway.Parallel.StartBackground(_pathWorkAction, _pathWorkCallBack, _pathWorkData);
    }

    internal void CheckLineOfSight()
    {
      if (_awaitingCallBack || Character == null || Character.MarkedForClose || Character.IsDead)
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

      var pos = Position + WorldMatrix.Up * 0.4; // close to the muzzle height
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

        var angle = VectorUtils.GetAngleBetween(Character.WorldMatrix.Forward, tgt - pos);
        if (Math.Abs(angle) > VectorUtils.PiOver3)
        {
          if (hangar != null)
          {
            // Just in case the bot happens to be standing in front of the tip of the hangar
            tgt += hangar.WorldMatrix.Down * hangar.CubeGrid.GridSize;
            angle = VectorUtils.GetAngleBetween(Character.WorldMatrix.Forward, tgt - pos);
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

      _awaitingCallBack = true;
      _hitList.Clear();

      MyAPIGateway.Physics.CastRayParallel(ref pos, ref tgt, _hitList, CollisionLayers.CharacterCollisionLayer, RayBlockedCallback);
    }

    void RayBlockedCallback(List<IHitInfo> hitList)
    {
      _awaitingCallBack = false;

      if (Character?.IsDead != false || Character.MarkedForClose)
      {
        HasLineOfSight = false;
        return;
      }

      var targetTopMost = Target?.Entity as IMyEntity;
      var character = targetTopMost as IMyCharacter; ;
      if (targetTopMost == null || (character != null && character.IsDead))
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

        if (hitEnt.EntityId == Character.EntityId)
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

        var cube = Target.Entity as MyCubeBlock;
        var grid = hitEnt as MyCubeGrid;
        if (cube != null && grid != null)
        {
          if (grid.BlocksCount < 5)
            continue;

          _gridGroups3.Clear();
          MyAPIGateway.GridGroups.GetGroup(cube.CubeGrid, GridLinkTypeEnum.Logical, _gridGroups3);

          if (_gridGroups3.Contains(grid) && Vector3D.DistanceSquared(hitInfo.Position, cube.PositionComp.WorldAABB.Center) < 10)
            continue;
        }

        HasLineOfSight = false;
        break;
      }

      if (!HasLineOfSight)
        _waitForLOSTimer = true;
    }

    public void StartCheckGraph(ref Vector3D tgtPosition, bool force = false)
    {
      if (!_graphTask.IsComplete)
        return;

      if (_graphWorkData == null)
        _graphWorkData = new GraphWorkData();

      _checkGraph = true;
      _graphWorkData.Force = force;
      _graphWorkData.TargetPosition = tgtPosition;
      _graphTask = MyAPIGateway.Parallel.Start(_graphWorkAction, _graphWorkCallBack, _graphWorkData);
    }

    void CheckGraphComplete(WorkData workData)
    {
      _checkGraph = false;
    }

    void CheckGraph(WorkData workData)
    {
      var data = workData as GraphWorkData;
      var targetPosition = data.TargetPosition;
      bool force = data.Force;

      MyCubeGrid newGrid;
      Vector3D newGraphPosition, intermediatePosition;
      bool botInNewBox;

      if (_needsTransition)
      {
        bool targetInNext = _nextGraph?.IsPositionValid(targetPosition) == true;
        bool targetInCurrent = _currentGraph.IsPositionValid(targetPosition);
        bool transitionOk = _transitionPoint != null && _currentGraph.IsPositionUsable(this, _transitionPoint.Position);
        bool nextGraphOK = _nextGraph != null;
        if (!nextGraphOK || !transitionOk || (!targetInNext && targetInCurrent))
        {
          _transitionPoint = null;
          _needsTransition = false;
        }
        else if (nextGraphOK)
        {
          bool switchNow = true;
          var botPosition = Position;

          var localBot = _currentGraph.WorldToLocal(botPosition);
          var currentNode = _currentGraph.OpenTileDict.GetValueOrDefault(localBot, null);
          var currentIsTunnel = currentNode?.IsTunnelNode ?? false;
          var targetIsTunnel = _transitionPoint?.IsTunnelNode ?? false;

          if (!_nextGraph.IsPositionValid(botPosition) || (targetIsTunnel && !currentIsTunnel))
          {
            if (_transitionPoint != null)
            {
              var transitionPosition = _transitionPoint.SurfacePosition ?? _currentGraph.LocalToWorld(_transitionPoint.Position);
              var distance = Vector3D.DistanceSquared(botPosition, transitionPosition);
              if (distance > 5)
              {
                switchNow = false;
                _checkGraph = true;
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
            _currentGraph = _nextGraph;
            _nextGraph = null;
            _transitionPoint = null;
            _needsTransition = false;
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
        _needsTransition = !force;
        var botPosition = Position;

        if (newGrid != null || !_currentGraph.IsPositionValid(newGraphPosition))
        {         
          _nextGraph = AiSession.Instance.GetNewGraph(newGrid, newGraphPosition, WorldMatrix);
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
                  _nextGraph = new VoxelGridMap(botPosition);
                }
              }
              else
              {
                var bufferZonePosition = gridGraph.GetBufferZoneTargetPosition(targetPosition, center);

                if (bufferZonePosition.HasValue && !_currentGraph.IsPositionUsable(this, bufferZonePosition.Value))
                {
                  bufferZonePosition = gridGraph.GetClosestSurfacePointFast(this, bufferZonePosition.Value, WorldMatrix.Up);
                }

                var localNode = _currentGraph.WorldToLocal(bufferZonePosition.Value);
                if (_currentGraph.GetClosestValidNode(this, localNode, out localNode))
                {
                  tgtPoint = _currentGraph.OpenTileDict[localNode];
                }
              }

              if (tgtPoint == null || !_needsTransition)
              {
                _nextGraph = null;
                _pathCollection?.CleanUp(true);
                _needsTransition = false;
              }
              else
              {
                _transitionPoint = tgtPoint;
              }
            }
          }
          else if (_nextGraph != null)
          {
            if (!botInNewBox)
            {
              var direction = _currentGraph.WorldMatrix.GetClosestDirection(_nextGraph.OBB.Center - _currentGraph.OBB.Center);
              var tgtPoint = _currentGraph.GetBufferZoneTargetPositionFromPrunik(ref _nextGraph.OBB, ref direction, ref targetPosition, this);

              if (tgtPoint == null)
              {
                _nextGraph = new VoxelGridMap(botPosition);
              }
              else
              {
                _transitionPoint = tgtPoint;
                switchNow = false;
              }
            }
          }
          else if (_needsTransition)
          {
            var intermediateNode = _currentGraph.WorldToLocal(intermediatePosition);
            if (_currentGraph.GetClosestValidNode(this, intermediateNode, out intermediateNode))
            {
              _transitionPoint = _currentGraph.OpenTileDict[intermediateNode];
            }
          }
        }

        if (switchNow && _nextGraph != null)
        {
          _currentGraph = _nextGraph;
          _nextGraph = null;
          _transitionPoint = null;
          _needsTransition = false;
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
        enable |= _requiresJetpack;

        if (jetComp.TurnedOn)
        {
          if (!enable)
            jetComp.TurnOnJetpack(false);
        }
        else if (enable)
          jetComp.TurnOnJetpack(true);

        return true;
      }

      _canUseAirNodes = false;
      return false;
    }

    internal void UsePathfinder(Vector3D gotoPosition, Vector3D actualPosition)
    {
      try
      {
        if (_currentGraph?.Ready != true)
        {
          AiSession.Instance.AnalyzeHash.Add(Character.EntityId);

          var gridGraph = _currentGraph as CubeGridMap;
          if (gridGraph != null && (gridGraph.Grid == null || gridGraph.Grid.MarkedForClose))
          {
            _checkGraph = false;
            _needsTransition = false;
            _currentGraph = AiSession.Instance.GetVoxelGraph(Position, true);

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
          _pathCollection.DrawFreeSpace(Position, gotoPosition);
        #endregion

        if (_checkGraph)
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
        if (CheckIfCloseEnoughToAct(ref actualPosition, ref gotoPosition, out returnNow))
        {
          _pathCollection.CleanUp(true);

          if (Target.Override.HasValue)
          {
            if (actualPosition == Target.Override)
              Target.RemoveOverride(true);
          }

          if (!returnNow)
          {
            var checkDistance = Target.IsCubeBlock ? 5 : 2;
            MoveToPoint(gotoPosition, true, checkDistance);
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

        if (_useLadder || _botState.IsOnLadder || _botState.WasOnLadder)
        {
          _stuckTimer = 0;

          if (_useLadder)
          {
            bool forceUse = false;
            if (_pathCollection.HasNode && (_nextIsLadder || (_afterNextIsLadder && _pathCollection.PathToTarget.Count > 0)))
            {
              var node = _nextIsLadder ? _pathCollection.NextNode.Position : _pathCollection.PathToTarget.Peek().Position;
              var worldNode = _currentGraph.LocalToWorld(node);
              var rotated = Vector3D.Rotate(worldNode - Position, MatrixD.Transpose(WorldMatrix));
              forceUse = rotated.Y < -1;
            }

            bool wait;
            if (FaceLadderAndUse(forceUse, out wait))
            {
              _afterNextIsLadder = false;
              _useLadder = false;
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
            _nextIsLadder = false;
            _afterNextIsLadder = false;
            _stuckCounter = 0;
            _stuckTimerReset = 0;
            _waitForStuckTimer = false;
            _pathCollection.CleanUp(true);
          }
        }

        if (!_pathCollection.HasNode && !_useLadder && _botState.IsOnLadder)
          _pathFinderActive = false;

        if (_botMoved || targetMoved || (!_pathCollection.HasNode && !_pathCollection.HasPath))
        {
          if (ShouldFind(ref gotoPosition))
          {
            if (_task.IsComplete)
              FindNewPath(gotoPosition);
            else
              _pathTimer = 1000;
          }
          else if (!_botMoved)
          {
            if (_pathCollection.HasNode)
            {
              UpdatePathAndMove(ref distanceToCheck);
            }
            else if (_pathCollection.HasPath && !_useLadder)
            {
              GetNextNodeAndMove(ref distanceToCheck);
            }
            else if (!_pathCollection.HasPath && !_pathCollection.Locked && _pathCollection.IdlePathTimer > 120 && !Target.IsSlimBlock
              && (!_needsTransition || _transitionPoint == null))
            {
              SimulateIdleMovement(false, Owner?.Character?.IsDead == false);
            }
          }
        }
        else if (_pathCollection.HasNode)
        {
          UpdatePathAndMove(ref distanceToCheck);
        }
        else if (_pathCollection.HasPath && !_useLadder)
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
      if (_stuckCounter > 1 || _pathCollection.UpdateDistanceToNextNode())
      {
        _stuckTimer = 0;
        _stuckCounter = 0;

        _sideNode = null;
        _botMoved = true;
        _nextIsLadder = false;
        _afterNextIsLadder = false;
        _useLadder = false;

        if (_stuckCounter > 1)
        {
          if (!_currentGraph.IsGridGraph)
          {
            var voxelGraph = _currentGraph as VoxelGridMap;
            var lastNode = _pathCollection.LastNode;
            var nextNode = _pathCollection.NextNode;

            var currentNode = _currentGraph.WorldToLocal(Position);
            var current = _currentGraph.LocalToWorld(currentNode);
            var last = lastNode != null ? lastNode.SurfacePosition ?? _currentGraph.LocalToWorld(lastNode.Position) : current;
            var next = nextNode != null ? nextNode.SurfacePosition ?? _currentGraph.LocalToWorld(nextNode.Position) : current;

            voxelGraph.AddToObstacles(last, current, next);
            _pathCollection.CleanUp(true);
          }
          else
            _pathCollection.ClearNode(true);
        }
        else
          _pathCollection.CleanUp(true);
      }
      else if (_afterNextIsLadder)
      {
        // Only true if you're about to climb down from a cliff edge
        _useLadder = true;
      }
      else
      {
        var pNode = _pathCollection.NextNode;
        var worldNode = pNode.SurfacePosition ?? _currentGraph.LocalToWorld(pNode.Position);

        var vector = worldNode - Position;
        var relVector = Vector3D.TransformNormal(vector, MatrixD.Transpose(WorldMatrix));
        var flattenedVector = new Vector3D(relVector.X, 0, relVector.Z);
        var flattenedLengthSquared = flattenedVector.LengthSquared();
        var check = 1.1 * distanceToCheck;

        if (flattenedLengthSquared < check && Math.Abs(relVector.Y) < check)
        {
          if (_nextIsLadder)
          {
            _useLadder = true;
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
      _botMoved = false;
      var botPosition = Position;

      Vector3I start = _currentGraph.WorldToLocal(botPosition);
      if (!_currentGraph.GetClosestValidNode(this, start, out start))
      {
        var newStart = _currentGraph.WorldToLocal(botPosition);
        if (newStart == start || !_currentGraph.GetClosestValidNode(this, newStart, out start))
        {
          _pathCollection.CleanUp(true);

          var home = _currentGraph.GetReturnHomePoint(this);

          lock (_pathCollection.PathToTarget)
            _pathCollection.PathToTarget.Enqueue(home);

          return;
        }
      }

      var isSlimBlock = Target.IsSlimBlock;

      Vector3D exit;
      Vector3I goal;
      var tgtCharacter = Target.Entity as IMyCharacter;
      var isCharacter = tgtCharacter != null;
      if (_needsTransition && _transitionPoint != null)
      {
        isCharacter = false;
        goal = _transitionPoint.Position;
        exit = _transitionPoint.SurfacePosition ?? _currentGraph.LocalToWorld(goal);
      }
      //else if (isCharacter)
      //{
      //  exit = tgtCharacter.WorldMatrix.Translation;
      //  goal = _currentGraph.WorldToLocal(exit);
      //}
      else
      {
        exit = targetPosition;
        goal = _currentGraph.WorldToLocal(exit);
      }

      Node goalNode;
      bool found = false;
      if (isCharacter && tgtCharacter.EntityId == Owner?.Character?.EntityId && _currentGraph.OpenTileDict.TryGetValue(goal, out goalNode) && goalNode.IsAirNode)
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
          var position = block.Position;

          lock (_pathCollection.PathToTarget)
          {
            var node = new Node(position, grid, block);
            _pathCollection.PathToTarget.Enqueue(node);
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
      //FindPath();
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
        var distanceToTgtSqd = Vector3D.DistanceSquared(Position, targetPosition);
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

      if (!_canUseLadders || !_pathCollection.HasNode || !_currentGraph.IsGridGraph)
      {
        _useLadder = _nextIsLadder = false;
        return false;
      }

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
              var up = Base6Directions.GetIntVector(ladder.CubeGrid.WorldMatrix.GetClosestDirection(WorldMatrix.Up));
              var blockAbove = ladder.CubeGrid.GetCubeBlock(ladder.Position + up) as IMySlimBlock;
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
      var slim = gridGraph.Grid.GetCubeBlock(localPathTo) as IMySlimBlock;

      if (slim == null || slim.BlockDefinition.Id.TypeId != typeof(MyObjectBuilder_Ladder2))
        return false;

      var cubeForward = slim.CubeGrid.WorldMatrix.GetDirectionVector(slim.Orientation.Forward);
      var adjustedLadderPosition = slim.CubeGrid.GridIntegerToWorld(slim.Position) + cubeForward * slim.CubeGrid.GridSize * -0.5;

      var botMatrixT = MatrixD.Transpose(WorldMatrix);
      var vectorToLadder = adjustedLadderPosition - Position;
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
      _pathFinderActive = true;
      var botPosition = Position;
      var pNode = _pathCollection.NextNode;
      var worldNode = pNode.SurfacePosition ?? _currentGraph.LocalToWorld(pNode.Position);

      if (!_nextIsLadder && _botState.IsOnLadder && _botState.GoingDownLadder)
      {
        Character.Use();
        _useLadder = false;
        _afterNextIsLadder = false;
        _ticksSinceLastDismount = 0;

        if (_requiresJetpack && !_jetpackEnabled)
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

      MoveToPoint(worldNode, false, _nextIsLadder ? distanceToCheck * 0.5 : distanceToCheck);
    }

    bool _lastWasAirNode;
    void GetNextNodeAndMove(ref float distanceToCheck)
    {
      var botPosition = Position;
      bool useLadderNow, findNewPath, wait, nextIsAirNode;
      _pathCollection.GetNextNode(botPosition, _botState.IsOnLadder, _transitionPoint != null,
        out _nextIsLadder, out _afterNextIsLadder, out UseObject, out useLadderNow, out findNewPath, out nextIsAirNode);

      if (!findNewPath)
      {
        if (nextIsAirNode)
        {
          if (!_canUseAirNodes || !TrySwitchJetpack(true))
          {
            findNewPath = true;
          }
        }
        else if (_jetpackEnabled && !_requiresJetpack && !_lastWasAirNode)
        {
          Node curNode;
          var current = _currentGraph.WorldToLocal(botPosition);
          if (!_currentGraph.OpenTileDict.TryGetValue(current, out curNode) || !curNode.IsAirNode)
            TrySwitchJetpack(false);
        }
      }

      _lastWasAirNode = nextIsAirNode;

      if (findNewPath)
      {
        _pathCollection.ClearNode(true);
      }
      else if (useLadderNow && FaceLadderAndUse(true, out wait))
      {
        _nextIsLadder = false;
        _afterNextIsLadder = false;
        _useLadder = false;
        _pathCollection.ClearNode();
      }

      _stuckCounter = 0;

      if (!_pathCollection.Dirty && _pathCollection.HasNode)
        UpdatePathAndMove(ref distanceToCheck);
    }

    internal bool CheckPathForCharacter()
    {
      try
      {
        if (Character == null || Character.MarkedForClose || Character.IsDead)
          return false;

        var position = Position;
        var forward = WorldMatrix.Forward;
        _hitList2.Clear();
        MyAPIGateway.Physics.CastRay(position + forward * 0.25, position + forward * 3, _hitList2, CollisionLayers.CharacterCollisionLayer);

        // TODO: switch to using character's MyCasterComponent here??? Needs testing

        for (int i = 0; i < _hitList2.Count; i++)
        {
          var hitInfo = _hitList2[i];
          if (hitInfo?.HitEntity == null || hitInfo.HitEntity.EntityId == Character.EntityId || hitInfo.HitEntity == Target?.Entity)
            continue;

          return hitInfo.HitEntity is IMyCharacter || hitInfo.HitEntity is MyEnvironmentSector;
        }

        return false;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in BotBase.CheckPathForCharacter: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
        return false;
      }
    }

    internal bool CheckPathForClosedDoor()
    {
      try
      {
        var gridGraph = _currentGraph as CubeGridMap;
        if (gridGraph == null)
          return false;

        var botPosition = Position;
        var forward = WorldMatrix.Forward;
        
        _lineList.Clear();
        gridGraph.Grid.RayCastCells(botPosition, botPosition + forward * 3, _lineList);

        bool doorFound = false;
        for (int i = 0; i < _lineList.Count; i++)
        {
          var slim = gridGraph.Grid.GetCubeBlock(_lineList[i]) as IMySlimBlock;
          var doorBlock = slim?.FatBlock as IMyDoor;

          if (doorBlock == null || doorBlock.MarkedForClose)
            continue;

          doorFound = true;
          break;
        }

        if (!doorFound)
          return false;

        IHitInfo hitInfo;
        MyAPIGateway.Physics.CastRay(botPosition, botPosition + forward * 3, out hitInfo, CollisionLayers.CharacterCollisionLayer);

        IMySlimBlock block;
        IMyDoor door;
        var hitEnt = hitInfo?.HitEntity;
        if (hitEnt == null)
        {
          var localPos = gridGraph.WorldToLocal(botPosition);
          block = gridGraph.Grid.GetCubeBlock(localPos);
          door = block?.FatBlock as IMyDoor;

          if (door == null)
            return false;
        }
        else
        {
          var subpart = hitEnt as MyEntitySubpart;
          if (subpart != null)
          {
            door = subpart.Parent as IMyDoor;
            block = door?.SlimBlock;
          }
          else
          {
            var grid = hitEnt as IMyCubeGrid;
            if (grid == null)
              return false;

            var worldPos = hitInfo.Position - hitInfo.Normal * grid.GridSize * 0.2f;
            var blockPos = grid.WorldToGridInteger(worldPos);

            block = grid.GetCubeBlock(blockPos);
            door = block?.FatBlock as IMyDoor;

            if (door == null)
            {
              worldPos = hitInfo.Position + hitInfo.Normal * grid.GridSize * 0.2f;
              blockPos = grid.WorldToGridInteger(worldPos);

              block = grid.GetCubeBlock(blockPos);
              door = block?.FatBlock as IMyDoor;
            }
          }
        }

        if (door == null)
          return false;

        if (!door.IsFunctional)
        {
          var blockDef = (MyCubeBlockDefinition)door.SlimBlock.BlockDefinition;
          if (door.SlimBlock.BuildLevelRatio < blockDef.CriticalIntegrityRatio)
            return true;
        }

        if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open)
          return true;

        bool hasAccess = false;
        var doorOwner = door.CubeGrid.BigOwners.Count > 0 ? door.CubeGrid.BigOwners[0] : door.CubeGrid.SmallOwners.Count > 0 ? door.CubeGrid.SmallOwners[0] : 0;

        if (Owner != null)
        {
          var botOwner = Owner.IdentityId;
          var relation = MyIDModule.GetRelationPlayerPlayer(botOwner, doorOwner, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);

          hasAccess = ((MyDoorBase)door).AnyoneCanUse || relation != MyRelationsBetweenPlayers.Enemies;
        }
        else
        {
          var botOwner = Character.ControllerInfo.ControllingIdentityId;
          var relation = MyIDModule.GetRelationPlayerPlayer(botOwner, doorOwner);

          hasAccess = relation == MyRelationsBetweenPlayers.Self || relation == MyRelationsBetweenPlayers.Allies;
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
          if (door.CubeGrid.EntityId != gridGraph.Grid.EntityId)
            pos = gridGraph.Grid.WorldToGridInteger(door.CubeGrid.GridIntegerToWorld(pos));

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
              var mainGridPosition = gridGraph.Grid.WorldToGridInteger(block.CubeGrid.GridIntegerToWorld(position));

              gridGraph.BlockedDoors[mainGridPosition] = door;
            }
          }

          var pointBehind = Position + WorldMatrix.Backward * gridGraph.CellSize;
          var newPosition = gridGraph.GetLastValidNodeOnLine(pointBehind, WorldMatrix.Backward, 10);
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
        else if (door.IsWorking && door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Open)
        {
          if (door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Opening)
          {
            door.OpenDoor();
            AiSession.Instance.DoorsToClose[door.EntityId] = MyAPIGateway.Session.ElapsedPlayTime;
          }

          return true;
        }
      }
      catch (Exception ex)
      {
        if (MyAPIGateway.Session?.Player != null)
          MyAPIGateway.Utilities.ShowNotification($"Error in CheckForDoor: {ex.Message}", 1000);
  
        AiSession.Instance.Logger.LogAll($"Error checking for closed door: {ex.Message}\n{ex.StackTrace}\n", MessageType.ERROR);
      }

      return false;
    }

    internal void AdjustMovementForFlight(ref Vector3D relVectorBot, ref Vector3 movement, ref Vector3D botPosition, bool towardBlock = false)
    {
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
      }
      else
      {
        float multiplier;
        var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(botPosition, out multiplier);
        var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(botPosition, multiplier);

        multiplier = 0;
        var tGrav = nGrav + aGrav;
        if (tGrav.LengthSquared() > 0)
          multiplier = MathHelper.Clamp(tGrav.Length() / 9.81f, 0, 1);

        var amount = MathHelper.Lerp(0.5f, 0.25f, multiplier);
        movement += Vector3.Down * amount;
      }
    }

    internal void MoveToPoint(Vector3 movement, Vector2 rotation, float roll = 0)
    {
      if (_currentGraph?.Ready == true && _pathFinderActive && !_useLadder && !_botState.IsOnLadder && !_botState.WasOnLadder)
      {
        if (!_currentGraph.IsGridGraph)
        {
          if (_stuckTimer > 120)
          {
            if (_pathCollection.HasNode && !_currentGraph.IsGridGraph)
            {
              var currentPosition = Position;
              var nextPosition = _pathCollection.NextNode.SurfacePosition ?? _currentGraph.LocalToWorld(_pathCollection.NextNode.Position);
              var fromPosition = _pathCollection.LastNode != null ? _pathCollection.LastNode.SurfacePosition ?? _currentGraph.LocalToWorld(_pathCollection.LastNode.Position)
                : currentPosition;

              var vGrid = _currentGraph as VoxelGridMap;
              vGrid?.AddToObstacles(fromPosition, currentPosition, nextPosition);
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
          _waitForStuckTimer = true;
        }
        else if (_stuckTimer > 60 && CheckPathForClosedDoor())
        {
          _stuckTimer = 0;
        }
        else if (CheckPathForCharacter())
        {
          _waitForSwerveTimer = true;
        }
      }

      if (_waitForStuckTimer)
      {
        _stuckTimer = 0;

        if (++_stuckTimerReset > 60)
        {
          _stuckTimerReset = 0;
          _waitForStuckTimer = false;
        }

        rotation *= _stuckCounter == 0 ? -1 : 1;

        if (_stuckTimerReset > 40)
          Character.Jump();
      }
      else if (_waitForSwerveTimer)
      {
        _stuckTimer = 0;

        if (++_stuckTimerReset > 10)
        {
          _stuckTimerReset = 0;
          _waitForSwerveTimer = false;
        }

        movement += new Vector3(0.75f, 0, 0.5f);
      }

      Character.MoveAndRotate(movement, rotation, roll);
    }

    internal void SimulateIdleMovement(bool getMoving, bool towardOwner = false)
    {
      if (towardOwner)
      {
        _moveTo = null;
        _idleTimer = 0;
      }
      else if (_moveTo.HasValue)
      {
        var vector = Vector3D.TransformNormal(_moveTo.Value - Position, Matrix.Transpose(WorldMatrix));
        var flattenedVector = new Vector3D(vector.X, 0, vector.Z);

        if (flattenedVector.LengthSquared() <= 3)
          _moveTo = null;
        else
        {
          var distFromPrev = Vector3D.DistanceSquared(_prevMoveTo.Value, Position);
          if (distFromPrev > 4)
          {
            _idleTimer = 0;
            _prevMoveTo = Position;
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

      if (_moveTo == null)
      {
        if (towardOwner)
        {
          var pos = Owner.Character.WorldAABB.Center;
          if (Vector3D.DistanceSquared(Position, pos) <= 25)
          {
            _pathCollection?.CleanUp(true);

            Vector3D goTo, actual;
            if (Target.GetTargetPosition(out goTo, out actual))
            {
              var botPosition = Position;
              actual = Vector3D.Normalize(actual - botPosition);
              MoveToPoint(botPosition + actual);
            }

            return;
          }

          if (graphReady)
          {
            var localOwner = _currentGraph.WorldToLocal(pos);
            if (_currentGraph.GetClosestValidNode(this, localOwner, out localOwner, WorldMatrix.Up))
              _moveTo = _currentGraph.LocalToWorld(localOwner);
          }
          else
            _moveTo = pos;
        }

        if (_moveTo == null)
        {
          var direction = GetTravelDirection();

          if (graphReady)
          {
            Node moveNode;
            var pos = Position + direction * MyUtils.GetRandomInt(10, (int)_currentGraph.OBB.HalfExtent.AbsMax());
            if (!_currentGraph.GetRandomOpenNode(this, pos, out moveNode) || moveNode == null)
              return;

            _moveTo = moveNode.SurfacePosition ?? _currentGraph.LocalToWorld(moveNode.Position);
          }
          else
            _moveTo = Position + direction * MyUtils.GetRandomInt(10, 26);
        }

        _prevMoveTo = Position;
        _idleTimer = 0;
      }

      if (graphReady)
      {
        if (getMoving)
        {
          var distanceToCheck = (_botState.IsRunning || _botState.IsFlying) ? 1 : 0.5f;
          if (_pathCollection.HasNode)
            CheckNode(ref distanceToCheck);

          if (_pathCollection.HasNode)
          {
            UpdatePathAndMove(ref distanceToCheck);
          }
          else if (_pathCollection.HasPath && !_useLadder)
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

    Vector3D GetTravelDirection()
    {
      var random = MyUtils.GetRandomInt(1, 9);

      switch (random)
      {
        case 1:
          return WorldMatrix.Forward;
        case 2:
          return WorldMatrix.Forward + WorldMatrix.Right;
        case 3:
          return WorldMatrix.Right;
        case 4:
          return WorldMatrix.Right + WorldMatrix.Backward;
        case 5:
          return WorldMatrix.Backward;
        case 6:
          return WorldMatrix.Backward + WorldMatrix.Left;
        case 7:
          return WorldMatrix.Left;
        case 8:
          return WorldMatrix.Left + WorldMatrix.Forward;
        default:
          return WorldMatrix.Forward;
      }
    }
  }
}
