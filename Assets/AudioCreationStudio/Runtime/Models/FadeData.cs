using System;

namespace AudioCreationStudio.Models
{
    [Serializable]
    public class FadeData
    {
        /// <summary>Fade length in timeline samples.</summary>
        public long lengthSamples;
        public FadeCurve curve = FadeCurve.EqualPower;

        public FadeData Clone() => new FadeData { lengthSamples = lengthSamples, curve = curve };
    }
}
