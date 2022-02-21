using AiEnabled.Bots;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

using VRageMath;

using Sandbox.Definitions;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using AiEnabled.API;
using VRage.Game.Entity;
using Sandbox.Common.ObjectBuilders;
using AiEnabled.Bots.Roles;
using AiEnabled.Utilities;
using Sandbox.Game;
using VRage.Game.ModAPI.Interfaces;
using VRageRender;

namespace AiEnabled.Projectiles
{
  public partial class BotProjectile
  {
    public float Damage;
    public float BlockDamage;
    public bool IsTracer;
    public long WeaponId;
    public double ProjectileSpeed;
    public IMyCharacter Owner;
    public IMyEntity Target;

    public Vector3D Start;
    public Vector3D LastPosition;
    public Vector3D Position;
    public Vector3D Direction;
    float _headShotMultiplier;
    bool _firstCheck;
    bool _isMissile;
    Color _trailColor;
    List<IHitInfo> _hitList;
    MyEntity _missile;

    public void Init(Vector3D start, Vector3D dir, IMyCharacter bot, IMyEntity target, float damage, MyGunBase gun, List<IHitInfo> hitList, MyEntity missile)
    {
      if (missile != null)
      {
        _isMissile = true;
        _missile = missile;

        var ammoDef = gun.CurrentAmmoDefinition as MyMissileAmmoDefinition;
        _headShotMultiplier = 1;
        _trailColor = Color.Transparent;
        BlockDamage = damage;
        ProjectileSpeed = ammoDef.DesiredSpeed;
      }
      else
      {
        var ammoDef = gun.CurrentAmmoDefinition as MyProjectileAmmoDefinition;
        _headShotMultiplier = ammoDef == null ? 2 : ammoDef.ProjectileHeadShotDamage / ammoDef.ProjectileHealthDamage;
        _trailColor = Vector3.IsZero(ammoDef.ProjectileTrailColor) ? Color.White : new Color(ammoDef.ProjectileTrailColor);
        BlockDamage = ammoDef.ProjectileMassDamage;
        ProjectileSpeed = ammoDef.DesiredSpeed;
      }

      _firstCheck = true;
      _hitList = hitList;

      // Keen does damage = DamageModifier * (IsCharacter ? (HeadShot ? HeadShotDamage : HealthDamage) : MassDamage);

      Owner = bot;
      Target = target;
      Damage = damage;
      WeaponId = bot.EquippedTool.EntityId;
      Start = start;
      LastPosition = start;
      Position = start;
      Direction = dir;

      if (!_isMissile && MyAPIGateway.Session.Player != null)
      {
        IsTracer = MyUtils.GetRandomInt(1, 101) > 30;

        var soundComp = bot.Components?.Get<MyCharacterSoundComponent>();
        if (soundComp != null)
        {
          soundComp.PlayActionSound(gun.ShootSound);
        }
      }
      else
      {
        IsTracer = false;
      }
    }

    public bool Update(double timeStep)
    {
      LastPosition = Position;
      Position += Direction * ProjectileSpeed * timeStep;

      if (_isMissile)
      {
        if (_missile == null || _missile.Closed || _missile.MarkedForClose)
          return true;
      }
      else
      {
        var maxDistance = AiSession.Instance.MaxBotProjectileDistance;

        if (Vector3D.DistanceSquared(Start, Position) > maxDistance * maxDistance)
          return true;
      }

      float tracerLength = IsTracer ? MyUtils.GetRandomFloat(1, 2) : 0f;

      IHitInfo hitInfo = null;
      IMyEntity hitEntity = null;
      Vector3D hitPosition = Vector3D.Zero;
      var line = new LineD(LastPosition, Position);
      string effectName, soundName;

      if (AiSession.Instance.ShieldAPILoaded)
      {
        if (_firstCheck)
          LastPosition = Start - Direction;

        List<MyLineSegmentOverlapResult<VRage.Game.Entity.MyEntity>> lineList;
        if (!AiSession.Instance.OverlapResultListStack.TryPop(out lineList))
          lineList = new List<MyLineSegmentOverlapResult<VRage.Game.Entity.MyEntity>>();
        else
          lineList.Clear();

        MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref line, lineList, MyEntityQueryType.Dynamic);

        bool shieldHit = false;
        for (int i = 0; i < lineList.Count; i++)
        {
          var lineResult = lineList[i];
          var shieldent = lineResult.Element;
          if (AiSession.Instance.ShieldHash == shieldent?.DefinitionId?.SubtypeId && shieldent.Render.Visible)
          {
            var shieldInfo = AiSession.Instance.ShieldAPI.MatchEntToShieldFastExt(shieldent, true);
            if (shieldInfo != null && shieldInfo.Value.Item2.Item1 && Vector3D.Transform(Start, shieldInfo.Value.Item3.Item1).LengthSquared() > 1)
            {
              var dist = DefenseShieldsAPI.IntersectEllipsoid(shieldInfo.Value.Item3.Item1, shieldInfo.Value.Item3.Item2, new RayD(line.From, line.Direction));

              if (dist.HasValue && dist.Value <= line.Length)
              {
                // hit a shield
                hitPosition = line.From + line.Direction * dist.Value;
                var additionalDamage = _isMissile ? Damage : 0;

                AiSession.Instance.ShieldAPI.PointAttackShieldCon(shieldInfo.Value.Item1, hitPosition, Owner.EntityId, Damage, additionalDamage, energy: _isMissile, drawParticle: true);
                shieldHit = true;
                break;
              }
            }
          }
        }

        lineList.Clear();
        AiSession.Instance.OverlapResultListStack.Push(lineList);

        if (shieldHit)
        {
          if (MyAPIGateway.Session.Player != null)
          {
            if (_isMissile)
            {
              effectName = MyParticleEffectsNameEnum.Explosion_Missile;
              soundName = ProjectileConstants.ShieldHitSound_Missile.String;
            }
            else
            {
              effectName = MyParticleEffectsNameEnum.MaterialHit_Metal;
              soundName = ProjectileConstants.ShieldHitSound_Projectile.String;
            }

            var matrix = Owner.WorldMatrix;
            matrix.Translation = hitPosition;

            MyParticleEffect _;
            MyParticlesManager.TryCreateParticleEffect(effectName, ref matrix, ref hitPosition, uint.MaxValue, out _);

            MyEntity3DSoundEmitter emitter = AiSession.Instance.GetEmitter();
            emitter.SetPosition(hitPosition);

            MySoundPair soundPair;
            if (!AiSession.Instance.SoundPairDict.TryGetValue(soundName, out soundPair))
            {
              soundPair = new MySoundPair(soundName);
              AiSession.Instance.SoundPairDict[soundName] = soundPair;
            }

            emitter.PlaySound(soundPair);
            AiSession.Instance.ReturnEmitter(emitter);
          }

          return true;
        }
      }

      if (_isMissile)
        return false;

      if (_firstCheck)
      {
        _firstCheck = false;
        LastPosition = Start - Direction;

        _hitList.Clear();
        MyAPIGateway.Physics.CastRay(LastPosition, Position, _hitList, CollisionLayers.CharacterCollisionLayer);

        for (int i = 0; i < _hitList.Count; i++)
        {
          hitInfo = _hitList[i];
          var hit = hitInfo?.HitEntity;
          if (hit == null)
            continue;

          if (hit.EntityId == Owner.EntityId || hit.EntityId == Owner.EquippedTool?.EntityId)
          {
            continue;
          }

          hitEntity = hit;
          hitPosition = hitInfo.Position;
          break;
        }

        if (hitEntity == null)
        {
          if (IsTracer)
          {
            var tracerPos = Position - Direction * tracerLength;
            var clr = _trailColor.ToVector4();
            MyTransparentGeometry.AddLineBillboard(ProjectileConstants.ProjectileTrailLine, clr, tracerPos, (Vector3)Direction, tracerLength, 0.01f, BlendTypeEnum.PostPP);
          }

          return false;
        }
      }
      else if (!MyAPIGateway.Physics.CastRay(LastPosition, Position, out hitInfo, CollisionLayers.CharacterCollisionLayer) || hitInfo?.HitEntity?.Physics == null)
      {
        if (IsTracer)
        {
          var tracerPos = Position - Direction * tracerLength;
          var clr = _trailColor.ToVector4();
          MyTransparentGeometry.AddLineBillboard(ProjectileConstants.ProjectileTrailLine, clr, tracerPos, (Vector3)Direction, tracerLength, 0.01f, BlendTypeEnum.PostPP);
        }

        return false;
      }
      else
      {
        hitEntity = hitInfo.HitEntity;
        hitPosition = hitInfo.Position;
      }

      if (IsTracer)
      {
        var distanceGunToTarget = Vector3D.Distance(Start, hitPosition);
        tracerLength = (float)Math.Min(tracerLength, distanceGunToTarget);
        var tracerPos = hitPosition - Direction * tracerLength;
        var clr = _trailColor.ToVector4();
        MyTransparentGeometry.AddLineBillboard(ProjectileConstants.ProjectileTrailLine, clr, tracerPos, (Vector3)Direction, tracerLength, 0.01f, BlendTypeEnum.PostPP);
      }

      MySurfaceImpactEnum surfaceEnum;
      MyStringHash materialType;
      MyAPIGateway.Projectiles.GetSurfaceAndMaterial(hitEntity, ref line, ref hitPosition, 0, out surfaceEnum, out materialType);
      
      if (!ProjectileConstants.HitMaterialToEffect.TryGetValue(materialType, out effectName))
      {
        AiSession.Instance.Logger.Log($"BotProjectile.Update: MaterialType '{materialType.String}' not found in dictionary! Entity was {hitEntity?.GetType().FullName ?? "NULL"}", MessageType.WARNING);
        materialType = MyMaterialType.METAL;
      }

      var ent = hitEntity;
      var character = ent as IMyCharacter;
      if (character != null)
      {
        if (AiSession.Instance.IsServer)
        {
          var headMatrix = character.GetHeadMatrix(true);
          var headPosition = headMatrix.Translation + headMatrix.Backward * 0.2;
          var subtype = character.Definition.Id.SubtypeName;

          BotBase bot;
          AiSession.Instance.Bots.TryGetValue(character.EntityId, out bot);

          if (subtype.StartsWith("space_spider", StringComparison.OrdinalIgnoreCase) || subtype.StartsWith("space_wolf", StringComparison.OrdinalIgnoreCase)
            || (bot != null && subtype.StartsWith("default_astronaut", StringComparison.OrdinalIgnoreCase)))
            Damage *= 2;
          else if (Vector3D.DistanceSquared(headPosition, hitPosition) < 0.1)
            Damage *= _headShotMultiplier;

          var nomad = bot as NomadBot;
          if (nomad != null && nomad.Target.Entity == null)
          {
            nomad.SetHostile(Owner);
          }
          else if (AiSession.Instance.Players.ContainsKey(character.ControllerInfo.ControllingIdentityId) || bot?.Owner != null)
          {
            Damage *= AiSession.Instance.ModSaveData.BotDamageModifier;
          }

          AiSession.Instance.DamageCharacter(Owner.EntityId, character, MyDamageType.Bullet, Damage);
        }

        if (MyAPIGateway.Session.Player != null)
        {
          var matrix = character.WorldMatrix;
          matrix.Translation = hitPosition;
          soundName = ProjectileConstants.HitMaterialToSound[materialType];

          MyParticleEffect _;
          MyParticlesManager.TryCreateParticleEffect(effectName, ref matrix, ref hitPosition, uint.MaxValue, out _);

          var soundComp = character.Components?.Get<MyCharacterSoundComponent>();
          if (soundComp != null)
          {
            // WepPlayRifleImpPlay or WepFemPlayRifleImpPlay

            MySoundPair soundPair;
            if (!AiSession.Instance.SoundPairDict.TryGetValue(soundName, out soundPair))
            {
              soundPair = new MySoundPair(soundName);
              AiSession.Instance.SoundPairDict[soundName] = soundPair;
            }

            soundComp.PlayActionSound(soundPair);
          }

          MyHitInfo info = new MyHitInfo()
          {
            Normal = hitInfo.Normal,
            Position = hitPosition,
          };

          MyDecals.HandleAddDecal(hitEntity, info, (Vector3)Direction, materialType, ProjectileConstants.DecalSource_Rifle, damage: (float)Damage, voxelMaterial: materialType, flags: MyDecalFlags.IgnoreOffScreenDeletion);
        }

        return true;
      }

      var grid = ent as IMyCubeGrid;
      if (grid == null)
      {
        // In case we hit a door or subpart
        grid = ent?.GetTopMostParent() as IMyCubeGrid;
      }

      if (grid != null && !grid.MarkedForClose)
      {
        var blockPos = grid.WorldToGridInteger(hitPosition);
        var block = grid.GetCubeBlock(blockPos);

        if (block == null)
        {
          hitPosition -= hitInfo.Normal * grid.GridSize * 0.2f;
          blockPos = grid.WorldToGridInteger(hitPosition);
          block = grid.GetCubeBlock(blockPos);

          if (block == null)
          {
            return true;
          }
        }

        var isServer = AiSession.Instance.IsServer;
        if (isServer)
        {
          BlockDamage *= AiSession.Instance.ModSaveData.BotDamageModifier;

          BotBase bot;
          if (AiSession.Instance.Bots.TryGetValue(Owner.EntityId, out bot) && bot != null)
            BlockDamage *= bot.DamageModifier;

          block.DoDamage(BlockDamage, MyDamageType.Bullet, true);
        }

        block.DoDamage(BlockDamage * 0.5f, MyDamageType.Deformation, isServer);

        if (MyAPIGateway.Session.Player != null)
        {
          Matrix m;
          block.Orientation.GetMatrix(out m);
          MatrixD matrix = m;
          matrix.Translation = hitPosition;

          string material;
          var skin = block.SkinSubtypeId;

          if (skin == ProjectileConstants.BlockSkin_Concrete)
          {
            material = ProjectileConstants.Material_Rock.String;
            materialType = MyMaterialType.ROCK;
          }
          else if (skin == ProjectileConstants.BlockSkin_Wood)
          {
            material = ProjectileConstants.Material_Wood.String;
            materialType = MyMaterialType.WOOD;
          }
          else if (!ProjectileConstants.HitMaterialToEffect.TryGetValue(materialType, out material))
          {
            var blockDef = block.BlockDefinition.Id;
            if (blockDef.SubtypeName.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0
              || blockDef.SubtypeName.IndexOf("transparent", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              material = MyParticleEffectsNameEnum.MaterialHit_Glass;
              materialType = MyStringHash.GetOrCompute("Glass");
            }
            else
            {
              material = MyParticleEffectsNameEnum.MaterialHit_Metal; ;
              materialType = MyMaterialType.METAL;
            }
          }

          var sound = ProjectileConstants.HitMaterialToSound[materialType];

          MyParticleEffect _;
          MyParticlesManager.TryCreateParticleEffect(material, ref matrix, ref hitPosition, uint.MaxValue, out _);

          MyEntity3DSoundEmitter emitter = AiSession.Instance.GetEmitter();
          emitter.SetPosition(hitPosition);

          MySoundPair soundPair;
          if (!AiSession.Instance.SoundPairDict.TryGetValue(sound, out soundPair))
          {
            soundPair = new MySoundPair(sound);
            AiSession.Instance.SoundPairDict[sound] = soundPair;
          }

          emitter.PlaySound(soundPair);
          AiSession.Instance.ReturnEmitter(emitter);

          MyHitInfo info = new MyHitInfo()
          {
            Normal = hitInfo.Normal,
            Position = hitPosition,
          };

          //var defaultType = MyStringHash.GetOrCompute("Default");
          MyDecals.HandleAddDecal(hitEntity, info, (Vector3)Direction, materialType, ProjectileConstants.DecalSource_Rifle, damage: (float)Damage, voxelMaterial: materialType, flags: MyDecalFlags.IgnoreOffScreenDeletion);
        }

        return true;
      }
      else if (hitEntity != null && !hitEntity.MarkedForClose && MyAPIGateway.Session.Player != null)
      {
        soundName = ProjectileConstants.HitMaterialToSound[materialType];
        var position = hitPosition;
        var matrix = MatrixD.Identity;
        matrix.Translation = position;

        MyParticleEffect _;
        MyParticlesManager.TryCreateParticleEffect(effectName, ref matrix, ref hitPosition, uint.MaxValue, out _);

        MyEntity3DSoundEmitter emitter = AiSession.Instance.GetEmitter();
        emitter.SetPosition(hitPosition);

        MySoundPair soundPair;
        if (!AiSession.Instance.SoundPairDict.TryGetValue(soundName, out soundPair))
        {
          soundPair = new MySoundPair(soundName);
          AiSession.Instance.SoundPairDict[soundName] = soundPair;
        }

        emitter.PlaySound(soundPair);
        AiSession.Instance.ReturnEmitter(emitter);

        MyHitInfo info = new MyHitInfo()
        {
          Normal = hitInfo.Normal,
          Position = hitPosition,
        };

        if (AiSession.Instance.IsServer)
          Damage *= AiSession.Instance.ModSaveData.BotDamageModifier;
  
        MyDecals.HandleAddDecal(hitEntity, info, (Vector3)Direction, materialType, ProjectileConstants.DecalSource_Rifle, damage: (float)Damage, voxelMaterial: materialType, flags: MyDecalFlags.IgnoreOffScreenDeletion);
        return true;
      }

      return hitEntity != null;
    }
  }
}
