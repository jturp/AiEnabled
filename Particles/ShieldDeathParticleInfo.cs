using Sandbox.Game.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Particles
{
  public class ShieldDeathParticleInfo : ParticleInfoBase
  {
    Vector3D _position;
    int _ticksLeft = 60;
    readonly long _key;
    public MyEntity3DSoundEmitter SoundEmitter;
    public MySoundPair SoundPair;

    public ShieldDeathParticleInfo(Vector3D pos, long key)
    {
      Type = ParticleType.Shield;
      _position = pos;
      _key = key;

      var sound = "ParticleElectricalDischarge";
      if (!AiSession.Instance.SoundPairDict.TryGetValue(sound, out SoundPair))
      {
        SoundPair = new MySoundPair(sound);
        AiSession.Instance.SoundPairDict[sound] = SoundPair;
      }

      SoundEmitter = AiSession.Instance.GetEmitter();
      SoundEmitter.SetPosition(_position);
      SoundEmitter.PlaySound(SoundPair);
    }

    public override void Set(IMyCharacter bot) { }

    public override void Update()
    {
      _ticksLeft--;

      if (_ticksLeft <= 0)
      {
        Close();
        ParticleInfoBase _;
        AiSession.Instance.ParticleInfoDict.TryRemove(_key, out _);

        ParticleInfoClient __;
        AiSession.Instance.ParticleDictionary.TryRemove(_key, out __);

        return;
      }
      else if (_ticksLeft < 30 && SoundEmitter?.IsPlaying == true)
      {
        SoundEmitter.StopSound(false);
      }

      if (Effects.Count == 0)
      {
        var matrix = MatrixD.Identity;
        matrix.Translation = _position;

        MyParticleEffect particle;
        if (MyParticlesManager.TryCreateParticleEffect(MyParticleEffectsNameEnum.Damage_Electrical_Damaged, ref matrix, ref _position, uint.MaxValue, out particle))
        {
          particle.UserScale = 0.3f;
          particle.UserColorMultiplier = new Vector4(0.25f);
          particle.OnDelete += Particle_OnDelete;
          Effects.Add(particle);
        }
      }
    }

    public override void Close()
    {
      base.Close();
      SoundEmitter?.Cleanup();
      AiSession.Instance.ReturnEmitter(SoundEmitter);
    }

    public override void Stop()
    {
      base.Stop();
      SoundEmitter?.StopSound(true);
    }

    private void Particle_OnDelete(MyParticleEffect effect)
    {
      effect.OnDelete -= Particle_OnDelete;
      Effects?.Clear();
    }
  }
}
