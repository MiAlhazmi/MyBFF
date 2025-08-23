using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Game.Audio
{
    public sealed class MusicPlaylistPlayer : MonoBehaviour
    {
        // -------- Config: Playlist --------
        [Header("Playlist")]
        [SerializeField] private List<AudioClip> tracks = new List<AudioClip>();
        [SerializeField] private bool loop = true;
        [SerializeField] private bool shuffle = false;

        // -------- Config: Playback --------
        [Header("Playback")]
        [Range(0f, 1f)] [SerializeField] private float volume = 0.6f;
        [SerializeField] private AudioMixerGroup output;
        [Min(0f)] [SerializeField] private float crossfadeSeconds = 0f;
        [SerializeField] private bool persistAcrossScenes = true;

        // -------- Config: Spatial (2D/3D) --------
        public enum SpatialMode { TwoD, ThreeD }                           // [Spatial]
        [Header("Spatial (2D / 3D)")]
        [SerializeField] private SpatialMode spatialMode = SpatialMode.TwoD; // [Spatial]
        [Tooltip("When in 3D mode, this object will follow the target each frame. Optional.")] // [Spatial]
        [SerializeField] private Transform spatialFollowTarget;            // [Spatial]
        [Tooltip("3D spatial blend (1 = fully 3D).")]                      // [Spatial]
        [Range(0f, 1f)] [SerializeField] private float spatialBlend3D = 1f; // [Spatial]
        [Tooltip("Audio rolloff model in 3D.")]                             // [Spatial]
        [SerializeField] private AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic; // [Spatial]
        [Tooltip("Distance at which volume begins to fall off in 3D.")]     // [Spatial]
        [SerializeField] private float minDistance = 5f;                    // [Spatial]
        [Tooltip("Distance at which volume is near silent in 3D.")]         // [Spatial]
        [SerializeField] private float maxDistance = 50f;                   // [Spatial]
        [Tooltip("Usually 0 for music to avoid pitch shifts from relative velocity.")] // [Spatial]
        [Range(0f, 5f)] [SerializeField] private float dopplerLevel = 0f;   // [Spatial]
        [Tooltip("Stereo spread (degrees) in 3D. 0 = none.")]               // [Spatial]
        [Range(0f, 360f)] [SerializeField] private float spread = 0f;       // [Spatial]

        // -------- Internals --------
        private AudioSource _a;
        private AudioSource _b;
        private AudioSource _current;
        private AudioSource _next;

        private readonly List<int> _order = new List<int>();
        private int _orderCursor = 0;
        private Coroutine _runner;
        private bool _requestStop;

        private void Awake()
        {
            if (persistAcrossScenes)
                DontDestroyOnLoad(gameObject);

            if (tracks.Count == 0)
            {
                Debug.LogWarning($"{nameof(MusicPlaylistPlayer)} has no tracks assigned.", this);
                enabled = false;
                return;
            }

            // Child audio sources
            _a = CreateChildSource("MusicSource_A");
            _b = CreateChildSource("MusicSource_B");
            _current = _a;
            _next = _b;

            ApplySpatialTo(_a); // [Spatial]
            ApplySpatialTo(_b); // [Spatial]

            if (spatialMode == SpatialMode.ThreeD && spatialFollowTarget != null) // [Spatial]
                transform.position = spatialFollowTarget.position;                // [Spatial]

            BuildOrder();
        }

        private void OnEnable()
        {
            if (_runner == null && tracks.Count > 0)
            {
                _requestStop = false;
                _runner = StartCoroutine(RunPlaylist());
            }
        }

        private void OnDisable()
        {
            if (_runner != null)
            {
                _requestStop = true;
                StopCoroutine(_runner);
                _runner = null;
            }

            _a?.Stop();
            _b?.Stop();
        }

        private void LateUpdate() // [Spatial]
        {
            if (spatialMode == SpatialMode.ThreeD && spatialFollowTarget != null)
                transform.position = spatialFollowTarget.position;
        }

        private void OnValidate() // [Spatial] — keep sources in sync in Editor/runtime when values change
        {
            if (_a != null) ApplySpatialTo(_a);
            if (_b != null) ApplySpatialTo(_b);

            if (spatialMode == SpatialMode.ThreeD && spatialFollowTarget != null)
                transform.position = spatialFollowTarget.position;
        }

        private AudioSource CreateChildSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f; // default; ApplySpatialTo will overwrite
            src.volume = volume;
            if (output != null) src.outputAudioMixerGroup = output;
            return src;
        }

        // ---------- Spatial helpers ----------
        private void ApplySpatialTo(AudioSource src) // [Spatial]
        {
            if (src == null) return;

            if (spatialMode == SpatialMode.TwoD)
            {
                src.spatialBlend = 0f;
                src.rolloffMode = AudioRolloffMode.Logarithmic; // irrelevant in 2D
                src.minDistance = 1f;
                src.maxDistance = 500f;
                src.dopplerLevel = 0f;
                src.spread = 0f;
            }
            else
            {
                src.spatialBlend = spatialBlend3D;
                src.rolloffMode = rolloff;
                src.minDistance = Mathf.Max(0.01f, minDistance);
                src.maxDistance = Mathf.Max(src.minDistance + 0.01f, maxDistance);
                src.dopplerLevel = dopplerLevel;
                src.spread = spread;
            }
        }

        /// <summary>Set runtime spatial mode (2D/3D) and update both sources.</summary>
        public void SetSpatialMode(SpatialMode mode) // [Spatial]
        {
            if (spatialMode == mode) return;
            spatialMode = mode;

            ApplySpatialTo(_a);
            ApplySpatialTo(_b);

            // Ensure position is sensible when switching to 3D
            if (spatialMode == SpatialMode.ThreeD && spatialFollowTarget != null)
                transform.position = spatialFollowTarget.position;
        }

        /// <summary>Convenience wrapper: true => 3D, false => 2D.</summary>
        public void Set3DEnabled(bool enabled) => SetSpatialMode(enabled ? SpatialMode.ThreeD : SpatialMode.TwoD); // [Spatial]

        /// <summary>Assign/replace the target this audio object follows in 3D.</summary>
        public void SetSpatialFollowTarget(Transform target) // [Spatial]
        {
            spatialFollowTarget = target;
            if (spatialMode == SpatialMode.ThreeD && spatialFollowTarget != null)
                transform.position = spatialFollowTarget.position;
        }

        // ---------- Order/shuffle ----------
        private void BuildOrder()
        {
            _order.Clear();
            for (int i = 0; i < tracks.Count; i++) _order.Add(i);
            if (shuffle) Shuffle(_order);
            _orderCursor = 0;
        }

        private static void Shuffle(List<int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private int NextIndex()
        {
            if (_order.Count == 0) return -1;

            int idx = _order[_orderCursor];
            _orderCursor++;

            if (_orderCursor >= _order.Count)
            {
                if (loop) BuildOrder();
                else _orderCursor = _order.Count;
            }

            return idx;
        }

        // ---------- Core runner ----------
        private IEnumerator RunPlaylist()
        {
            int idx = NextIndex();
            if (idx < 0) yield break;

            var first = tracks[idx];
            if (first != null)
            {
                PrepareSource(_current, first, volume);
                _current.Play();
            }

            while (!_requestStop)
            {
                if (crossfadeSeconds <= 0.0001f)
                {
                    float len = _current.clip != null ? Mathf.Max(0.01f, _current.clip.length) : 0.01f;
                    yield return new WaitForSecondsRealtime(len);

                    idx = NextIndex();
                    if (idx < 0) yield break;

                    var clip = tracks[idx];
                    if (clip == null) continue;

                    PrepareSource(_current, clip, volume);
                    _current.Play();
                }
                else
                {
                    float currentLen = _current.clip != null ? _current.clip.length : 0.01f;
                    float leadIn = Mathf.Clamp(crossfadeSeconds, 0f, Mathf.Max(0f, currentLen - 0.01f));
                    float wait = Mathf.Max(0.01f, currentLen - leadIn);
                    yield return new WaitForSecondsRealtime(wait);

                    idx = NextIndex();
                    if (idx < 0) yield break;

                    var nextClip = tracks[idx];
                    if (nextClip == null) continue;

                    PrepareSource(_next, nextClip, 0f);
                    _next.Play();

                    float t = 0f;
                    float dur = Mathf.Max(0.01f, crossfadeSeconds);
                    float startVolCurrent = _current.volume;

                    while (t < dur)
                    {
                        t += Time.unscaledDeltaTime;
                        float k = Mathf.Clamp01(t / dur);
                        _next.volume = Mathf.Lerp(0f, volume, k);
                        _current.volume = Mathf.Lerp(startVolCurrent, 0f, k);
                        yield return null;
                    }

                    _current.Stop();
                    _current.volume = volume;
                    (_current, _next) = (_next, _current);
                }
            }
        }

        private static void PrepareSource(AudioSource src, AudioClip clip, float vol)
        {
            src.clip = clip;
            src.volume = vol;
        }

        // ---------- Public controls ----------
        public void Skip()
        {
            if (_runner == null) return;
            StopCoroutine(_runner);
            _runner = StartCoroutine(SkipAndRun());
        }

        private IEnumerator SkipAndRun()
        {
            if (crossfadeSeconds > 0f)
            {
                int idx = NextIndex();
                if (idx >= 0 && tracks[idx] != null)
                {
                    PrepareSource(_next, tracks[idx], 0f);
                    _next.Play();

                    float t = 0f;
                    float dur = Mathf.Max(0.05f, crossfadeSeconds * 0.5f);
                    float startVolCurrent = _current.volume;

                    while (t < dur)
                    {
                        t += Time.unscaledDeltaTime;
                        float k = Mathf.Clamp01(t / dur);
                        _next.volume = Mathf.Lerp(0f, volume, k);
                        _current.volume = Mathf.Lerp(startVolCurrent, 0f, k);
                        yield return null;
                    }

                    _current.Stop();
                    _current.volume = volume;
                    (_current, _next) = (_next, _current);
                }
            }
            else
            {
                int idx = NextIndex();
                if (idx >= 0 && tracks[idx] != null)
                {
                    PrepareSource(_current, tracks[idx], volume);
                    _current.Play();
                }
            }

            _runner = StartCoroutine(RunPlaylist());
        }

        public void SetPaused(bool paused)
        {
            if (paused) { _a.Pause(); _b.Pause(); }
            else { _a.UnPause(); _b.UnPause(); }
        }
    }
}
