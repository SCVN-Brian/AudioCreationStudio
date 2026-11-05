namespace AudioCreationStudio.DSP
{
    /// <summary>
    /// Format-level channel conversion (used by export). Mono → Stereo duplicates the signal
    /// (no pan law: the file should sound identical). Stereo → Mono averages (L+R)/2, which avoids
    /// clipping when channels are correlated. Channels beyond the first two are ignored.
    /// </summary>
    public static class ChannelConverter
    {
        public static AudioBuffer Convert(AudioBuffer src, int targetChannels)
        {
            if (targetChannels <= 0 || targetChannels == src.Channels) return src.Clone();
            var dst = new AudioBuffer(targetChannels, src.Frames, src.SampleRate);
            if (targetChannels == 1)
            {
                var d = dst.GetChannel(0);
                if (src.Channels == 1) { System.Array.Copy(src.GetChannel(0), d, src.Frames); return dst; }
                var l = src.GetChannel(0);
                var r = src.GetChannel(1);
                for (int i = 0; i < src.Frames; i++) d[i] = 0.5f * (l[i] + r[i]);
                return dst;
            }
            for (int c = 0; c < targetChannels; c++)
            {
                var s = src.GetChannel(src.Channels == 1 ? 0 : System.Math.Min(c, src.Channels - 1));
                System.Array.Copy(s, dst.GetChannel(c), src.Frames);
            }
            return dst;
        }
    }
}
