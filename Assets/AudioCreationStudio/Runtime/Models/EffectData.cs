using System;

namespace AudioCreationStudio.Models
{
    /// <summary>
    /// Serializable effect instance. Parameters are stored as a float array aligned with the
    /// <see cref="DSP.EffectDefinition"/> registered for <see cref="typeId"/>, which keeps
    /// serialization trivial (no polymorphic references) and lets new effect types be added
    /// without touching the model (Open/Closed).
    /// </summary>
    [Serializable]
    public class EffectData
    {
        public string uid;
        public string typeId;
        /// <summary>Disabled effects are removed from processing entirely (no state, no tail).</summary>
        public bool enabled = true;
        /// <summary>Bypassed effects pass audio through untouched while keeping their slot and state.</summary>
        public bool bypass;
        public float[] values = Array.Empty<float>();
        public bool expanded = true;

        public EffectData Clone(bool newUid = false)
        {
            return new EffectData
            {
                uid = newUid ? Uid.New() : uid,
                typeId = typeId,
                enabled = enabled,
                bypass = bypass,
                values = (float[])(values ?? Array.Empty<float>()).Clone(),
                expanded = expanded
            };
        }
    }

    public static class Uid
    {
        public static string New() => Guid.NewGuid().ToString("N").Substring(0, 12);
    }
}
