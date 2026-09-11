using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Chart authoring tool. Set BPM and beat count in the Inspector, press Play,
/// then follow the on-screen prompts to tap or hold the left hand, then the right hand.
///
/// - Holding a key for more than N beats produces a length-(N+1) hold tile.
/// - If you press multiple distinct keys within a single recording beat, the recorder
///   detects the finest sub-beat division you used (2×, 4×, or 8×) and writes the chart
///   at an elevated BPM so those sub-beats are separate lines. The chart's BPM is logged
///   to the console and written as a "// BPM: N" header at the top of the saved file —
///   set the Piano's BPM to that value.
/// </summary>
public class ChartRecorder : MonoBehaviour
{
    [Header("Recording Settings")]
    public float bpm = 120f;
    [Tooltip("Total beats to record per hand (at the recording BPM, before any sub-beat scaling).")]
    public int totalBeats = 32;
    [Tooltip("Count-in beats before recording begins (audible clicks, no key capture).")]
    public int countInBeats = 4;

    [Header("Output")]
    [Tooltip("Folder, project-relative. Created if it doesn't exist.")]
    public string outputDirectory = "Assets/Songs/Song Charts";
    public string outputFileName = "Recorded_Chart.txt";

    [Header("Metronome")]
    [Tooltip("Optional. If left empty, a procedural click sound is generated.")]
    public AudioClip metronomeClick;
    [Range(0f, 1f)] public float clickVolume = 0.7f;

    enum Phase { Idle, CountInLeft, RecordingLeft, ReadyRight, CountInRight, RecordingRight, Done }
    Phase phase = Phase.Idle;

    struct ChartEvent
    {
        public char letter;
        public float pressTime;   // seconds since this phase's recording start
        public float releaseTime; // seconds since this phase's recording start
    }

    readonly List<ChartEvent> leftEvents = new List<ChartEvent>();
    readonly List<ChartEvent> rightEvents = new List<ChartEvent>();
    readonly Dictionary<KeyCode, float> activePresses = new Dictionary<KeyCode, float>(); // KeyCode -> Time.time when pressed

    float phaseStartTime;
    int lastClickedBeat;
    string lastSavedPath = "";
    int lastSubdivision = 1;
    float lastChartBpm = 120f;

    AudioSource audioSource;
    AudioClip proceduralRegular;

    static readonly Dictionary<KeyCode, char> leftMap = new Dictionary<KeyCode, char>
    {
        { KeyCode.A, 'a' }, { KeyCode.S, 's' }, { KeyCode.D, 'd' },
        { KeyCode.F, 'f' }, { KeyCode.G, 'g' }, { KeyCode.Space, 'g' }
    };
    static readonly Dictionary<KeyCode, char> rightMap = new Dictionary<KeyCode, char>
    {
        { KeyCode.H, 'h' }, { KeyCode.J, 'j' }, { KeyCode.K, 'k' },
        { KeyCode.L, 'l' }, { KeyCode.Semicolon, ';' }, { KeyCode.Space, 'h' }
    };

    void Awake()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        proceduralRegular = MakeClick(900f, 0.05f);
    }

    void Update()
    {
        switch (phase)
        {
            case Phase.Idle:
            case Phase.Done:
                if (Input.GetKeyDown(KeyCode.R)) BeginCountIn(true);
                break;
            case Phase.ReadyRight:
                if (Input.GetKeyDown(KeyCode.R)) BeginCountIn(false);
                break;
            case Phase.CountInLeft:
            case Phase.CountInRight:
                Tick();
                if (Elapsed() >= countInBeats * SecondsPerBeat()) StartCapture();
                break;
            case Phase.RecordingLeft:
                Tick();
                Capture(leftMap, leftEvents);
                // ENTER ends the left-hand recording early. FinishCapture()
                // flushes anything still held, then transitions to ReadyRight
                // so the user can press R to start the right hand — exactly
                // the "nothing else for this hand" semantic.
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                    FinishCapture();
                else if (Elapsed() >= totalBeats * SecondsPerBeat()) FinishCapture();
                break;
            case Phase.RecordingRight:
                Tick();
                Capture(rightMap, rightEvents);
                // ENTER ends the right-hand recording early. FinishCapture()
                // transitions to Done and writes the chart with whatever was
                // captured up to that point (remaining beats become rests).
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                    FinishCapture();
                else if (Elapsed() >= totalBeats * SecondsPerBeat()) FinishCapture();
                break;
        }
    }

    void BeginCountIn(bool isLeft)
    {
        if (isLeft) leftEvents.Clear();
        else rightEvents.Clear();
        EnterPhase(isLeft ? Phase.CountInLeft : Phase.CountInRight);
    }

    void StartCapture()
    {
        EnterPhase(phase == Phase.CountInLeft ? Phase.RecordingLeft : Phase.RecordingRight);
    }

    void FinishCapture()
    {
        // Any keys still held at the end of recording: treat as released now.
        FlushActivePresses();

        if (phase == Phase.RecordingLeft)
        {
            phase = Phase.ReadyRight;
        }
        else
        {
            phase = Phase.Done;
            CombineAndSave();
        }
    }

    void EnterPhase(Phase p)
    {
        phase = p;
        phaseStartTime = Time.time;
        lastClickedBeat = -1;
        activePresses.Clear();
    }

    float SecondsPerBeat() => 60f / Mathf.Max(1f, bpm);
    float Elapsed() => Time.time - phaseStartTime;

    void Tick()
    {
        int beat = Mathf.FloorToInt(Elapsed() / SecondsPerBeat());
        if (beat > lastClickedBeat)
        {
            lastClickedBeat = beat;
            AudioClip click = metronomeClick != null ? metronomeClick : proceduralRegular;
            audioSource.PlayOneShot(click, clickVolume);
        }
    }

    void Capture(Dictionary<KeyCode, char> map, List<ChartEvent> events)
    {
        foreach (var kvp in map)
        {
            KeyCode k = kvp.Key;

            if (Input.GetKeyDown(k) && !activePresses.ContainsKey(k))
                activePresses[k] = Time.time;

            if (Input.GetKeyUp(k) && activePresses.TryGetValue(k, out float pressTime))
            {
                activePresses.Remove(k);
                events.Add(new ChartEvent
                {
                    letter = kvp.Value,
                    pressTime = pressTime - phaseStartTime,
                    releaseTime = Time.time - phaseStartTime,
                });
            }
        }
    }

    void FlushActivePresses()
    {
        if (activePresses.Count == 0) return;
        var map = phase == Phase.RecordingLeft ? leftMap : rightMap;
        var events = phase == Phase.RecordingLeft ? leftEvents : rightEvents;
        float now = Time.time;
        foreach (var kvp in activePresses)
        {
            if (!map.TryGetValue(kvp.Key, out char letter)) continue;
            events.Add(new ChartEvent
            {
                letter = letter,
                pressTime = kvp.Value - phaseStartTime,
                releaseTime = now - phaseStartTime,
            });
        }
        activePresses.Clear();
    }

    void CombineAndSave()
    {
        // 1. Detect subdivision from the smallest non-simultaneous gap between presses.
        int subdivision = DetectSubdivision();
        float chartBpm = bpm * subdivision;
        float chartSpb = SecondsPerBeat() / subdivision;
        int totalChartBeats = totalBeats * subdivision;
        lastSubdivision = subdivision;
        lastChartBpm = chartBpm;

        // 2. Build per-chart-beat token lists. Combine + sort all events, then walk
        // through clustering close-in-time presses to the same chart beat so chords
        // stay together even at subdivision 2.
        var lines = new List<string>[totalChartBeats];
        for (int i = 0; i < totalChartBeats; i++) lines[i] = new List<string>();

        var allEvents = new List<ChartEvent>(leftEvents.Count + rightEvents.Count);
        allEvents.AddRange(leftEvents);
        allEvents.AddRange(rightEvents);
        allEvents.Sort((a, b) => a.pressTime.CompareTo(b.pressTime));

        float chordThresholdSec = SecondsPerBeat() * ChordThresholdBeats;
        float clusterStart = float.NegativeInfinity;
        int clusterBeat = -1;

        foreach (var e in allEvents)
        {
            if (clusterBeat < 0 || e.pressTime - clusterStart >= chordThresholdSec)
            {
                clusterStart = e.pressTime;
                clusterBeat = Mathf.Clamp(Mathf.RoundToInt(e.pressTime / chartSpb), 0, totalChartBeats - 1);
            }
            float heldBeats = (e.releaseTime - e.pressTime) / chartSpb;
            int duration = Mathf.Max(1, Mathf.CeilToInt(heldBeats));
            string token = duration == 1 ? e.letter.ToString() : e.letter.ToString() + duration;
            if (!lines[clusterBeat].Contains(token))
                lines[clusterBeat].Add(token);
        }

        // 3. Write the file with a BPM header comment.
        var sb = new StringBuilder();
        sb.AppendLine($"// BPM: {chartBpm:0.##}  (recording BPM {bpm:0.##} × {subdivision} subdivision)");
        sb.AppendLine($"// Total beats: {totalChartBeats}");
        for (int i = 0; i < totalChartBeats; i++)
            sb.AppendLine(lines[i].Count == 0 ? "-" : string.Join(",", lines[i]));

        try
        {
            if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);
            string fullPath = Path.Combine(outputDirectory, outputFileName);
            File.WriteAllText(fullPath, sb.ToString());
            lastSavedPath = fullPath;
            Debug.Log($"[ChartRecorder] Saved chart to {fullPath}  •  Set Piano BPM to {chartBpm:0.##}  (subdivision {subdivision}×)");
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ChartRecorder] Failed to save: {e.Message}");
        }
    }

    // Any two presses within this many recording-beats of each other are treated as
    // a chord (same chart beat) rather than as two sub-beat events. This sets the
    // effective ceiling: anything finer than this would imply subdivision > 2.
    const float ChordThresholdBeats = 0.35f;

    int DetectSubdivision()
    {
        // Collect press times across both hands.
        var presses = new List<float>(leftEvents.Count + rightEvents.Count);
        foreach (var e in leftEvents) presses.Add(e.pressTime);
        foreach (var e in rightEvents) presses.Add(e.pressTime);
        if (presses.Count < 2) return 1;

        presses.Sort();
        float spb = SecondsPerBeat();
        float chordThresholdSec = spb * ChordThresholdBeats;

        float minGapBeats = float.PositiveInfinity;
        for (int i = 1; i < presses.Count; i++)
        {
            float gapSec = presses[i] - presses[i - 1];
            if (gapSec < chordThresholdSec) continue; // chord — not a sub-beat
            float gapBeats = gapSec / spb;
            if (gapBeats < minGapBeats) minGapBeats = gapBeats;
        }

        if (float.IsPositiveInfinity(minGapBeats)) return 1;
        // Max subdivision is 2: anything that would've been finer is treated as a chord above.
        return minGapBeats >= 0.7f ? 1 : 2;
    }

    AudioClip MakeClick(float frequency, float duration)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.FloorToInt(sampleRate * duration);
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = Mathf.Exp(-t * 30f);
            samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope;
        }
        var clip = AudioClip.Create($"Click_{frequency}", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
        };
        style.normal.textColor = Color.white;

        var rect = new Rect(20, 20, Screen.width - 40, Screen.height - 40);
        int currentBeat = Mathf.FloorToInt(Elapsed() / SecondsPerBeat()) + 1;
        int beatClamped = Mathf.Clamp(currentBeat, 1, totalBeats);

        string text;
        switch (phase)
        {
            case Phase.Idle:
                text = $"CHART RECORDER\n\nBPM: {bpm}   Beats: {totalBeats}   Count-in: {countInBeats}\n\n" +
                       $"Tap or HOLD keys to the metronome. Sub-beat presses auto-bump the chart BPM.\n\n" +
                       $"Press R to record the LEFT hand (a s d f g, space = g)";
                break;
            case Phase.CountInLeft:
                text = $"Count-in LEFT  •  {currentBeat}/{countInBeats}";
                break;
            case Phase.RecordingLeft:
                text = $"RECORDING LEFT  •  beat {beatClamped}/{totalBeats}\n" +
                       $"Keys: a s d f g (space = g)  •  HOLD for multi-beat tiles\n" +
                       $"ENTER — skip ahead (record nothing else for left; move on to right hand)";
                break;
            case Phase.ReadyRight:
                text = "Left hand done.\n\nPress R to record the RIGHT hand (h j k l ;, space = h)";
                break;
            case Phase.CountInRight:
                text = $"Count-in RIGHT  •  {currentBeat}/{countInBeats}";
                break;
            case Phase.RecordingRight:
                text = $"RECORDING RIGHT  •  beat {beatClamped}/{totalBeats}\n" +
                       $"Keys: h j k l ; (space = h)  •  HOLD for multi-beat tiles\n" +
                       $"ENTER — end recording now and save the chart";
                break;
            case Phase.Done:
                text = $"DONE!\n\nSaved to:\n{lastSavedPath}\n\nDetected subdivision: {lastSubdivision}×\nSet the Piano's BPM to: {lastChartBpm:0.##}\n\nPress R to re-record both hands.";
                break;
            default:
                text = "";
                break;
        }
        GUI.Label(rect, text, style);
    }
}
