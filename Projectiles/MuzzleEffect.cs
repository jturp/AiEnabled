using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using System.Collections.Generic;

using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;

using VRageMath;

namespace AiEnabled.Projectiles
{
  internal class MuzzleEffect
  {
    public double StartTime;
    public int Duration;
    MyGunBase _gun;
    IMyEntity _tool;
    //IMyCharacter _bot;
    //readonly List<ProjectileInfo.WeaponEffect> _effects = new List<ProjectileInfo.WeaponEffect>();

    public void Start(MyGunBase gun, IMyCharacter bot)
    {
      if (gun == null || bot == null)
        return;

      _gun = gun;
      //_bot = bot;
      _tool = bot.EquippedTool;

      StartTime = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;
      Duration = gun.MuzzleFlashLifeSpan;
      var renderId = (gun.IsUserControllableGunBlock && _tool?.Render != null) ? _tool.Render.GetRenderObjectID() : uint.MaxValue;
      gun.CreateEffects(MyWeaponDefinition.WeaponEffectAction.Shoot, renderId, false);

      _tool.OnMarkForClose += EquippedTool_OnMarkForClose;
      _tool.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
      _tool.NeedsWorldMatrix = true;

      //var translationAddition = (bot.Physics?.LinearVelocity / 60f) ?? Vector3.Zero;
      //_effects.Clear();

      //var matrix = gun.GetMuzzleWorldMatrix();
      //var position = gun.GetMuzzleWorldPosition();
      //matrix.Translation += translationAddition;

      //for (int i = 0; i < gun.WeaponDefinition.WeaponEffects.Length; i++)
      //{
      //  var eff = gun.WeaponDefinition.WeaponEffects[i];
      //  if (eff == null || eff.Action != MyWeaponDefinition.WeaponEffectAction.Shoot)
      //    continue;

      //  MyParticleEffect particle;
      //  if (MyParticlesManager.TryCreateParticleEffect(eff.Particle, ref matrix, ref position, renderId, out particle) && particle.Loop)
      //  {
      //    var effect = AiSession.Instance.Projectiles.WeaponEffects.Count > 0 ? AiSession.Instance.Projectiles.WeaponEffects.Pop() : new ProjectileInfo.WeaponEffect();
      //    effect.Set(particle, eff);

      //    _effects.Add(effect);
      //  }
      //}
    }

    private void EquippedTool_OnMarkForClose(IMyEntity obj)
    {
      if (obj != null)
        obj.OnMarkForClose -= EquippedTool_OnMarkForClose;
  
      Stop();
    }

    public bool Update()
    {
      if (_gun == null)
        return false;

      _tool.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
      _tool.NeedsWorldMatrix = true;

      //var matrix = _gun.GetMuzzleWorldMatrix();
      //matrix.Translation += (_bot?.Physics?.LinearVelocity) / 60f ?? Vector3.Zero;

      //var position = _gun.GetMuzzleWorldPosition();

      //for (int i = 0; i < _effects.Count; i++)
      //{
      //  var weaponEffect = _effects[i];
      //  var pEff = weaponEffect.ParticleEffect;
      //  var def = weaponEffect.Effect;
      //  pEff.UserBirthMultiplier = MathHelper.Clamp(pEff.UserBirthMultiplier - def.ParticleBirthDecrease, def.ParticleBirthMin, def.ParticleBirthMax);
      //  pEff.WorldMatrix = matrix;
      //}

      _gun.UpdateEffectPositions();
      _gun.UpdateEffects();

      var time = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds - StartTime;
      if (time > _gun.ReleaseTimeAfterFire)
        return false;

      Duration--;
      return Duration > 0;
    }

    public void Stop()
    {
      if (_tool != null)
        _tool.OnMarkForClose -= EquippedTool_OnMarkForClose;

      //for (int i = 0; i < _effects.Count; i++)
      //{
      //  var eff = _effects[i];
      //  eff.ParticleEffect.Stop(eff.Effect.InstantStop);
      //  AiSession.Instance.Projectiles.WeaponEffects.Push(eff);
      //}

      //_effects.Clear();

      _gun?.RemoveAllEffects();
    }
  }
}
