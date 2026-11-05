using System;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.DSP
{
    /// <summary>
    /// Renders one clip additively into a bus. Every timeline sample's source position is computed
    /// directly from its integer index (no accumulated phase), so long clips stay sample-accurate.
    /// </summary>
    public static class ClipRenderer
    {
        public static void Render(ClipData clip, AudioBuffer source, AudioBuffer bus, int timelineRate,
            ISampleInterpolator interpolator)
        {
            if (clip == null || source == null || !clip.enabled || clip.lengthSamples <= 0 || source.Frames == 0) return;
            interpolator ??= SincInterpolator.Shared;

            double step = clip.playbackRate * source.SampleRate / timelineRate;
            if (!(step > 0) || double.IsInfinity(step)) return;
            bool exact = Math.Abs(step - 1.0) < 1e-12;
            float cutoff = step > 1.0 ? (float)(Resampler.DownsampleMargin / step) : 1f;

            int srcLen = source.Frames;
            int srcCh = source.Channels;
            int outCh = bus.Channels;
            long start = clip.startSample;
            long tBegin = Math.Max(0, start);
            long tEnd = Math.Min(start + clip.lengthSamples, bus.Frames);
            if (tEnd <= tBegin) return;

            var s0 = source.GetChannel(0);
            var s1 = srcCh > 1 ? source.GetChannel(1) : s0;
            var outL = bus.GetChannel(0);
            var outR = outCh > 1 ? bus.GetChannel(1) : null;

            var gainLane = clip.FindLane(AutomationTargets.Gain);
            var panLane = clip.FindLane(AutomationTargets.Pan);
            bool gainAuto = gainLane != null && gainLane.IsActive;
            bool panAuto = panLane != null && panLane.IsActive;
            float staticGain = DspMath.DbToGain(clip.gainDb);
            float pl, pr;
            ComputePan(clip.pan, srcCh, out pl, out pr);

            for (long t = tBegin; t < tEnd; t++)
            {
                long local = t - start;
                double pos = clip.sourceOffset + local * step;
                if (clip.loop) pos = DspMath.Mod(pos, srcLen);
                else if (pos >= srcLen) break;
                if (pos < 0) continue;

                float g = gainAuto ? DspMath.DbToGain(gainLane.Evaluate(local, clip.gainDb)) : staticGain;
                g *= FadeCurves.ClipFadeGain(clip, local);
                if (g == 0f) continue;
                if (panAuto) ComputePan(panLane.Evaluate(local, clip.pan), srcCh, out pl, out pr);

                float a, b;
                if (exact)
                {
                    long idx = (long)pos;
                    a = s0[idx];
                    b = srcCh > 1 ? s1[idx] : a;
                }
                else
                {
                    a = interpolator.Sample(s0, srcLen, pos, cutoff, clip.loop);
                    b = srcCh > 1 ? interpolator.Sample(s1, srcLen, pos, cutoff, clip.loop) : a;
                }

                if (outR == null)
                {
                    float v = srcCh > 1 ? 0.5f * (a + b) : a;
                    outL[t] += v * g;
                }
                else
                {
                    outL[t] += a * g * pl;
                    outR[t] += b * g * pr;
                }
            }
        }

        private static void ComputePan(float pan, int srcChannels, out float l, out float r)
        {
            if (srcChannels == 1) PanLaw.MonoToStereo(pan, out l, out r);
            else PanLaw.Balance(pan, out l, out r);
        }
    }
}
