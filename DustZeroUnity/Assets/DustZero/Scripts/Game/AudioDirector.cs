// Background music: three tracks, one per screen, cross-faded on the way between them.
//
// The original drove this from Web Audio, where a buffer source takes loopStart/loopEnd
// directly and loops inside the file with no seam. AudioSource has no equivalent - its
// `loop` flag only repeats the whole clip - so the loop window is held here instead, the
// same way the original's <audio> fallback did it: play unlooped and rewind by the length
// of the window once the playhead passes its end. Reading timeSamples rather than time
// keeps that rewind sample-exact, so the seam lands on the same sample every lap.
using DustZero.Core.Meta;
using UnityEngine;

namespace DustZero.Game
{
    public enum BgmTrack { None, Lobby, Battle, Survival }

    public sealed class AudioDirector : MonoBehaviour
    {
        /// <summary>
        /// Intro-then-loop windows, in seconds, from the original's BGM_LOOP. They belong to
        /// these three files specifically - they are cut points in this music, not a house
        /// style - so a replaced track needs its own measured window, not these numbers.
        /// </summary>
        static readonly (float start, float end) LobbyLoop = (9.9f, 29.6f);
        static readonly (float start, float end) BattleLoop = (11.42f, 71.42f);
        static readonly (float start, float end) SurvivalLoop = (11.84f, 150.72f);

        /// <summary>
        /// Seconds to cross from one track to the next. The original ramped gain with
        /// setTargetAtTime(0.25-0.3), which is a time constant rather than a duration; a
        /// linear second lands close enough by ear and is far easier to reason about.
        /// </summary>
        const float FadeSeconds = 1f;

        /// <summary>
        /// The original scaled its slider by 0.8 before it reached the output. Kept so 100 on
        /// the slider is the same loudness in both builds.
        /// </summary>
        const float Headroom = 0.8f;

        [SerializeField] AudioClip lobbyClip;
        [SerializeField] AudioClip battleClip;
        [SerializeField] AudioClip survivalClip;

        sealed class Track
        {
            public AudioSource Source;
            public int LoopStartSample;
            public int LoopEndSample;
            /// <summary>Where this track was when it last faded out, so the lobby can resume.</summary>
            public int SavedSample;
            /// <summary>
            /// Set when the fade-out parked this track. It separates "we stopped this on
            /// purpose and SavedSample is good" from "this ran off the end during a stall",
            /// where SavedSample is stale and the loop point is the only sane place to rejoin.
            /// </summary>
            public bool Parked;
            public float Gain;
            public float TargetGain;
        }

        Track _lobby, _battle, _survival;
        BgmTrack _current = BgmTrack.None;
        SaveData _opts;
        bool _suspended;

        public BgmTrack Current => _current;

        void Awake()
        {
            _lobby = Build(lobbyClip, LobbyLoop, "BGM Lobby");
            _battle = Build(battleClip, BattleLoop, "BGM Battle");
            _survival = Build(survivalClip, SurvivalLoop, "BGM Survival");
        }

        Track Build(AudioClip clip, (float start, float end) loop, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.playOnAwake = false;
            // Looping is driven by the rewind in Update; the built-in flag would restart the
            // intro every lap instead of staying inside the window.
            src.loop = false;
            src.volume = 0f;
            src.spatialBlend = 0f;
            src.ignoreListenerPause = true;

            var t = new Track { Source = src };
            if (clip != null)
            {
                int total = clip.samples;
                int freq = clip.frequency;
                t.LoopStartSample = Mathf.Clamp(Mathf.RoundToInt(loop.start * freq), 0, Mathf.Max(0, total - 1));
                // A shorter file than the window implies the track was replaced without new
                // loop points; falling back to the whole clip keeps it playing rather than
                // rewinding past the end.
                int end = Mathf.RoundToInt(loop.end * freq);
                t.LoopEndSample = end > t.LoopStartSample && end <= total ? end : total;
            }
            return t;
        }

        /// <summary>Supplies the volume and mute settings; until this is called nothing plays.</summary>
        public void Bind(SaveData data)
        {
            _opts = data;
            ApplyTargets();
        }

        /// <summary>
        /// Switches tracks with a cross-fade. Battle and survival restart from the top on every
        /// run, as the original did - a run is its own performance - while the lobby picks up
        /// where it left off, so stepping in and out of a menu does not restart the theme.
        /// </summary>
        public void Play(BgmTrack track)
        {
            if (_current == track) return;
            _current = track;

            Stash(_lobby);
            Stash(_battle);
            Stash(_survival);

            var next = Of(track);
            if (next?.Source.clip != null)
            {
                bool resume = track == BgmTrack.Lobby;
                if (!next.Source.isPlaying)
                {
                    next.Source.timeSamples = resume && next.Parked ? ResumeSample(next) : 0;
                    next.Parked = false;
                    next.Source.Play();
                }
                else if (!resume)
                {
                    next.Source.timeSamples = 0;
                }
            }

            ApplyTargets();
        }

        void Stash(Track t)
        {
            if (t?.Source == null || !t.Source.isPlaying) return;
            if (Of(_current) == t) return;
            t.SavedSample = t.Source.timeSamples;
        }

        Track Of(BgmTrack t) => t switch
        {
            BgmTrack.Lobby => _lobby,
            BgmTrack.Battle => _battle,
            BgmTrack.Survival => _survival,
            _ => null,
        };

        float Volume()
        {
            if (_opts?.opt == null || _suspended) return 0f;
            return _opts.opt.mute != 0 ? 0f : Mathf.Clamp01(_opts.opt.bgm / 100f) * Headroom;
        }

        /// <summary>Re-reads the settings after the player moves the slider or hits mute.</summary>
        public void ApplyTargets()
        {
            float v = Volume();
            SetTarget(_lobby, BgmTrack.Lobby, v);
            SetTarget(_battle, BgmTrack.Battle, v);
            SetTarget(_survival, BgmTrack.Survival, v);
        }

        void SetTarget(Track t, BgmTrack which, float v)
        {
            if (t == null) return;
            t.TargetGain = _current == which ? v : 0f;
        }

        void Update()
        {
            float step = Time.unscaledDeltaTime / FadeSeconds;
            Step(_lobby, step);
            Step(_battle, step);
            Step(_survival, step);
        }

        void Step(Track t, float step)
        {
            if (t?.Source == null || t.Source.clip == null) return;

            if (!Mathf.Approximately(t.Gain, t.TargetGain))
            {
                t.Gain = Mathf.MoveTowards(t.Gain, t.TargetGain, step);
                t.Source.volume = t.Gain;
            }

            // Wrapping comes before anything that might return early. A track fading out is
            // still advancing, and these loop windows end within a tenth of a second of the
            // file end - let one fade run past the window and the clip simply ends, which
            // both drops the voice and leaves a resume position that is past the music.
            if (t.Source.isPlaying) Wrap(t);

            // Faded out and no longer wanted: remember the spot and release the voice.
            if (t.Gain <= 0.0001f && t.TargetGain <= 0f)
            {
                if (t.Source.isPlaying)
                {
                    t.SavedSample = t.Source.timeSamples;
                    t.Parked = true;
                    t.Source.Pause();
                }
                return;
            }

            // The original's tick re-issued play() on the current track whenever it found it
            // paused but audible, and the same guard earns its keep here: a clip that was not
            // ready the instant Play() ran, or a source that ran past its loop window during
            // a frame stall and ended, both leave a track wanted but silent. The windows end
            // within a tenth of a second of the file, so a long enough hitch - a scene load,
            // a collection - will outrun the wrap no matter how tight the check is; what
            // matters is that the next frame puts the music back rather than leaving silence.
            if (t.TargetGain > 0f && !t.Source.isPlaying)
            {
                t.Source.timeSamples = t.Parked ? ResumeSample(t) : t.LoopStartSample;
                t.Parked = false;
                t.Source.Play();
            }
        }

        static void Wrap(Track t)
        {
            if (t.LoopEndSample <= t.LoopStartSample) return;
            int now = t.Source.timeSamples;
            if (now < t.LoopEndSample) return;
            int span = t.LoopEndSample - t.LoopStartSample;
            t.Source.timeSamples = t.LoopStartSample + (now - t.LoopEndSample) % span;
        }

        /// <summary>
        /// Where a resumed track picks up. A stash taken mid-fade can sit past the loop
        /// window, so it is folded back inside rather than replayed as a position that no
        /// longer exists in the music.
        /// </summary>
        static int ResumeSample(Track t)
        {
            int last = Mathf.Max(0, t.Source.clip.samples - 1);
            int pos = Mathf.Clamp(t.SavedSample, 0, last);
            if (t.LoopEndSample > t.LoopStartSample && pos >= t.LoopEndSample)
            {
                int span = t.LoopEndSample - t.LoopStartSample;
                pos = t.LoopStartSample + (pos - t.LoopEndSample) % span;
            }
            return pos;
        }

        /// <summary>
        /// Silences everything while the app is in the background, as the original suspended
        /// its AudioContext on visibilitychange. The fade is skipped on the way out so the
        /// music does not keep playing for a second over another app.
        /// </summary>
        public void SetSuspended(bool suspended)
        {
            if (_suspended == suspended) return;
            _suspended = suspended;
            ApplyTargets();
            if (!suspended)
            {
                var t = Of(_current);
                if (t?.Source != null && t.Source.clip != null && !t.Source.isPlaying) t.Source.UnPause();
                return;
            }
            foreach (var t in new[] { _lobby, _battle, _survival })
            {
                if (t?.Source == null) continue;
                t.Gain = 0f;
                t.Source.volume = 0f;
                if (t.Source.isPlaying) { t.SavedSample = t.Source.timeSamples; t.Parked = true; t.Source.Pause(); }
            }
        }
    }
}
