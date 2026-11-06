using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.Export
{
    /// <summary>
    /// MP3 (libmp3lame) and OGG Vorbis (libvorbis, or ffmpeg's built-in 'vorbis' encoder as a fallback) through an
    /// external ffmpeg executable. The mix is handed over as a lossless 32-bit float WAV, so ffmpeg is the only
    /// lossy step. Editor-only in practice: spawning processes is not available on most player platforms.
    /// </summary>
    public static class FfmpegEncoder
    {
        public static readonly int[] Mp3Rates = { 8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000 };
        public static readonly int[] Bitrates = { 64, 96, 128, 160, 192, 256, 320 };

        private static readonly Dictionary<string, string> EncoderListCache = new Dictionary<string, string>();

        /// <summary>Finds ffmpeg: the preferred path if it exists, then PATH, then common install folders. Null when missing.</summary>
        public static string Locate(string preferred = null)
        {
            if (!string.IsNullOrEmpty(preferred) && File.Exists(preferred)) return preferred;
            bool win = Path.DirectorySeparatorChar == '\\';
            string exe = win ? "ffmpeg.exe" : "ffmpeg";
            var dirs = new List<string>();
            var path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path)) dirs.AddRange(path.Split(Path.PathSeparator));
            if (win)
            {
                dirs.Add(@"C:\ffmpeg\bin");
                dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin"));
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                dirs.Add(Path.Combine(local, "Microsoft", "WinGet", "Links"));
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                dirs.Add(Path.Combine(home, "scoop", "shims"));
                dirs.Add(@"C:\ProgramData\chocolatey\bin");
            }
            else
            {
                // Unity launched from the Dock/Hub does not inherit the shell PATH, so check Homebrew/MacPorts/Linux defaults.
                dirs.AddRange(new[] { "/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin", "/usr/bin", "/snap/bin" });
            }
            foreach (var d in dirs)
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                try
                {
                    var p = Path.Combine(d.Trim().Trim('"'), exe);
                    if (File.Exists(p)) return p;
                }
                catch (ArgumentException) { /* malformed PATH entry */ }
            }
            return null;
        }

        public static bool HasEncoder(string ffmpeg, string name)
        {
            string list;
            lock (EncoderListCache)
            {
                if (!EncoderListCache.TryGetValue(ffmpeg, out list))
                {
                    list = Run(ffmpeg, "-hide_banner -encoders", CancellationToken.None, 15000, out _, out var stdout) == 0 ? stdout : "";
                    EncoderListCache[ffmpeg] = list;
                }
            }
            foreach (var line in list.Split('\n'))
            {
                var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1] == name) return true;
            }
            return false;
        }

        /// <summary>The nearest MP3-legal sample rate at or above <paramref name="rate"/> (48 kHz max).</summary>
        public static int Mp3Rate(int rate)
        {
            foreach (var r in Mp3Rates) if (r >= rate) return r;
            return 48000;
        }

        /// <summary>Encodes <paramref name="buffer"/> to <paramref name="outPath"/>. Returns a note describing any format adjustments ffmpeg needed.</summary>
        public static string Encode(AudioBuffer buffer, AudioFileType type, int kbps, string ffmpegPath, string outPath,
            CancellationToken ct = default, IProgress<float> progress = null)
        {
            if (type != AudioFileType.Mp3 && type != AudioFileType.Ogg) throw new ArgumentException("ffmpeg is only used for MP3 and OGG.", nameof(type));
            string ffmpeg = Locate(ffmpegPath) ?? throw new FileNotFoundException(
                "ffmpeg was not found. Install it (macOS: 'brew install ffmpeg', Windows: 'winget install ffmpeg') " +
                "or set its path in the export settings. WAV, AIFF and FLAC export do not need it.");
            if (kbps <= 0) kbps = 192;

            var args = new StringBuilder("-hide_banner -nostdin -y -v error ");
            string src = outPath + ".src.wav~";
            args.Append("-i ").Append(Quote(src)).Append(" -map_metadata -1 ");
            var notes = new List<string>();
            int ch = buffer.Channels;

            if (type == AudioFileType.Mp3)
            {
                args.Append("-c:a libmp3lame ");
                if (ch > 2) { args.Append("-ac 2 "); notes.Add($"downmixed {ch}ch → stereo (MP3 limit)"); }
                int rate = Mp3Rate(buffer.SampleRate);
                if (rate != buffer.SampleRate) { args.Append("-ar ").Append(rate).Append(' '); notes.Add($"resampled to {rate} Hz (MP3 limit)"); }
                args.Append("-b:a ").Append(kbps).Append("k -f mp3 ");
            }
            else if (HasEncoder(ffmpeg, "libvorbis"))
            {
                args.Append("-c:a libvorbis -b:a ").Append(kbps).Append("k -f ogg ");
            }
            else
            {
                // ffmpeg's native Vorbis encoder is experimental and only accepts stereo.
                args.Append("-c:a vorbis -strict -2 ");
                if (ch != 2) { args.Append("-ac 2 "); notes.Add($"{ch}ch → stereo (ffmpeg built-in Vorbis is stereo-only; install an ffmpeg with libvorbis to keep mono)"); }
                args.Append("-b:a ").Append(kbps).Append("k -f ogg ");
            }
            args.Append(Quote(outPath));

            try
            {
                using (var fs = new FileStream(src, FileMode.Create, FileAccess.Write, FileShare.None))
                    WavEncoder.Write(fs, buffer, WavSampleFormat.Float32, ct: ct);
                progress?.Report(0.3f);
                ct.ThrowIfCancellationRequested();

                int code = Run(ffmpeg, args.ToString(), ct, 10 * 60 * 1000, out string err, out _);
                ct.ThrowIfCancellationRequested();
                if (code != 0) throw new IOException($"ffmpeg failed (exit {code}): {LastLines(err)}");
                progress?.Report(0.85f);

                if (!File.Exists(outPath) || new FileInfo(outPath).Length == 0) throw new IOException("ffmpeg produced no output.");
                if (!HasMagic(outPath, type)) throw new IOException($"ffmpeg output is not a valid {type} file.");
                // Decode the result once so a broken stream is never moved into place.
                code = Run(ffmpeg, $"-hide_banner -nostdin -v error -i {Quote(outPath)} -f null -", ct, 5 * 60 * 1000, out err, out _);
                if (code != 0) throw new IOException($"Encoded {type} failed to decode: {LastLines(err)}");
                progress?.Report(1f);
                return string.Join("; ", notes);
            }
            finally
            {
                try { if (File.Exists(src)) File.Delete(src); } catch { /* best effort */ }
            }
        }

        public static bool HasMagic(string path, AudioFileType type)
        {
            var b = new byte[4];
            using (var fs = File.OpenRead(path)) if (fs.Read(b, 0, 4) < 4) return false;
            if (type == AudioFileType.Ogg) return b[0] == 'O' && b[1] == 'g' && b[2] == 'g' && b[3] == 'S';
            bool id3 = b[0] == 'I' && b[1] == 'D' && b[2] == '3';
            bool sync = b[0] == 0xFF && (b[1] & 0xE0) == 0xE0;
            return id3 || sync;
        }

        private static string Quote(string p) => "\"" + p.Replace("\"", "\\\"") + "\"";

        private static string LastLines(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "(no output)";
            var lines = s.Trim().Split('\n');
            int from = Math.Max(0, lines.Length - 3);
            return string.Join(" | ", lines, from, lines.Length - from).Trim();
        }

        private static int Run(string exe, string args, CancellationToken ct, int timeoutMs, out string stderr, out string stdout)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true,
            };
            var err = new StringBuilder();
            var outp = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (outp) outp.AppendLine(e.Data); };
                p.Start();
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
                var sw = Stopwatch.StartNew();
                while (!p.WaitForExit(50))
                {
                    if (ct.IsCancellationRequested || sw.ElapsedMilliseconds > timeoutMs)
                    {
                        try { p.Kill(); } catch { /* already exited */ }
                        p.WaitForExit(2000);
                        ct.ThrowIfCancellationRequested();
                        throw new TimeoutException("ffmpeg timed out.");
                    }
                }
                p.WaitForExit(); // flush async readers
                lock (err) stderr = err.ToString();
                lock (outp) stdout = outp.ToString();
                return p.ExitCode;
            }
        }
    }
}
