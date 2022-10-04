using Sandbox.Game.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Particles
{
  public class BuilderParticleInfo : ParticleInfoBase
  {
    public IMySlimBlock Block;
    public string ParticleName;
    public MatrixD ParticleMatrix;
    public int RotationCount;
    public MyEntity3DSoundEmitter SoundEmitter;
    public MySoundPair SoundPair;

    public BuilderParticleInfo(IMyCharacter bot, IMySlimBlock block, bool isWelder)
    {
      Bot = bot;
      Block = block;
      ParticleName = isWelder ? MyParticleEffectsNameEnum.WelderContactPoint : MyParticleEffectsNameEnum.AngleGrinder;

      var sound = isWelder ? "ToolPlayWeldMetal" : "ToolPlayGrindMetal";
      if (!AiSession.Instance.SoundPairDict.TryGetValue(sound, out SoundPair))
      {
        SoundPair = new MySoundPair(sound);
        AiSession.Instance.SoundPairDict[sound] = SoundPair;
      }

      SoundEmitter = AiSession.Instance.GetEmitter(bot as MyEntity);
      SoundEmitter.PlaySound(SoundPair);
    }

    public Vector3D Position
    {
      get
      {
        Vector3D pos = Vector3D.Zero;
        Block?.ComputeWorldCenter(out pos);
        return pos;
      }
    }

    public MatrixD WorldMatrix
    {
      get
      {
        Matrix m;
        Block.Orientation.GetMatrix(out m);
        m.Translation = (Vector3)Position;
        return m;
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
      SoundEmitter?.StopSound(false);
    }

    public void Set(IMySlimBlock block)
    {
      Stop();
      Block = block;

      //MyLog.Default.WriteLine($"Playing sound: {SoundPair.SoundId}");
      SoundEmitter?.PlaySound(SoundPair);
    }

    public override void Set(IMyCharacter bot)
    {
      Stop();
      Bot = bot;
    }

    public override void Update()
    {
      if (Effects.Count == 0)
      {
        ParticleMatrix = WorldMatrix;
        var position = Position;
        MyParticleEffect particle;
        if (MyParticlesManager.TryCreateParticleEffect(ParticleName, ref ParticleMatrix, ref position, uint.MaxValue, out particle))
        {
          particle.UserScale = 3;
          Effects.Add(particle);
          particle.OnDelete += Particle_OnDelete;
        }
      }
    }

    private void Particle_OnDelete(MyParticleEffect effect)
    {
      effect.OnDelete -= Particle_OnDelete;
      Effects?.Clear();
    }
  }
}
