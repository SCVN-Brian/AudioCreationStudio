using System;

namespace AudioCreationStudio.DSP
{
    /// <summary>
    /// Pan laws.
    /// • Mono source → stereo bus: constant power (sin/cos), -3.01 dB per side at centre, so perceived
    ///   loudness stays constant across the pan range.
    /// • Stereo source / stereo bus: balance law, unity at centre; panning attenuates the opposite side linearly.
    /// </summary>
    public static class PanLaw
    {
        public static void MonoToStereo(float pan, out float left, out float right)
        {
            double a = (DspMath.Clamp(pan, -1f, 1f) + 1.0) * Math.PI * 0.25;
            left = (float)Math.Cos(a);
            right = (float)Math.Sin(a);
        }

        public static void Balance(float pan, out float left, out float right)
        {
            pan = DspMath.Clamp(pan, -1f, 1f);
            left = pan > 0f ? 1f - pan : 1f;
            right = pan < 0f ? 1f + pan : 1f;
        }
    }
}
