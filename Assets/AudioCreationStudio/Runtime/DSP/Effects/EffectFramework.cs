using System;
using System.Collections.Generic;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.DSP
{
    public enum ParamScale { Linear, Logarithmic }

    /// <summary>Describes one effect parameter: range, default, display unit and optional discrete choices.</summary>
    public sealed class ParamDef
    {
        public readonly string Id;
        public readonly string Name;
        public readonly float Min, Max, Default;
        public readonly string Unit;
        public readonly ParamScale Scale;
        public readonly string[] Choices;
        public readonly bool Automatable;

        public ParamDef(string id, string name, float min, float max, float def, string unit = "",
            ParamScale scale = ParamScale.Linear, string[] choices = null, bool automatable = true)
        {
            Id = id; Name = name; Min = min; Max = max; Default = def; Unit = unit;
            Scale = scale; Choices = choices; Automatable = automatable && choices == null;
        }

        public bool IsChoice => Choices != null && Choices.Length > 0;

        public float Clamp(float v)
        {
            if (float.IsNaN(v)) return Default;
            v = DspMath.Clamp(v, Min, Max);
            return IsChoice ? (float)Math.Round(v) : v;
        }

        /// <summary>Maps a value to a 0..1 slider position honouring the parameter scale.</summary>
        public float ToNormalized(float v)
        {
            v = Clamp(v);
            if (Scale == ParamScale.Logarithmic && Min > 0)
                return (float)(Math.Log(v / Min) / Math.Log(Max / Min));
            return Max > Min ? (v - Min) / (Max - Min) : 0f;
        }

        public float FromNormalized(float n)
        {
            n = DspMath.Clamp(n, 0f, 1f);
            if (Scale == ParamScale.Logarithmic && Min > 0)
                return Clamp((float)(Min * Math.Pow(Max / Min, n)));
            return Clamp(Min + (Max - Min) * n);
        }

        public string Format(float v)
        {
            if (IsChoice) return Choices[(int)DspMath.Clamp((float)Math.Round(v), 0, Choices.Length - 1)];
            if (Unit == "Hz" && v >= 1000f) return $"{v / 1000f:0.##} kHz";
            float a = Math.Abs(v);
            string num = a >= 100 ? v.ToString("0") : a >= 10 ? v.ToString("0.0") : v.ToString("0.00");
            return string.IsNullOrEmpty(Unit) ? num : $"{num} {Unit}";
        }
    }

    /// <summary>A stateful DSP processor instance. Not thread-safe; one instance per render chain.</summary>
    public interface IEffectProcessor
    {
        void Prepare(int sampleRate, int channels, int maxBlockFrames);
        /// <summary>Applies a new parameter set (aligned with the definition). Called between blocks.</summary>
        void SetParameters(float[] values);
        /// <summary>Processes <paramref name="frames"/> frames of <paramref name="buffer"/> in place.</summary>
        void Process(AudioBuffer buffer, int offset, int frames);
        void Reset();
        /// <summary>Frames of audible output after the input stops (delay/reverb tails).</summary>
        int GetTailFrames();
    }

    public abstract class EffectProcessorBase : IEffectProcessor
    {
        protected int SampleRate = 48000;
        protected int Channels = 2;
        protected float[] Values = Array.Empty<float>();

        public virtual void Prepare(int sampleRate, int channels, int maxBlockFrames)
        {
            SampleRate = sampleRate;
            Channels = channels;
            OnPrepare(maxBlockFrames);
            Reset();
        }

        public void SetParameters(float[] values)
        {
            Values = values;
            OnParametersChanged();
        }

        protected virtual void OnPrepare(int maxBlockFrames) { }
        protected virtual void OnParametersChanged() { }
        public abstract void Process(AudioBuffer buffer, int offset, int frames);
        public virtual void Reset() { }
        public virtual int GetTailFrames() => 0;

        protected float P(int index) => index < Values.Length ? Values[index] : 0f;
    }

    public sealed class EffectDefinition
    {
        public readonly string TypeId;
        public readonly string DisplayName;
        public readonly string Category;
        public readonly string Description;
        public readonly ParamDef[] Params;
        private readonly Func<IEffectProcessor> _factory;

        public EffectDefinition(string typeId, string displayName, string category, string description,
            ParamDef[] parameters, Func<IEffectProcessor> factory)
        {
            TypeId = typeId; DisplayName = displayName; Category = category; Description = description;
            Params = parameters; _factory = factory;
        }

        public IEffectProcessor CreateProcessor() => _factory();

        public int IndexOf(string paramId)
        {
            for (int i = 0; i < Params.Length; i++) if (Params[i].Id == paramId) return i;
            return -1;
        }

        public float[] DefaultValues()
        {
            var v = new float[Params.Length];
            for (int i = 0; i < v.Length; i++) v[i] = Params[i].Default;
            return v;
        }

        /// <summary>Returns a clamped copy aligned with <see cref="Params"/>, padding missing values with defaults.</summary>
        public float[] Normalize(float[] values)
        {
            var v = new float[Params.Length];
            for (int i = 0; i < v.Length; i++)
                v[i] = values != null && i < values.Length ? Params[i].Clamp(values[i]) : Params[i].Default;
            return v;
        }
    }

    /// <summary>
    /// Registry of available effects. Built-ins register themselves; projects can register more effect
    /// types without modifying the engine (Open/Closed principle).
    /// </summary>
    public static class EffectRegistry
    {
        private static readonly Dictionary<string, EffectDefinition> Map = new Dictionary<string, EffectDefinition>();
        private static readonly List<EffectDefinition> Ordered = new List<EffectDefinition>();
        private static readonly object Lock = new object();

        static EffectRegistry() => BuiltInEffects.RegisterAll();

        public static IReadOnlyList<EffectDefinition> All
        {
            get { lock (Lock) return Ordered.ToArray(); }
        }

        public static void Register(EffectDefinition def)
        {
            lock (Lock)
            {
                if (Map.ContainsKey(def.TypeId)) Ordered.RemoveAll(d => d.TypeId == def.TypeId);
                Map[def.TypeId] = def;
                Ordered.Add(def);
            }
        }

        public static EffectDefinition Get(string typeId)
        {
            if (typeId == null) return null;
            lock (Lock) return Map.TryGetValue(typeId, out var d) ? d : null;
        }

        public static EffectData CreateData(string typeId)
        {
            var def = Get(typeId) ?? throw new ArgumentException($"Unknown effect type '{typeId}'");
            return new EffectData { uid = Uid.New(), typeId = typeId, values = def.DefaultValues() };
        }
    }

    /// <summary>
    /// Runs an ordered list of <see cref="EffectData"/> as a chain. Order in the list is processing order.
    /// Disabled effects are skipped entirely; bypassed effects pass audio through untouched.
    /// </summary>
    public sealed class EffectChainProcessor
    {
        private sealed class Slot
        {
            public EffectData Data;
            public EffectDefinition Def;
            public IEffectProcessor Proc;
            public float[] Values;
            public AutomationLane[] Lanes;
        }

        private readonly List<Slot> _slots = new List<Slot>();
        public int SampleRate { get; }
        public int Count => _slots.Count;

        public EffectChainProcessor(IReadOnlyList<EffectData> effects, int sampleRate, int channels, int maxBlockFrames,
            IReadOnlyList<AutomationLane> automation = null)
        {
            SampleRate = sampleRate;
            if (effects == null) return;
            foreach (var e in effects)
            {
                if (e == null || !e.enabled) continue;
                var def = EffectRegistry.Get(e.typeId);
                if (def == null) continue;
                var proc = def.CreateProcessor();
                proc.Prepare(sampleRate, channels, maxBlockFrames);
                var values = def.Normalize(e.values);
                proc.SetParameters(values);
                var lanes = new AutomationLane[def.Params.Length];
                bool any = false;
                if (automation != null)
                {
                    for (int i = 0; i < def.Params.Length; i++)
                    {
                        string target = AutomationTargets.Effect(e.uid, def.Params[i].Id);
                        foreach (var l in automation)
                            if (l.targetId == target && l.IsActive) { lanes[i] = l; any = true; }
                    }
                }
                _slots.Add(new Slot { Data = e, Def = def, Proc = proc, Values = values, Lanes = any ? lanes : null });
            }
        }

        public int GetTailFrames()
        {
            long tail = 0;
            foreach (var s in _slots) if (!s.Data.bypass) tail += s.Proc.GetTailFrames();
            return (int)Math.Min(tail, SampleRate * 10L);
        }

        /// <summary>Evaluates parameter automation at <paramref name="time"/> (timeline samples).</summary>
        public void ApplyAutomation(long time)
        {
            foreach (var s in _slots)
            {
                if (s.Lanes == null) continue;
                bool changed = false;
                for (int i = 0; i < s.Lanes.Length; i++)
                {
                    var lane = s.Lanes[i];
                    if (lane == null) continue;
                    float v = s.Def.Params[i].Clamp(lane.Evaluate(time, s.Values[i]));
                    if (v != s.Values[i]) { s.Values[i] = v; changed = true; }
                }
                if (changed) s.Proc.SetParameters(s.Values);
            }
        }

        public void Process(AudioBuffer buffer, int offset, int frames)
        {
            foreach (var s in _slots)
            {
                if (s.Data.bypass) continue;
                s.Proc.Process(buffer, offset, frames);
            }
        }

        public void Reset()
        {
            foreach (var s in _slots) s.Proc.Reset();
        }
    }
}
