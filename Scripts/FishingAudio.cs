using System;
using System.Collections.Generic;
using System.IO;
using Helpers;
using UnityEngine;
using UnityEngine.Audio;

namespace FishingMod
{
    internal enum FishingSound
    {
        Cast,
        BobberSplash,
        ReelOut,
        ReelIn,
        QteSuccess,
        QteFailure,
        FishLanded,
        LineSnap
    }

    internal sealed class FishingAudio : IDisposable
    {
        private const int SourcePoolSize = 6;

        private static readonly KeyValuePair<FishingSound, string>[] Specs =
        {
            new KeyValuePair<FishingSound, string>(FishingSound.Cast, "cast-whoosh.wav"),
            new KeyValuePair<FishingSound, string>(FishingSound.BobberSplash, "bobber-splash.wav"),
            new KeyValuePair<FishingSound, string>(FishingSound.ReelOut, "reel-out.wav"),
            new KeyValuePair<FishingSound, string>(FishingSound.ReelIn, "reel-in.wav"),
            new KeyValuePair<FishingSound, string>(FishingSound.QteSuccess, "qte-success.wav"),
            new KeyValuePair<FishingSound, string>(FishingSound.QteFailure, "qte-failure.wav"),
            new KeyValuePair<FishingSound, string>(FishingSound.FishLanded, "fish-landed.wav"),
            new KeyValuePair<FishingSound, string>(FishingSound.LineSnap, "line-snap.wav")
        };

        private readonly Dictionary<FishingSound, AudioClip> _clips = new Dictionary<FishingSound, AudioClip>();
        private readonly List<AudioSource> _sources = new List<AudioSource>(SourcePoolSize);
        private Action<string> _log;
        private int _nextSource;
        private bool _disposed;

        internal static IReadOnlyList<KeyValuePair<FishingSound, string>> RequiredSounds => Specs;
        internal int LoadedClipCount => _clips.Count;

        internal void Initialize(GameObject host, string modRootPath, Action<string> log)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (string.IsNullOrWhiteSpace(modRootPath)) throw new ArgumentException("The mod root path is required.", nameof(modRootPath));
            _log = log ?? (_ => { });

            string root = Path.GetFullPath(modRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string soundsRoot = Path.GetFullPath(Path.Combine(root, "Sounds"));
            string rootPrefix = root + Path.DirectorySeparatorChar;
            if (!soundsRoot.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fishing audio path escaped the mod root.");

            AudioMixerGroup mixer = TryGetNativeEffectsMixer();
            for (int i = 0; i < SourcePoolSize; i++)
            {
                AudioSource source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.priority = 96;
                source.outputAudioMixerGroup = mixer;
                _sources.Add(source);
            }

            long loadedBytes = 0;
            for (int i = 0; i < Specs.Length; i++)
            {
                KeyValuePair<FishingSound, string> spec = Specs[i];
                string path = Path.GetFullPath(Path.Combine(soundsRoot, spec.Value));
                string soundsPrefix = soundsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (!path.StartsWith(soundsPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Fishing sound path escaped the Sounds folder.");

                try
                {
                    FishingWaveData wave = FishingWaveDecoder.Load(path);
                    AudioClip clip = AudioClip.Create(
                        "FishingMod_" + spec.Key,
                        wave.FrameCount,
                        wave.Channels,
                        wave.SampleRate,
                        stream: false);
                    if (!clip.SetData(wave.Samples, 0))
                    {
                        UnityEngine.Object.Destroy(clip);
                        throw new InvalidDataException("Unity rejected decoded WAV samples.");
                    }

                    _clips.Add(spec.Key, clip);
                    loadedBytes += new FileInfo(path).Length;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[FishingMod] Sound unavailable: " + spec.Value + " (" + exception.Message + ").");
                }
            }

            _log("[FishingMod] Loaded " + _clips.Count + "/" + Specs.Length
                + " fishing sounds (" + loadedBytes + " bytes) from " + soundsRoot + ".");
        }

        internal void Play(FishingSound sound, float volume, float pitch = 1f)
        {
            if (_disposed || _sources.Count == 0 || !_clips.TryGetValue(sound, out AudioClip clip) || clip == null)
                return;

            AudioSource source = _sources[_nextSource++ % _sources.Count];
            if (source == null) return;
            source.Stop();
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, 0.75f, 1.25f);
            source.Play();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            for (int i = 0; i < _sources.Count; i++)
            {
                AudioSource source = _sources[i];
                if (source == null) continue;
                source.Stop();
                UnityEngine.Object.Destroy(source);
            }
            _sources.Clear();

            foreach (AudioClip clip in _clips.Values)
                if (clip != null) UnityEngine.Object.Destroy(clip);
            _clips.Clear();
            _log = null;
        }

        private static AudioMixerGroup TryGetNativeEffectsMixer()
        {
            try
            {
                GlobalReferences references = InstanceBehavior<GlobalReferences>.Instance;
                return references != null ? references.foleyMixerGroup : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
