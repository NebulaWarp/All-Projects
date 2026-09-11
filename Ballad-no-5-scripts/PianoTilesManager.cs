using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class PianoTilesManager : MonoBehaviour
{
    [Header("Session")]
    [Tooltip("If true, the session starts automatically in Start(). Leave false when a GameDirector controls when to begin/end the minigame.")]
    public bool autoStart = false;
    [Tooltip("Multiplier applied to the song's BPM and audio playback. 1 = original speed; 0.5 = half speed; 0.1 = a tenth. Set externally by GameDirector before BeginSession.")]
    [Range(0.05f, 2f)] public float speedMultiplier = 1f;
    [Tooltip("First beat (0-based) to play in chart mode. Set externally by GameDirector before BeginSession to start from a section instead of the beginning.")]
    [HideInInspector] public int startBeatIndex = 0;
    [Tooltip("Multiplier applied to the song's tile height (and therefore fall speed — " +
             "fall speed is derived from tile height × bpm / 60). 1 = default; 0.5 = half-tall, " +
             "half-fast; 3 = three times as tall and fast. Set externally by GameDirector " +
             "before BeginSession. Only takes effect when a SongConfig is supplied.")]
    [HideInInspector] public float tileHeightScale = 1f;
    [Tooltip("Global audio-output latency compensation, in SECONDS. Added to musicLeadOffset " +
             "when computing the music's scheduled play time. Negative values play the music " +
             "earlier (to compensate for an audio output that adds its own delay — e.g. " +
             "Bluetooth headphones, which typically add 100-300 ms). Set externally by " +
             "GameDirector from a PlayerPrefs-saved value adjustable in the speed prompt.")]
    [HideInInspector] public float globalAudioOffset = 0f;

    [Tooltip("Optional. 10-entry array of player-rebound column keys. When non-null with " +
             "exactly 10 entries, BeginSession uses these instead of the hard-coded defaults " +
             "(A S D F Space Space J K L ;). Labels shown under each column are derived from " +
             "these KeyCodes. Set by GameDirector from a PlayerPrefs-saved value adjustable " +
             "in the ESC settings menu.")]
    [HideInInspector] public KeyCode[] customColumnKeys;

    [Header("Layout")]
    [Range(0.5f, 1f)] public float playFieldWidthRatio = 0.92f; // game spans most of screen
    [Range(0.05f, 0.5f)] public float hitLineFromBottomRatio = 0.2f; // 3/4 down from top = 1/4 from bottom
    public int columnCount = 10;
    [Tooltip("Default tile height. Overridden per-piano via SongConfig.tileHeight. " +
             "Fall speed is derived as tileHeight * bpm / 60 so consecutive beats touch top-to-bottom.")]
    public float tileHeight = 2.2f;
    [Range(0.3f, 0.95f)] public float tileWidthRatio = 0.65f; // narrower than column

    [Header("Gameplay")]
    public float minSpawnInterval = 0.6f;
    public float maxSpawnInterval = 1.5f;
    [Tooltip("Seconds before a tile reaches the hit line that a key press will " +
             "still register as a hit instead of a miss. Defaults to 0.05 s " +
             "(≈ 3 frames at 60 fps), so an input that lands a couple of frames " +
             "ahead of the visual contact still grabs the tile.")]
    [Range(0f, 0.25f)] public float earlyHitLeadTime = 0.05f;
    [Tooltip("Seconds AFTER a tap has passed the hit line that a key press still " +
             "catches it — so a slightly-late tap still counts and glows instead of " +
             "missing (and letting the next tile in the column get grabbed by mistake). " +
             "Hold tiles use their on-screen overlap instead of this window.")]
    [Range(0f, 0.3f)] public float lateHitTrailTime = 0.12f;
    [Tooltip("Tiles whose visualHeight (world units) exceeds this value are treated " +
             "as 'hold' tiles regardless of how many beats they cover — the player " +
             "must hold the key while the tile passes under the hit line, the tile " +
             "only fades after the key is released, and the hold sparks/clip animation " +
             "play the whole time. Multi-beat charts produce hold tiles naturally; this " +
             "threshold catches very-tall single-beat tiles (high tileHeightScale).")]
    public float holdTileMinHeight = 3f;

    [Header("Chord Spawning")]
    [Range(0f, 1f)] public float multiSpawnChance = 0.3f;
    [Range(2, 6)] public int maxChordSize = 2;

    [Header("Audio")]
    [Tooltip("Optional. Plays in chart mode, automatically delayed so the song's downbeat " +
             "lines up with the first tile reaching the hit line.")]
    public AudioClip song;
    [Range(0f, 1f)] public float musicVolume = 1f;
    [Tooltip("Seconds added to the auto-computed music delay. " +
             "The music naturally starts when the first tile's bottom edge reaches the hit line; " +
             "positive values delay it further, negative values move it earlier.")]
    public float musicLeadOffset = 0f;

    [Header("Chart")]
    [Tooltip("Beats per minute. Only used when Chart has content.")]
    public float bpm = 120f;
    [Tooltip("Optional. Drag a .txt asset here for long charts. If set, overrides the typed Chart below.")]
    public TextAsset chartFile;
    [Tooltip("One line = one beat. Tokens per line are comma-separated; each token is a " +
             "column letter (a s d f g h j k l ;), optionally with a digit suffix for a " +
             "multi-beat hold (e.g. 'g2'). g and h designate the two SPACE columns; " +
             "in-game you still press SPACE for both. '-' is an explicit rest beat. " +
             "Lines that are blank or contain only '|' are skipped (use for bar dividers). " +
             "Anything after '//' on a line is a comment. " +
             "Ignored when Chart File is set. " +
             "If both Chart and Chart File are empty, the random spawner runs instead.\n" +
             "Example:\n  a\n  s\n  d,k2   // chord, hold K for 2 beats\n  -\n  |\n  f")]
    [TextArea(10, 30)] public string chart;
    public bool loopChart = true;

    [Header("Colors")]
    public Color tileColor = Color.black;
    [Tooltip("Body color for tiles in alternating-color groups (chart lines wrapped " +
             "in '( ... )'). Default is a dark gray — still clearly distinct from the " +
             "main black tile color, but dark enough that both groups read as 'black " +
             "tiles' against the playfield. The outline stays white (tileOutlineColor) " +
             "for both groups.")]
    public Color alternateTileColor = new Color(0.22f, 0.22f, 0.22f);
    public Color columnLineColor = new Color(1f, 1f, 1f, 0.55f);
    public Color hitLineColor = Color.white;
    public Color hitZoneColor = new Color(1f, 1f, 1f, 0.08f);
    public Color hitGlowColor = new Color(1f, 0.92f, 0.2f, 0.5f);
    public Color labelColor = Color.white;

    [Header("Fonts")]
    [Tooltip("Optional. Font for the in-game playfield text — the column key labels " +
             "and the miss counter. This is a TextMeshPro font asset (TMP_FontAsset), " +
             "not a regular Font. Leave empty to use TextMeshPro's default font.")]
    public TMP_FontAsset playfieldFont;

    [Header("Hit Glow")]
    [Tooltip("Seconds before a tile reaches the hit line that its column starts glowing.")]
    [Range(0f, 1f)] public float glowLeadTime = 0.25f;

    [Header("Tile Outline")]
    [Tooltip("Thickness of the white outline drawn around every tile, in world units. " +
             "Two stacked tiles' outlines touch, naturally marking the boundary between them.")]
    public float tileOutlineThickness = 0.06f;
    [Tooltip("Color of the tile outline. Slight transparency gives a soft 'glow' against the dark playfield.")]
    public Color tileOutlineColor = new Color(1f, 1f, 1f, 0.85f);
    [Tooltip("The tile outline is never drawn thinner than this many on-screen pixels, even at " +
             "low resolutions (e.g. a small WebGL canvas) where a fixed world-space thickness " +
             "would shrink to sub-pixel — which is what makes the border shimmer and the band " +
             "between two stacked tiles vanish. tileOutlineThickness is used when it's wider.")]
    public float minOutlinePixels = 2.5f;

    [Header("Background")]
    [Tooltip("Default background image (overridden per-piano via SongConfig.background). " +
             "Stretched to cover the camera view behind the playfield.")]
    public Sprite background;
    [Tooltip("Alpha of the background. Lower keeps tiles legible.")]
    [Range(0f, 1f)] public float backgroundOpacity = 0.7f;
    [Tooltip("Brightness multiplier on the background's RGB (1 = full color, 0 = black). Lower to dim.")]
    [Range(0f, 1f)] public float backgroundBrightness = 0.45f;

    [Header("Catch Flash")]
    [Tooltip("Color the tile flashes to the moment it's successfully caught.")]
    public Color catchFlashColor = new Color(1f, 0.95f, 0.4f, 1f);
    [Tooltip("Seconds for the catch flash to fade from catchFlashColor back to tileColor.")]
    [Range(0.05f, 1f)] public float catchFlashDuration = 0.3f;
    [Tooltip("Color a tile briefly flashes when it's MISSED (passes the hit line with no " +
             "chance left to hit it) before it fades out falling off-screen. Kept muted " +
             "so it's a quiet 'gone' cue, not a distraction.")]
    public Color missDissolveColor = new Color(0.8f, 0.2f, 0.2f, 1f);
    [Tooltip("Color the hit-zone box (the one showing the key's letter) flashes when you " +
             "press that key at the wrong time — basically the catch glow, but red. A " +
             "slightly brighter red than the missed-tile color reads as 'wrong'. The alpha " +
             "is the peak glow strength; it fades back to the box's normal look.")]
    public Color wrongPressFlashColor = new Color(1f, 0.3f, 0.3f, 0.5f);
    [Tooltip("Seconds for the wrong-press hit-zone flash to fade back to normal.")]
    [Range(0.05f, 1f)] public float wrongPressFlashDuration = 0.25f;

    [Header("Press Feedback")]
    [Tooltip("How much a correctly-hit tile shrinks in WIDTH only — its height/length " +
             "is never touched — to feel 'pressed'. 0.08 = 8% narrower. Taps snap to " +
             "this; multi-beat holds ease into it over the first half of the note and " +
             "freeze there (or the moment the key is released). 0 = no press effect.")]
    [Range(0f, 0.3f)] public float pressShrink = 0.08f;
    [Tooltip("Seconds a one-beat (tap) tile takes to reach the pressed width. Lower = snappier.")]
    [Range(0.02f, 0.4f)] public float pressTapDuration = 0.08f;

    [Header("Hold Effect")]
    [Tooltip("Color of the pulsing core + sparks shown at the hit line while a multi-beat tile is being held.")]
    public Color holdEffectColor = new Color(1f, 0.95f, 0.4f, 1f);
    [Tooltip("Floor that the tile's body+outline alpha drifts down to while the " +
             "player is holding the key. 1.0 = no fade during hold; 0.0 = fades to " +
             "fully transparent by the natural end of the hold. Default 0.3 makes a " +
             "held tile slowly translucent without ever fully disappearing.")]
    [Range(0f, 1f)] public float holdFadeMinAlpha = 0.3f;
    [Tooltip("Seconds between spark emissions per held column. Lower = denser stream.")]
    [Range(0.01f, 0.2f)] public float holdSparkInterval = 0.035f;
    [Tooltip("How fast each spark flies outward, in world units / sec.")]
    [Range(0.5f, 8f)] public float holdSparkSpeed = 3f;
    [Tooltip("Average lifetime of each spark in seconds (small random jitter is added).")]
    [Range(0.1f, 1.5f)] public float holdSparkLifetime = 0.45f;
    [Tooltip("Tiles this many beats long (or longer) get a MORE DRAMATIC effect while " +
             "held: the tile body glows ORANGE, and thin particle streams shoot out of " +
             "the tile's LEFT and RIGHT sides at the hit line, growing denser/faster the " +
             "longer the note is held.")]
    public int longHoldBeatThreshold = 8;
    [Tooltip("Orange the long-hold tile body glows toward while held. Brightens from a " +
             "dimmer to a fuller orange as the hold intensifies.")]
    public Color longHoldGlowColor = new Color(1f, 0.5f, 0.05f, 1f);
    [Tooltip("Seconds of holding for the long-hold side-particle effect (and orange " +
             "glow) to ramp from its mildest to its most intense.")]
    [Range(0.3f, 6f)] public float longHoldRampSeconds = 2.5f;

    // --- runtime ---
    float fallSpeed;
    KeyCode[] columnKeys;
    string[] columnLabels;
    float playFieldLeft, playFieldRight, playFieldWidth, columnWidth;
    float hitLineY, spawnY, screenTopY, screenBottomY;
    float effectiveOutline; // tileOutlineThickness, bumped up so it's never sub-pixel on screen
    List<List<Tile>> tilesPerColumn;
    float nextSpawnTime;
    float screenRightX;
    int missCount;
    int leftHandMissCount;   // columns 0..4 (a s d f g — left space column)
    int rightHandMissCount;  // columns 5..9 (h j k l ; — right space column)
    int hitCount;            // tiles successfully secured this session (independent of misses)
    int totalTileCount;      // total tiles spawned this session (= the song's tile count once it completes)
    TextMeshPro missCounterText;

    bool useChart;
    bool chartActive;
    float secondsPerBeat;
    int currentBeatIndex;
    float nextBeatTime;
    BeatEntry[] parsedBeats;
    TempoChange[] parsedTempos;   // mid-song tempo markers (sorted by beatIndex)
    int nextTempoIdx;             // index of the next un-applied tempo marker
    float initialChartBpm;        // bpm in effect at session start (used on loop reset)
    AudioSource audioSource;
    // A speed-adjusted copy of `song` built at session start when speedMultiplier != 1.
    // WebGL ignores AudioSource.pitch, so the speed is baked into the PCM instead.
    // Owned by this manager (created via AudioClip.Create) and destroyed when the
    // session ends or a new one starts; never points at the shared `song` asset.
    AudioClip generatedClip;
    SpriteRenderer[] hitZoneRenderers;
    float[] wrongPressFlashTime; // per column: Time.time of the last wrong-time key press (drives the hit-zone flash)

    bool sessionActive;
    float inputSuppressedUntil;

    // Hold effect: one pulsing "core" sprite per column, plus a pool of flying sparks
    // shared across all columns. Together they make the hit line crackle while a
    // multi-beat tile is being held.
    SpriteRenderer[] holdBurstRenderers;
    float[] holdNextSparkTime;        // per-column next-spawn timestamp so spawning is column-local
    float[] holdNextStreamTime;       // per-column timer for the dense long-hold vertical streams
    List<HoldSpark> holdSparks;

    class HoldSpark
    {
        public GameObject go;
        public SpriteRenderer sr;
        public Vector2 velocity;
        public float spawnTime;
        public float lifetime;
        public Color color; // base color (yellow for radial sparks, orange for long-hold streams)
    }

    public int MissCount => missCount;
    public int LeftHandMissCount => leftHandMissCount;
    public int RightHandMissCount => rightHandMissCount;
    public int HitCount => hitCount;             // tiles successfully hit
    public int TotalTileCount => totalTileCount; // total tiles in the (played) song
    public bool SessionActive => sessionActive;

    /// <summary>
    /// True once a (non-looping) chart has finished spawning AND every tile has
    /// fallen off-screen or faded out — i.e. "the tiles are out". Always false for
    /// looping charts and the random spawner, which have no natural end.
    /// </summary>
    public bool Completed => sessionActive && useChart && !chartActive && NoTilesRemain();

    bool NoTilesRemain()
    {
        if (tilesPerColumn == null) return true;
        for (int col = 0; col < tilesPerColumn.Count; col++)
            if (tilesPerColumn[col] != null && tilesPerColumn[col].Count > 0) return false;
        return true;
    }

    static Sprite _squareSprite;
    static Sprite SquareSprite
    {
        get
        {
            if (_squareSprite != null) return _squareSprite;
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            _squareSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return _squareSprite;
        }
    }

    void Start()
    {
        if (autoStart) BeginSession();
    }

    /// <summary>Begin a session using a per-interactable song/chart config (overrides the Inspector defaults).</summary>
    public void BeginSession(SongConfig config)
    {
        if (config != null)
        {
            song = config.song;
            bpm = config.bpm;
            // Scale the per-song tile height by tileHeightScale. Because fallSpeed
            // is derived as tileHeight × bpm / 60 below, bigger tiles also fall faster
            // (one beat still equals one tile-height of travel) — which is the
            // "tile fall speed" knob exposed by the speed prompt.
            tileHeight = config.tileHeight * Mathf.Max(0.05f, tileHeightScale);
            chartFile = config.chartFile;
            chart = config.chart;
            loopChart = config.loopChart;
            musicLeadOffset = config.musicLeadOffset;
            background = config.background;
            backgroundOpacity = config.backgroundOpacity;
            backgroundBrightness = config.backgroundBrightness;
        }
        BeginSession();
    }

    public void BeginSession()
    {
        if (sessionActive) return;
        sessionActive = true;

        // Capture the audio DSP clock NOW, before any of the heavy session setup —
        // the sprite creation below and, above all, the full-song resample inside
        // StartMusic, which can burn 100s of ms on a real track. Time.time is frozen
        // at this frame's start for the whole method, so the tile timeline is anchored
        // to that instant; the music must be anchored to the matching DSP instant.
        // Reading AudioSettings.dspTime later (after the resample) would place the
        // music that much LATER than the tiles — which is exactly the "tiles reach the
        // hit line before the note" desync seen at non-1x speeds (1x skips the resample
        // entirely, so it stays in sync). StartMusic schedules against this value.
        double sessionStartDsp = AudioSettings.dspTime;

        // Briefly ignore key-down events so the SPACE press that opened the
        // minigame doesn't get consumed as a column tap on the entry frame.
        inputSuppressedUntil = Time.time + 0.15f;

        if (customColumnKeys != null && customColumnKeys.Length == columnCount)
        {
            columnKeys = (KeyCode[])customColumnKeys.Clone();
        }
        else
        {
            columnKeys = new[]
            {
                KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F,
                KeyCode.Space, KeyCode.Space,
                KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.Semicolon
            };
        }
        // Labels derive from the actual KeyCode of each column. When columns 4
        // AND 5 are both Space, blank out their labels — the DrawSpaceBarSymbol
        // method draws the ⎵ glyph spanning both. Otherwise, show the bound
        // key's label for each one individually.
        columnLabels = new string[columnCount];
        for (int i = 0; i < columnCount; i++)
            columnLabels[i] = KeyCodeToColumnLabel(columnKeys[i]);
        if (columnKeys[4] == KeyCode.Space && columnKeys[5] == KeyCode.Space)
        {
            columnLabels[4] = "";
            columnLabels[5] = "";
        }

        CalculateDimensions();
        DrawBackground();     // background sits behind everything else
        DrawPlayfieldShade(); // subtle dark panel between background and gameplay
        DrawHitZones();       // draw the soft zones first so lines render on top
        DrawColumnLines();
        DrawHitLine();
        DrawLabels();
        DrawSpaceBarSymbol();
        DrawMissCounter();
        DrawHoldBursts();

        tilesPerColumn = new List<List<Tile>>();
        for (int i = 0; i < columnCount; i++) tilesPerColumn.Add(new List<Tile>());

        holdNextSparkTime = new float[columnCount];
        holdNextStreamTime = new float[columnCount];
        holdSparks = new List<HoldSpark>();

        wrongPressFlashTime = new float[columnCount];
        for (int i = 0; i < columnCount; i++) wrongPressFlashTime[i] = -999f; // no flash at start

        string chartSource = (chartFile != null) ? chartFile.text : chart;
        var parsed = ParseChart(chartSource);
        parsedBeats = parsed.beats;
        parsedTempos = parsed.tempos;
        useChart = parsedBeats.Length > 0;
        chartActive = useChart;

        // Determine the chart BPM in effect at the starting beat. Tempo markers
        // (pure-number chart lines) override the Inspector BPM; if the player
        // jumps into a section partway through, the active tempo is the last
        // marker at or before that beat. nextTempoIdx is advanced past every
        // marker that's already "in effect" so the spawn loop only applies
        // genuinely upcoming changes.
        int startBeat = useChart ? Mathf.Clamp(startBeatIndex, 0, parsedBeats.Length - 1) : 0;
        float initialBpm = bpm;
        nextTempoIdx = 0;
        if (parsedTempos != null)
        {
            for (int i = 0; i < parsedTempos.Length; i++)
            {
                if (parsedTempos[i].beatIndex <= startBeat)
                {
                    initialBpm = parsedTempos[i].bpm;
                    nextTempoIdx = i + 1;
                }
                else break;
            }
        }
        initialChartBpm = initialBpm;

        // Apply the speed multiplier to the (chart- or Inspector-derived) BPM.
        // Both spawn cadence and fall speed scale together with the music.
        float effectiveBpm = initialBpm * Mathf.Max(0.01f, speedMultiplier);
        secondsPerBeat = 60f / Mathf.Max(1f, effectiveBpm);
        // Tiles fall exactly one tile-height per beat, so consecutive beats touch top-to-bottom.
        fallSpeed = tileHeight * effectiveBpm / 60f;

        if (useChart)
        {
            currentBeatIndex = startBeat;
            nextBeatTime = Time.time + secondsPerBeat; // one-beat lead-in
            StartMusic(sessionStartDsp);
        }
        else
        {
            nextSpawnTime = Time.time + Random.Range(minSpawnInterval, maxSpawnInterval);
        }
    }

    /// <summary>The minigame song's AudioSource (null until a song has started).
    /// Exposed so GameDirector can fade it out when exiting the SONG COMPLETE screen.</summary>
    public AudioSource MusicSource => audioSource;

    public void EndSession(bool keepMusicPlaying = false)
    {
        if (!sessionActive) return;
        sessionActive = false;

        // keepMusicPlaying leaves the song sounding (the SONG COMPLETE screen lets the
        // track keep playing). Gameplay still stops — tiles are cleared and Update
        // early-outs on !sessionActive — and the caller must keep the GameObject active
        // for the audio to continue.
        if (audioSource != null && !keepMusicPlaying) audioSource.Stop();

        // The speed-adjusted clip is ours (AudioClip.Create). Free it once the music
        // has actually stopped. When keepMusicPlaying is true the SONG COMPLETE screen
        // is still playing it, so leave it be — StartMusic frees the previous one when
        // the next song starts.
        if (!keepMusicPlaying && generatedClip != null) { Destroy(generatedClip); generatedClip = null; }

        // Destroy everything we drew or spawned (tiles, lines, hit zones, labels, miss counter).
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }

        tilesPerColumn = null;
        hitZoneRenderers = null;
        missCounterText = null;
        // The hold burst sprites and any in-flight sparks are children of this
        // transform, so the loop above already destroyed their GameObjects.
        // Clear the references so a fresh BeginSession starts clean.
        holdBurstRenderers = null;
        holdNextSparkTime = null;
        holdNextStreamTime = null;
        holdSparks = null;
        wrongPressFlashTime = null;

        missCount = 0;
        leftHandMissCount = 0;
        rightHandMissCount = 0;
        hitCount = 0;
        totalTileCount = 0;
        useChart = false;
        chartActive = false;
        currentBeatIndex = 0;
    }

    void CalculateDimensions()
    {
        var cam = Camera.main;
        screenTopY = cam.orthographicSize;
        screenBottomY = -cam.orthographicSize;
        screenRightX = cam.orthographicSize * cam.aspect;
        float screenWidthWorld = 2f * cam.orthographicSize * cam.aspect;

        playFieldWidth = screenWidthWorld * playFieldWidthRatio;
        playFieldLeft = -playFieldWidth / 2f;
        playFieldRight = playFieldWidth / 2f;
        columnWidth = playFieldWidth / columnCount;

        hitLineY = Mathf.Lerp(screenBottomY, screenTopY, hitLineFromBottomRatio);
        spawnY = screenTopY + tileHeight; // just off the top

        // Keep the outline at least minOutlinePixels wide on screen. A fixed world-space
        // thickness can shrink to sub-pixel on a small canvas (e.g. the WebGL build), which
        // is what makes the border shimmer and the band between two stacked tiles vanish.
        // Clamp so a big bump can never swallow the tile body.
        float worldPerPixel = (Screen.height > 0) ? (2f * cam.orthographicSize) / Screen.height : 0f;
        float tw = columnWidth * tileWidthRatio;
        effectiveOutline = Mathf.Clamp(Mathf.Max(tileOutlineThickness, minOutlinePixels * worldPerPixel), 0f, tw * 0.32f);
    }

    float ColumnCenterX(int column) => playFieldLeft + columnWidth * (column + 0.5f);

    GameObject MakeQuad(string name, Vector3 position, float width, float height, Color color, int sortingOrder = 0)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        go.transform.position = position;
        go.transform.localScale = new Vector3(width, height, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SquareSprite;
        sr.color = color;
        sr.sortingOrder = sortingOrder;
        return go;
    }

    void DrawBackground()
    {
        if (background == null) return;

        var go = new GameObject("Background");
        go.transform.SetParent(transform);
        go.transform.position = Vector3.zero; // camera is at (0,0,-z) during minigame, looking at origin

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = background;
        sr.color = new Color(backgroundBrightness, backgroundBrightness, backgroundBrightness, backgroundOpacity);
        sr.sortingOrder = -100; // far behind every gameplay element

        // Cover the camera view (scale so the sprite fills both axes).
        float screenWidthWorld  = 2f * Camera.main.orthographicSize * Camera.main.aspect;
        float screenHeightWorld = 2f * Camera.main.orthographicSize;
        Vector2 sz = background.bounds.size;
        if (sz.x <= 0f || sz.y <= 0f) return;
        float scale = Mathf.Max(screenWidthWorld / sz.x, screenHeightWorld / sz.y);
        go.transform.localScale = new Vector3(scale, scale, 1f);
    }

    void DrawPlayfieldShade()
    {
        // Slightly darkens the column area only (the background stays at its
        // configured brightness outside the playfield), so tiles and lines keep
        // their contrast over bright song backgrounds.
        if (background == null) return;
        float height = screenTopY - screenBottomY;
        MakeQuad("PlayfieldShade", Vector3.zero, playFieldWidth, height,
                 new Color(0f, 0f, 0f, 0.30f), -50);
    }

    void DrawColumnLines()
    {
        float height = screenTopY - screenBottomY;
        // 11 lines for 10 columns (both outer edges + 9 dividers). The outer
        // edges stay at full strength to frame the playfield; the inner
        // dividers recede slightly so the falling tiles carry the scene.
        for (int i = 0; i <= columnCount; i++)
        {
            bool isEdge = (i == 0 || i == columnCount);
            Color c = columnLineColor;
            if (!isEdge) c.a *= 0.5f;
            float lineThickness = isEdge ? 0.06f : 0.04f;
            float x = playFieldLeft + columnWidth * i;
            MakeQuad($"ColumnLine_{i}", new Vector3(x, 0f, 0f), lineThickness, height, c, 1);
        }
    }

    void DrawHitLine()
    {
        MakeQuad("HitLine", new Vector3(0f, hitLineY, 0f), playFieldWidth, 0.08f, hitLineColor, 2);
    }

    void DrawHitZones()
    {
        // Subtle highlight in each column's hit-zone area so labels read better
        float zoneHeight = 0.9f;
        hitZoneRenderers = new SpriteRenderer[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            var go = MakeQuad(
                $"HitZone_{i}",
                new Vector3(ColumnCenterX(i), hitLineY - zoneHeight / 2f, 0f),
                columnWidth * 0.95f,
                zoneHeight,
                hitZoneColor,
                0
            );
            hitZoneRenderers[i] = go.GetComponent<SpriteRenderer>();
        }
    }

    void DrawLabels()
    {
        for (int i = 0; i < columnCount; i++)
        {
            if (string.IsNullOrEmpty(columnLabels[i])) continue;
            var go = new GameObject($"Label_{i}");
            go.transform.SetParent(transform);
            go.transform.position = new Vector3(ColumnCenterX(i), hitLineY - 0.45f, 0f);

            var tmp = go.AddComponent<TextMeshPro>();
            if (playfieldFont != null) tmp.font = playfieldFont;
            tmp.text = columnLabels[i];
            // Shrink the font for multi-character labels so a rebound key like
            // "Space" or "Enter" still fits inside its column.
            tmp.fontSize = columnLabels[i].Length <= 1 ? 4.5f : 2.5f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = labelColor;
            tmp.sortingOrder = 10;

            // Make the text component sized so alignment behaves nicely
            var rt = tmp.rectTransform;
            rt.sizeDelta = new Vector2(columnWidth, 0.8f);
        }
    }

    // Maps a column's bound KeyCode to a short on-screen label. Letters return
    // themselves; common symbols return their printed character; named keys get
    // a short word. Falls back to KeyCode.ToString() for anything unhandled.
    public static string KeyCodeToColumnLabel(KeyCode kc)
    {
        if (kc >= KeyCode.A && kc <= KeyCode.Z) return kc.ToString();
        if (kc >= KeyCode.Alpha0 && kc <= KeyCode.Alpha9)
            return ((int)(kc - KeyCode.Alpha0)).ToString();
        switch (kc)
        {
            case KeyCode.Space:        return "Space";
            case KeyCode.Semicolon:    return ";";
            case KeyCode.Comma:        return ",";
            case KeyCode.Period:       return ".";
            case KeyCode.Slash:        return "/";
            case KeyCode.LeftBracket:  return "[";
            case KeyCode.RightBracket: return "]";
            case KeyCode.Quote:        return "'";
            case KeyCode.BackQuote:    return "`";
            case KeyCode.Minus:        return "-";
            case KeyCode.Equals:       return "=";
            case KeyCode.Backslash:    return "\\";
            case KeyCode.Tab:          return "Tab";
            case KeyCode.Return:       return "Enter";
            case KeyCode.KeypadEnter:  return "Enter";
            case KeyCode.LeftShift:    case KeyCode.RightShift:   return "Shift";
            case KeyCode.LeftControl:  case KeyCode.RightControl: return "Ctrl";
            case KeyCode.LeftAlt:      case KeyCode.RightAlt:     return "Alt";
        }
        return kc.ToString();
    }

    // Columns 4 and 5 share the SPACE binding, so instead of letter labels we
    // draw a small '⎵'-style space-bar glyph that straddles the dividing line
    // between them — a thin horizontal bar with short upright caps at each end.
    // The whole symbol is centered on the column-4/5 divider and stretches across
    // most of the combined width of both hit-zone boxes, so it visually reads as
    // "press SPACE for either of these two columns." Only draws when BOTH cols
    // are still mapped to Space — if the player has rebound either of them, the
    // per-column key labels take over and this glyph is suppressed.
    void DrawSpaceBarSymbol()
    {
        if (columnKeys[4] != KeyCode.Space || columnKeys[5] != KeyCode.Space) return;
        // The column-4/5 divider is the 5th internal column line (0-indexed from
        // the left edge of the playfield).
        float dividerX = playFieldLeft + columnWidth * 5f;
        // Same vertical row the letter labels sit on, so the symbol feels like
        // it belongs to the same row.
        float centerY = hitLineY - 0.45f;
        // Spans most of the two-column combined width without quite touching the
        // outer column lines, so the symbol reads as belonging to BOTH boxes.
        float symbolWidth = columnWidth * 1.5f;
        float halfWidth   = symbolWidth / 2f;
        float lineThickness = 0.08f;
        float capHeight     = 0.32f;

        // Horizontal bar — the floor of the '⎵'. Sits at the bottom of the
        // symbol's vertical extent so the upright caps appear to "rise" from it.
        float barCenterY = centerY - capHeight / 2f + lineThickness / 2f;
        MakeQuad("SpaceBarSymbol_Bar",
                 new Vector3(dividerX, barCenterY, 0f),
                 symbolWidth, lineThickness, labelColor, 10);

        // Left and right upright end-caps. Centered on centerY (so their bottom
        // edges meet the top of the horizontal bar) and tucked just inside the
        // symbol's horizontal extents.
        MakeQuad("SpaceBarSymbol_LeftCap",
                 new Vector3(dividerX - halfWidth + lineThickness / 2f, centerY, 0f),
                 lineThickness, capHeight, labelColor, 10);
        MakeQuad("SpaceBarSymbol_RightCap",
                 new Vector3(dividerX + halfWidth - lineThickness / 2f, centerY, 0f),
                 lineThickness, capHeight, labelColor, 10);
    }

    void Update()
    {
        if (!sessionActive) return;

        // 1. Spawn
        if (useChart)
        {
            // Apply any tempo marker(s) scheduled at the beat we're about to
            // spawn, BEFORE the spawn timing check, so the new tempo's spawn
            // cadence + fall speed (and the lead-time compensation in
            // ApplyTempo) govern this beat onward.
            if (chartActive)
            {
                while (nextTempoIdx < parsedTempos.Length
                       && parsedTempos[nextTempoIdx].beatIndex == currentBeatIndex)
                {
                    ApplyTempo(parsedTempos[nextTempoIdx].bpm);
                    nextTempoIdx++;
                }
            }

            if (chartActive && Time.time >= nextBeatTime)
            {
                // Spawn the tile where it WOULD be had it appeared exactly on its beat,
                // not at this (slightly late) frame. Every tile otherwise spawns at a
                // fixed Y, so per-frame timing jitter — especially a frame hitch — leaks
                // into the spacing: one pair ends up overlapping and the next gapped even
                // though consecutive tiles should sit snug. The beat is "due" at
                // nextBeatTime; by now it should already have fallen for the overshoot,
                // so start it that much lower. This keeps spacing frame-rate independent.
                float beatOvershoot = Time.time - nextBeatTime;
                SpawnBeat(parsedBeats[currentBeatIndex], -fallSpeed * beatOvershoot);
                currentBeatIndex++;
                if (currentBeatIndex >= parsedBeats.Length)
                {
                    if (loopChart) { currentBeatIndex = 0; LoopResetTempo(); }
                    else chartActive = false;
                }
                nextBeatTime += secondsPerBeat;
            }
        }
        else if (Time.time >= nextSpawnTime)
        {
            SpawnTileGroup();
            nextSpawnTime = Time.time + Random.Range(minSpawnInterval, maxSpawnInterval);
        }

        // 2. Move tiles, then resolve miss/destroy. The miss timing depends on
        //    whether the tile is a 1-beat tap or a multi-beat hold:
        //      • 1-beat tiles: a miss fires the moment the TOP edge crosses
        //        BELOW the hit line — i.e. the entire tile is now past the
        //        line. After that point the tile is no longer grabbable
        //        (TryGrab's top threshold is hitLineY for 1-beat tiles), and
        //        a press in that frame can't save it. The tile keeps falling
        //        visually until off-screen, where it's then destroyed.
        //      • Multi-beat hold tiles: the miss is deferred all the way to
        //        off-screen (top < screenBottomY) so a fast-falling tile that
        //        passes the line in less than 1 beat of wall-clock time can
        //        still be saved by holding the key. The last-chance auto-secure
        //        treats "still holding when the tile crosses off-screen" as
        //        a valid catch even if held < 1 beat.
        float step = fallSpeed * Time.deltaTime;
        for (int col = 0; col < columnCount; col++)
        {
            var list = tilesPerColumn[col];
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var t = list[i];
                if (t == null) { list.RemoveAt(i); continue; }
                t.Fall(step);

                float top = t.transform.position.y + t.visualHeight / 2f;

                // Tap miss: fires once the tile is past the hit line AND the late-
                // catch window has also elapsed, so a slightly-late tap still counts
                // as a hit instead of missing (and letting the tile above it get
                // grabbed). Hold tiles — incl. tall single-beat ones — are resolved at
                // off-screen below instead, never here.
                if (!IsHoldTile(t)
                    && top < hitLineY - fallSpeed * lateHitTrailTime
                    && !t.missFired && !t.catchSecured)
                {
                    RegisterMiss(col);
                    t.missFired = true;
                    if (!t.spent) t.spent = true;
                }

                // Missed-tile dissolve (purely visual): once an UNCAUGHT tile has fallen
                // past the point where it can still be hit — a tap past its late window,
                // a hold past the hit line — flash it a muted red and fade it out as it
                // drops off the bottom. The off-screen block below still destroys it and
                // the miss is still counted exactly as before.
                float dissolveThreshold = IsHoldTile(t) ? hitLineY : (hitLineY - fallSpeed * lateHitTrailTime);
                if (!t.caught && top < dissolveThreshold)
                {
                    if (!t.dissolving) { t.dissolving = true; t.dissolveStart = Time.time; }
                    ApplyMissDissolve(t, top, dissolveThreshold);
                }

                if (top < screenBottomY)
                {
                    // Hold miss (any hold tile, incl. tall single-beat): deferred to
                    // off-screen, with the still-holding auto-secure for fast holds.
                    if (IsHoldTile(t)
                        && !t.missFired && !t.catchSecured)
                    {
                        bool stillHolding = t.grabbed && Input.GetKey(columnKeys[col]);
                        if (stillHolding)
                        {
                            SecureCatch(t);
                        }
                        else
                        {
                            RegisterMiss(col);
                            t.missFired = true;
                        }
                    }
                    if (!t.spent) t.spent = true;
                    if (!t.caught)
                    {
                        Destroy(t.gameObject);
                        list.RemoveAt(i);
                    }
                }
            }
        }

        // 3. (Removed) Hit-zone glow telegraph — hit zones now stay at their base
        //    color the whole time. The pulsing hold-effect at the hit line and the
        //    catch flash on the tile itself provide enough feedback without the
        //    pre-hit telegraph.

        // 4. Resolve in-progress holds (grabbed tiles — multi-beat AND tall single-
        //    beat tiles). The tile stays in the "grabbed && !spent" state — and
        //    therefore stays fully opaque, with the spark stream + clip animation
        //    playing — for as long as the player keeps the key down. Two things
        //    can happen here:
        //      • Once held has reached secondsPerBeat (1 beat), the catch is
        //        SECURED. The player can release any time after that without
        //        penalty — no miss will ever be registered for this tile.
        //      • If the player releases (keyDown false), the tile is marked
        //        spent so step 6 can start the post-release fade. We DON'T fire
        //        a miss here even if held < secondsPerBeat — the miss is
        //        decided in step 2 when the tile crosses off-screen, so a
        //        fast-falling tile whose visible duration was shorter than 1
        //        beat still has a chance of being saved by the off-screen
        //        auto-secure (if the player kept holding).
        //      • If the player is still holding when the tile fully exits the
        //        camera, auto-secure here too — they effectively held until
        //        the end.
        for (int col = 0; col < columnCount; col++)
        {
            var holdList = tilesPerColumn[col];
            for (int i = 0; i < holdList.Count; i++)
            {
                var t = holdList[i];
                if (t == null) continue;
                if (!t.grabbed || t.spent) continue;

                float held = Time.time - t.grabTime;
                bool keyDown = Input.GetKey(columnKeys[col]);

                if (!t.catchSecured && held >= secondsPerBeat)
                    SecureCatch(t);

                if (!keyDown)
                {
                    t.spent = true;
                }
                else
                {
                    float top = t.transform.position.y + t.visualHeight / 2f;
                    if (top < screenBottomY)
                    {
                        SecureCatch(t);
                        t.spent = true;
                    }
                }
            }
        }

        // 4b. Hold effect — pulsing core + spark stream at the hit line for every
        //     column currently sustaining a multi-beat tile.
        UpdateHoldEffects();

        // 4c. Hold clip (mobile-Piano-Tiles style) — for every caught multi-beat
        //     tile, hide the portion of its body+outline that has fallen below
        //     the hit line. The parent transform keeps falling naturally; only
        //     the children's localScale.y / localPosition.y are trimmed, so the
        //     tile appears to "feed into" the hit line as the player holds.
        for (int col = 0; col < columnCount; col++)
        {
            var clipList = tilesPerColumn[col];
            for (int i = 0; i < clipList.Count; i++)
            {
                var t = clipList[i];
                if (t == null) continue;
                if (!t.caught || !IsHoldTile(t)) continue;
                ApplyHoldClip(t);
            }
        }

        // 5. Input (fresh key-down events) — suppressed briefly after BeginSession
        if (Time.time >= inputSuppressedUntil)
        {
            var processedKeys = new HashSet<KeyCode>();
            for (int col = 0; col < columnCount; col++)
            {
                var key = columnKeys[col];
                if (processedKeys.Contains(key)) continue;
                if (!Input.GetKeyDown(key)) continue;
                processedKeys.Add(key);

                // Try every column bound to this key (SPACE covers columns 4 and 5).
                bool anyHit = false;
                for (int c = 0; c < columnCount; c++)
                {
                    if (columnKeys[c] == key && TryGrab(c))
                        anyHit = true;
                }
                // For SPACE, the outer-loop col is 4 (left SPACE column), so the
                // miss is attributed to the left hand — consistent with the
                // chart's g/h convention where g sits in the left half.
                if (!anyHit)
                {
                    RegisterMiss(col);
                    // Flash the hit-zone box(es) bound to this key red (SPACE covers cols 4 & 5).
                    for (int c = 0; c < columnCount; c++)
                        if (columnKeys[c] == key) wrongPressFlashTime[c] = Time.time;
                }
            }
        }

        // 5b. Fade each hit-zone box back from any recent wrong-time press.
        UpdateWrongPressFlashes();

        // 6. Catch-flash + duration fade for caught tiles
        for (int col = 0; col < columnCount; col++)
        {
            var fadeList = tilesPerColumn[col];
            for (int i = fadeList.Count - 1; i >= 0; i--)
            {
                var t = fadeList[i];
                if (t == null) { fadeList.RemoveAt(i); continue; }
                if (!t.caught) continue;

                // Press feedback: a tiny WIDTH-only shrink so a caught tile feels
                // "pressed". Taps snap to the pressed width; multi-beat holds ease into
                // it over the FIRST HALF of the note (slower the longer the note) and
                // then hold there — and freeze wherever they are the moment the key is
                // released. Height/length is never touched, so hold tiles keep their
                // full length. A released hold falls through both branches below, so
                // its pressFactor simply stays frozen at its last value.
                float pressTarget = 1f - pressShrink;
                if (IsHoldTile(t) && t.grabbed && !t.spent)
                {
                    float pressHalf = 0.5f * t.durationBeats * secondsPerBeat;
                    float prog = pressHalf > 0.0001f ? Mathf.Clamp01((Time.time - t.grabTime) / pressHalf) : 1f;
                    t.pressFactor = Mathf.Lerp(1f, pressTarget, prog);
                }
                else if (!IsHoldTile(t))
                {
                    float prog = pressTapDuration > 0.0001f ? Mathf.Clamp01((Time.time - t.caughtTime) / pressTapDuration) : 1f;
                    t.pressFactor = Mathf.Lerp(1f, pressTarget, prog);
                }
                t.transform.localScale = new Vector3(t.pressFactor, 1f, 1f);

                // --- Branch A: still being held ------------------------------------
                // The tile's alpha drifts from 1 down to holdFadeMinAlpha over its
                // natural duration (durationBeats * secondsPerBeat) — so by the time
                // the held tile would have run out, it's at ~0.3 opacity (default).
                // Holding past that point keeps it pinned at the floor; no further
                // fade until the player releases the key. The catch-flash color is
                // still applied to the body so the held tile reads as "lit up", and
                // caughtTime is anchored to Time.time so the post-release fade
                // starts cleanly at t=0 when t.spent eventually flips.
                if (t.grabbed && !t.spent)
                {
                    float holdElapsed = Time.time - t.grabTime;
                    float holdDuration = Mathf.Max(0.0001f, t.durationBeats * secondsPerBeat);
                    float holdProgress = Mathf.Clamp01(holdElapsed / holdDuration);
                    float heldAlpha = Mathf.Lerp(1f, holdFadeMinAlpha, holdProgress);

                    if (t.spriteRenderer != null)
                    {
                        // Long holds glow ORANGE (brightening as the hold intensifies);
                        // normal holds keep the yellow catch-flash color.
                        Color c;
                        if (t.durationBeats >= longHoldBeatThreshold)
                        {
                            float intensity = Mathf.Clamp01(holdElapsed / Mathf.Max(0.01f, longHoldRampSeconds));
                            // Dimmer orange early → full longHoldGlowColor at peak intensity.
                            c = Color.Lerp(longHoldGlowColor * 0.6f, longHoldGlowColor, intensity);
                        }
                        else
                        {
                            c = catchFlashColor;
                        }
                        c.a = heldAlpha;
                        t.spriteRenderer.color = c;
                    }
                    if (t.outlineRenderer != null)
                    {
                        var oc = t.outlineColor;
                        oc.a = t.outlineColor.a * heldAlpha;
                        t.outlineRenderer.color = oc;
                    }

                    t.releaseAlpha = heldAlpha; // post-release fade starts here
                    t.caughtTime = Time.time;   // anchor the fade clock
                    continue;
                }

                // --- Branch B: post-release (or tap) fade --------------------------
                // For taps, releaseAlpha defaulted to 1 in TryGrab so this behaves
                // identically to the old fade. For released holds, releaseAlpha is
                // whatever the held-alpha was at the moment of release, so the tile
                // fades smoothly from that value to 0 without snapping back to full
                // opacity first.
                float elapsed = Time.time - t.caughtTime;
                float totalFade = t.durationBeats * secondsPerBeat;

                if (elapsed >= totalFade)
                {
                    Destroy(t.gameObject);
                    fadeList.RemoveAt(i);
                    continue;
                }

                float alpha = t.releaseAlpha * (1f - elapsed / totalFade);
                float flashFrac = catchFlashDuration > 0f
                    ? 1f - Mathf.Min(elapsed / catchFlashDuration, 1f)
                    : 0f;

                if (t.spriteRenderer != null)
                {
                    Color c = Color.Lerp(t.bodyColor, catchFlashColor, flashFrac);
                    c.a = alpha;
                    t.spriteRenderer.color = c;
                }
                if (t.outlineRenderer != null)
                {
                    var oc = t.outlineColor;
                    oc.a = t.outlineColor.a * alpha;
                    t.outlineRenderer.color = oc;
                }
            }
        }
    }

    bool TryGrab(int column)
    {
        var list = tilesPerColumn[column];
        // Grab the LOWEST unresolved tile that's within reach. "Lowest" means the
        // one closest to / just past the hit line, so when two tiles are stacked
        // (consecutive notes in a column) a press always takes the one being played
        // — never the tile above it.
        //
        // Reach is bounded on both ends:
        //   • Top end (early): bottom may sit up to earlyLeadDistance ABOVE the
        //     hit line. Lets a press a few frames ahead of contact still count.
        //   • Bottom end (late):
        //       – Taps get a short late window: catchable until the top has fallen
        //         lateTrailDistance BELOW the hit line (and step 2's miss is held off
        //         to the same point). So a slightly-late tap still lands and glows.
        //         Taps aren't clipped, so the catch flash shows wherever they are.
        //       – Hold tiles are catchable only while they still OVERLAP the hit line
        //         (top at/above it). They span the line for their whole duration, so
        //         this is a long window — and it deliberately stops short of a hold
        //         that's fully below the line, which the hold-clip would otherwise
        //         render to nothing (the "press → hit-line particle but no glowing
        //         tile" glitch). They miss at off-screen instead.
        float earlyLeadDistance = fallSpeed * earlyHitLeadTime;
        float lateTrailDistance = fallSpeed * lateHitTrailTime;
        Tile best = null;
        int bestIdx = -1;
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            if (t == null) continue;
            if (t.grabbed || t.spent) continue;
            float top = t.transform.position.y + t.visualHeight / 2f;
            float bottom = t.transform.position.y - t.visualHeight / 2f;
            float topThreshold = IsHoldTile(t) ? hitLineY : (hitLineY - lateTrailDistance);
            if (bottom <= hitLineY + earlyLeadDistance && top >= topThreshold)
            {
                if (best == null || t.transform.position.y < best.transform.position.y)
                {
                    best = t;
                    bestIdx = i;
                }
            }
        }
        if (best == null) return false;

        if (!IsHoldTile(best))
        {
            // Tap: counted as a hit immediately; fade step will handle the visuals.
            best.spent = true;
            best.caught = true;
            best.caughtTime = Time.time;
            best.releaseAlpha = 1f;       // tap fades from full opacity
            SecureCatch(best);            // tap is unrevocable the moment it lands (a hit)
        }
        else
        {
            // Hold: mark grabbed; verdict decided in the hold-resolution step
            // (fade only starts once the player releases the key). releaseAlpha
            // is updated every frame in step 6's Branch A as the hold-fade
            // progresses, so the post-release fade picks up from there.
            // catchSecured stays false until step 4 sets it (held for >= 1 beat,
            // or auto-secured at off-screen if the player is still holding).
            best.grabbed = true;
            best.grabTime = Time.time;
            best.caught = true;
            best.caughtTime = Time.time;
            best.releaseAlpha = 1f;
        }
        return true;
    }

    void SpawnTileGroup()
    {
        int count = 1;
        if (Random.value < multiSpawnChance)
            count = Random.Range(2, maxChordSize + 1);

        // Pick distinct columns AND distinct keys, so a "chord" can never collapse
        // into a single keypress (e.g. columns 4 and 5 both map to SPACE).
        var chosenCols = new List<int>();
        var usedKeys = new HashSet<KeyCode>();
        var pool = new List<int>();
        for (int i = 0; i < columnCount; i++)
        {
            // Skip columns whose most recent tile hasn't fallen far enough
            // to leave room for another one without visual overlap.
            if (!HasRoomToSpawn(i)) continue;
            pool.Add(i);
        }

        while (chosenCols.Count < count && pool.Count > 0)
        {   
            int idx = Random.Range(0, pool.Count);
            int col = pool[idx];
            pool.RemoveAt(idx);

            if (usedKeys.Contains(columnKeys[col])) continue;
            usedKeys.Add(columnKeys[col]);
            chosenCols.Add(col);
        }

        foreach (var c in chosenCols)
        {
            SpawnTileAt(c);
        }
    }

    // Time it takes a freshly-spawned tile's bottom edge to fall from the spawn
    // point to the hit line, at the CURRENT fall speed. This is the "lead time"
    // the music scheduling and tempo compensation both rely on.
    float CurrentFallTime()
    {
        float bottomSpawnY = screenTopY + tileHeight / 2f;
        return (bottomSpawnY - hitLineY) / Mathf.Max(0.0001f, fallSpeed);
    }

    // Apply a mid-song tempo change. Recomputes the spawn cadence and fall speed
    // from the new chart BPM (scaled by the practice speed multiplier), then
    // shifts the next spawn time to compensate for the changed lead time so the
    // new section stays in sync with the recording.
    //
    // Why the compensation: at a faster tempo a tile falls quicker, so it would
    // reach the hit line sooner after spawning. Delaying the next spawn by the
    // drop in lead time keeps each beat landing on its music beat. We clamp so
    // we never schedule a spawn in the past (which on a big slow-DOWN would dump
    // several queued tiles at once); in that case the first beat of the slower
    // section may land a touch early, but no tile pile-up occurs.
    void ApplyTempo(float chartBpm)
    {
        float oldFallTime = CurrentFallTime();
        float effectiveBpm = chartBpm * Mathf.Max(0.01f, speedMultiplier);
        secondsPerBeat = 60f / Mathf.Max(1f, effectiveBpm);
        fallSpeed = tileHeight * effectiveBpm / 60f;
        float newFallTime = CurrentFallTime();
        nextBeatTime = Mathf.Max(nextBeatTime + (oldFallTime - newFallTime), Time.time);
    }

    // On a chart loop, snap the tempo back to the chart's opening tempo and rewind
    // the tempo-marker cursor so mid-song changes re-fire on the next pass. No
    // lead-time compensation here — the loop wrap is already a hard discontinuity.
    void LoopResetTempo()
    {
        nextTempoIdx = 0;
        float bpm0 = initialChartBpm;
        // If the chart opens with a tempo marker at beat 0, that IS the opening
        // tempo; skip it so the spawn loop doesn't redundantly re-apply it.
        if (parsedTempos != null)
        {
            for (int i = 0; i < parsedTempos.Length && parsedTempos[i].beatIndex == 0; i++)
            {
                bpm0 = parsedTempos[i].bpm;
                nextTempoIdx = i + 1;
            }
        }
        float effectiveBpm = bpm0 * Mathf.Max(0.01f, speedMultiplier);
        secondsPerBeat = 60f / Mathf.Max(1f, effectiveBpm);
        fallSpeed = tileHeight * effectiveBpm / 60f;
    }

    // Native (un-speed-scaled) seconds into the recording at a given chart beat,
    // summing each preceding beat's duration at the tempo in effect for it. Used
    // to seek the audio when the player starts from a section partway through a
    // tempo-varying chart. Falls back to a flat Inspector-BPM timeline when there
    // are no tempo markers.
    float NativeAudioTimeAtBeat(int beatIndex)
    {
        float t = 0f;
        float curBpm = bpm;
        int ti = 0;
        for (int b = 0; b < beatIndex; b++)
        {
            if (parsedTempos != null)
                while (ti < parsedTempos.Length && parsedTempos[ti].beatIndex <= b)
                {
                    curBpm = parsedTempos[ti].bpm;
                    ti++;
                }
            t += 60f / Mathf.Max(1f, curBpm);
        }
        return t;
    }

    // scheduleBaseDsp is AudioSettings.dspTime sampled at the top of BeginSession —
    // the same frame-start instant the tile timeline is anchored to via Time.time.
    // The music is scheduled relative to it (NOT a fresh dspTime read at the end of
    // this method) so the time spent resampling the song below doesn't shift the
    // music later than the tiles. See the capture site in BeginSession.
    void StartMusic(double scheduleBaseDsp)
    {
        if (song == null) return;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        float speed = Mathf.Max(0.01f, speedMultiplier);

        // WebGL's audio backend ignores AudioSource.pitch, so at a non-1x speed the
        // music would keep its original tempo while the tiles re-time to the new BPM —
        // they'd drift apart instantly. Instead of relying on pitch, bake the speed
        // straight into the PCM (resample) and play that at pitch 1. The result is
        // sample-identical to a pitch change but honored on every platform. At 1x we
        // play `song` directly (no copy, no cost). Rebuilt each session; the old one is
        // freed here and in EndSession.
        if (generatedClip != null) { Destroy(generatedClip); generatedClip = null; }
        AudioClip playClip = song;
        if (Mathf.Abs(speed - 1f) > 0.001f)
        {
            generatedClip = ResampleClip(song, speed);
            if (generatedClip != null) playClip = generatedClip;
        }

        // Reset to a fully clean state so the start offset is identical on every session.
        // (Stop alone doesn't always rewind reliably when the source is reused after a
        // PlayDelayed that may have been cancelled before it actually began.)
        audioSource.Stop();
        audioSource.clip = playClip;
        audioSource.timeSamples = 0;
        audioSource.volume = musicVolume;
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.pitch = 1f; // speed is baked into playClip; never rely on pitch (WebGL ignores it).

        // Audio playback position corresponds to startBeatIndex in the ORIGINAL song
        // timeline. NativeAudioTimeAtBeat returns that original-timeline position; since
        // the resampled clip is 1/speed as long, scale the seek into the clip we're
        // actually playing. (At 1x, playClip == song and speed == 1 → just the original
        // offset.) With mid-song tempo changes, beat N's original-timeline position is
        // the sum of each preceding beat's duration at its own tempo.
        float audioOffset = NativeAudioTimeAtBeat(currentBeatIndex);
        float clipTime = audioOffset / speed;
        if (playClip.length > 0.1f) clipTime = Mathf.Min(clipTime, playClip.length - 0.1f);
        if (clipTime < 0f) clipTime = 0f;
        audioSource.time = clipTime;

        // Music starts when the first tile's bottom edge reaches the hit line.
        // First tile spawns one beat after BeginSession; its bottom then falls
        // (bottomSpawnY - hitLineY) units to the line.
        // musicLeadOffset shifts it later (positive) or earlier (negative) for manual tuning.
        float bottomSpawnY = screenTopY + tileHeight / 2f;
        float fallTime = (bottomSpawnY - hitLineY) / fallSpeed;
        // musicLeadOffset is the per-song manual tuning; globalAudioOffset is the
        // player-set device-wide compensation (typically negative on Bluetooth).
        float musicDelay = Mathf.Max(0f, secondsPerBeat + fallTime + musicLeadOffset + globalAudioOffset);

        // Schedule on the DSP clock (sample-accurate). The base is the DSP time captured
        // at the TOP of BeginSession — the same frame-start instant the tile timeline is
        // anchored to via Time.time — NOT a fresh reading here, which would be offset by
        // however long the setup + song resample above took and would make the music
        // start late (tiles reaching the hit line ahead of their note). Guard the rare
        // case where setup outlasted the whole lead time, which would otherwise schedule
        // playback in the past.
        double scheduledDsp = scheduleBaseDsp + musicDelay;
        double dspNow = AudioSettings.dspTime;
        if (scheduledDsp < dspNow) scheduledDsp = dspNow;
        audioSource.PlayScheduled(scheduledDsp);
    }

    // Build a new AudioClip that plays `src` at `speed`x by resampling its PCM with
    // linear interpolation. speed > 1 → fewer frames (faster + higher pitch);
    // speed < 1 → more frames (slower + lower pitch). Same sonic result as
    // AudioSource.pitch, but baked into the samples so WebGL (which ignores pitch)
    // honors it. Returns null if the source has no readable data. Allocation is a
    // one-time cost at song start; the temp arrays are GC'd and the clip itself is
    // freed by StartMusic/EndSession.
    static AudioClip ResampleClip(AudioClip src, float speed)
    {
        if (src == null || speed <= 0f) return null;
        int channels = Mathf.Max(1, src.channels);
        int srcFrames = src.samples; // frames per channel
        if (srcFrames <= 0) return null;

        float[] srcData = new float[srcFrames * channels];
        if (!src.GetData(srcData, 0)) return null;

        // WebGL's AudioClip.GetData has historically returned all-zeros (the decoded
        // PCM can live in the browser's audio context, out of reach of managed code).
        // If that happens, resampling would yield a silent track — detect it via a
        // sparse scan and bail, so StartMusic falls back to the original clip (played
        // unspeeded) rather than shipping silence.
        bool hasAudio = false;
        int scanStep = Mathf.Max(1, srcData.Length / 4096);
        for (int s = 0; s < srcData.Length; s += scanStep)
            if (srcData[s] != 0f) { hasAudio = true; break; }
        if (!hasAudio)
        {
            Debug.LogWarning("[PianoTiles] Couldn't read the song's PCM (AudioClip.GetData " +
                "returned silence on this platform), so the speed change can't be baked into the " +
                "audio. Playing at original speed. If this is the WebGL build, pre-bake speed variants.");
            return null;
        }

        int outFrames = Mathf.Max(1, Mathf.RoundToInt(srcFrames / speed));
        float[] outData = new float[outFrames * channels];

        int lastFrame = srcFrames - 1;
        for (int i = 0; i < outFrames; i++)
        {
            float srcPos = i * speed;          // fractional source frame
            int i0 = (int)srcPos;
            if (i0 > lastFrame) i0 = lastFrame;
            int i1 = i0 < lastFrame ? i0 + 1 : lastFrame;
            float frac = srcPos - i0;
            int o = i * channels;
            int b0 = i0 * channels;
            int b1 = i1 * channels;
            for (int c = 0; c < channels; c++)
            {
                float a = srcData[b0 + c];
                outData[o + c] = a + (srcData[b1 + c] - a) * frac;
            }
        }

        var clip = AudioClip.Create(src.name + "_x" + speed.ToString("0.00"),
                                    outFrames, channels, src.frequency, false);
        clip.SetData(outData, 0);
        return clip;
    }

    static readonly char[] LineSplit = new[] { '\n', '\r' };

    public struct BeatEntry
    {
        public string text;
        public int groupIndex; // -1 = not in any group; 0,1,2,... = group ordinal (alternating colors)
    }

    public struct Section
    {
        public string name;
        public int beatIndex; // index into the beats array — first beat that belongs to this section
    }

    public struct TempoChange
    {
        public float bpm;     // the new tempo (chart BPM) starting at beatIndex
        public int beatIndex; // index into the beats array — first beat at this tempo
    }

    public struct ChartParseResult
    {
        public BeatEntry[] beats;
        public Section[] sections;
        public TempoChange[] tempos;
    }

    public static ChartParseResult ParseChart(string raw)
    {
        var empty = new ChartParseResult
        {
            beats = System.Array.Empty<BeatEntry>(),
            sections = System.Array.Empty<Section>(),
            tempos = System.Array.Empty<TempoChange>(),
        };
        if (string.IsNullOrEmpty(raw)) return empty;

        var lines = raw.Split(LineSplit, System.StringSplitOptions.RemoveEmptyEntries);
        var result = new List<BeatEntry>(lines.Length);
        var sections = new List<Section>();
        var tempos = new List<TempoChange>();
        int currentGroup = -1;
        int nextGroupId = 0;

        foreach (var line in lines)
        {
            // Extract trailing "//" comment (kept around in case the line has nothing else).
            string s = line;
            string commentText = null;
            int commentIdx = s.IndexOf("//");
            if (commentIdx >= 0)
            {
                commentText = s.Substring(commentIdx + 2).Trim();
                s = s.Substring(0, commentIdx);
            }
            s = s.Trim();

            if (s.Length == 0)
            {
                // Blank or comment-only line. If it carries comment text, record it as a
                // section marker pointing to the NEXT beat that gets added.
                if (!string.IsNullOrEmpty(commentText))
                    sections.Add(new Section { name = commentText, beatIndex = result.Count });
                continue;
            }

            // Group markers — a line consisting of just '(' or ')' (with optional whitespace).
            if (s == "(") { currentGroup = nextGroupId++; continue; }
            if (s == ")") { currentGroup = -1; continue; }

            // Tempo marker — a line that's JUST a number (no key letters, since
            // every key token starts with a letter or ';'/'|'/'-'). It sets the
            // chart BPM starting at the next beat that gets added. A tempo line
            // at the very top overrides the Inspector BPM for the whole song;
            // a different number mid-chart is a tempo change from that point on.
            if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float tempoBpm)
                && tempoBpm > 0f)
            {
                tempos.Add(new TempoChange { bpm = tempoBpm, beatIndex = result.Count });
                continue;
            }

            // Skip lines that contain only '|' (bar dividers)
            bool allBars = true;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '|') { allBars = false; break; }
            }
            if (allBars) continue;

            result.Add(new BeatEntry { text = s, groupIndex = currentGroup });
        }
        return new ChartParseResult
        {
            beats = result.ToArray(),
            sections = sections.ToArray(),
            tempos = tempos.ToArray(),
        };
    }

    void SpawnBeat(BeatEntry entry, float spawnYOffset = 0f)
    {
        if (string.IsNullOrWhiteSpace(entry.text)) return;
        var trimmed = entry.text.Trim();
        if (trimmed == "-") return;

        // Group 0,2,4,... or no group → default colors (black body / white outline).
        // Group 1,3,5,...           → inverted colors (white body / black outline).
        bool inverted = entry.groupIndex >= 0 && (entry.groupIndex % 2 == 1);

        var parts = trimmed.Split(',');
        foreach (var p in parts)
        {
            var s = p.Trim();
            if (string.IsNullOrEmpty(s)) continue;

            int col = LetterToColumn(s[0]);
            if (col < 0 || col >= columnCount) continue;

            int duration = 1;
            if (s.Length > 1 && (!int.TryParse(s.Substring(1), out duration) || duration < 1))
                duration = 1;

            SpawnTileAt(col, duration, inverted, spawnYOffset);
        }
    }

    static int LetterToColumn(char c)
    {
        switch (char.ToLower(c))
        {
            case 'a': return 0;
            case 's': return 1;
            case 'd': return 2;
            case 'f': return 3;
            case 'g': return 4; // designator for left SPACE column
            case 'h': return 5; // designator for right SPACE column
            case 'j': return 6;
            case 'k': return 7;
            case 'l': return 8;
            case ';': return 9;
            default:  return -1;
        }
    }

    bool HasRoomToSpawn(int col)
    {
        var list = tilesPerColumn[col];
        float minClearance = tileHeight; // edges may touch but never overlap
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            if (t == null) continue;
            if (spawnY - t.transform.position.y < minClearance)
                return false;
        }
        return true;
    }

    void SpawnTileAt(int col, int durationBeats = 1, bool inverted = false, float spawnYOffset = 0f)
    {
        if (durationBeats < 1) durationBeats = 1;

        // Color set: normal = default tileColor; inverted swaps to alternateTileColor
        // (a slightly lighter dark gray). The outline stays white on both groups so
        // every tile retains the same glowing border against the playfield.
        Color bodyCol    = inverted ? alternateTileColor : tileColor;
        Color outlineCol = tileOutlineColor;

        float tileWidth = columnWidth * tileWidthRatio;
        float visualHeight = tileHeight * durationBeats;

        // Position so every tile's bottom edge spawns at the same Y, regardless of height.
        float bottomY = screenTopY + tileHeight / 2f;
        // spawnYOffset back-dates the tile to where it should be for the exact beat time
        // (see the spawn call), so spacing stays snug regardless of frame timing.
        float centerY = bottomY + visualHeight / 2f + spawnYOffset;

        var go = new GameObject($"Tile_c{col}_d{durationBeats}");
        go.transform.SetParent(transform);
        go.transform.position = new Vector3(ColumnCenterX(col), centerY, 0f);
        // The parent transform has no SpriteRenderer; body/outline live as children.
        // The "full tile bounds" (used by hit detection via tile.visualHeight) are
        // those of the outline child, which exactly matches the tileWidth × visualHeight.

        // Outline: fills the full tile bounds. Two stacked tiles contribute their own
        // outlines at the boundary, doubling up into a visible band.
        var outlineGo = new GameObject("Outline");
        outlineGo.transform.SetParent(go.transform);
        outlineGo.transform.localPosition = Vector3.zero;
        outlineGo.transform.localScale = new Vector3(tileWidth, visualHeight, 1f);

        var outlineSr = outlineGo.AddComponent<SpriteRenderer>();
        outlineSr.sprite = SquareSprite;
        outlineSr.color = outlineCol;
        outlineSr.sortingOrder = 4; // behind the body

        // Body: shrunk inward by tileOutlineThickness on every side so the outline
        // shows as a ring "carved out of" the tile rather than added around the outside.
        float bodyWidth = Mathf.Max(0.01f, tileWidth - 2f * effectiveOutline);
        float bodyHeight = Mathf.Max(0.01f, visualHeight - 2f * effectiveOutline);
        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(go.transform);
        bodyGo.transform.localPosition = Vector3.zero;
        bodyGo.transform.localScale = new Vector3(bodyWidth, bodyHeight, 1f);

        var bodySr = bodyGo.AddComponent<SpriteRenderer>();
        bodySr.sprite = SquareSprite;
        bodySr.color = bodyCol;
        bodySr.sortingOrder = 5; // in front of the outline

        var tile = go.AddComponent<Tile>();
        tile.column = col;
        tile.durationBeats = durationBeats;
        tile.visualHeight = visualHeight;
        tile.spriteRenderer = bodySr;
        tile.outlineRenderer = outlineSr;
        tile.bodyColor = bodyCol;
        tile.outlineColor = outlineCol;

        tilesPerColumn[col].Add(tile);
        totalTileCount++; // one more tile in this song
    }

    void DrawHoldBursts()
    {
        holdBurstRenderers = new SpriteRenderer[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            // Sits exactly on the hit line, between the hit-line bar (sort=2) and
            // the tile body (sort=5). Starts fully transparent — UpdateHoldEffects
            // fades it in/out based on whether this column has an active hold.
            var go = MakeQuad(
                $"HoldBurst_{i}",
                new Vector3(ColumnCenterX(i), hitLineY, 0f),
                columnWidth * 0.92f,
                0.75f,
                new Color(holdEffectColor.r, holdEffectColor.g, holdEffectColor.b, 0f),
                3
            );
            holdBurstRenderers[i] = go.GetComponent<SpriteRenderer>();
        }
    }

    // Called once per Update while a session is active. For every column that
    // currently holds a grabbed-but-not-spent multi-beat tile, pulses a bright
    // core at the hit line and emits sparks that fly outward and fade out.
    void UpdateHoldEffects()
    {
        if (holdBurstRenderers == null) return;

        // 1) Which columns have an active hold right now — and is that hold a
        //    "long" tile (>= longHoldBeatThreshold beats) that earns the extra
        //    dramatic orange-glow + side-particle effect? For long holds, also
        //    record how long the key has been held so the effect can intensify.
        bool[] holdActive = new bool[columnCount];
        bool[] longHoldActive = new bool[columnCount];
        float[] longHoldHeld = new float[columnCount];
        for (int col = 0; col < columnCount; col++)
        {
            var list = tilesPerColumn[col];
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t == null) continue;
                if (IsHoldTile(t) && t.grabbed && !t.spent)
                {
                    holdActive[col] = true;
                    if (t.durationBeats >= longHoldBeatThreshold)
                    {
                        longHoldActive[col] = true;
                        longHoldHeld[col] = Time.time - t.grabTime;
                    }
                    break;
                }
            }
        }

        // 2) Pulse the per-column core box at the hit line. A quick sine wave makes
        //    it "breathe" so it doesn't look like a static highlight. Long holds
        //    glow the same ORANGE as their tile + side particles; normal holds
        //    keep the yellow holdEffectColor.
        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 14f);
        for (int col = 0; col < columnCount; col++)
        {
            if (holdBurstRenderers[col] == null) continue;
            Color baseCol = longHoldActive[col] ? longHoldGlowColor : holdEffectColor;
            Color c = baseCol;
            c.a = holdActive[col] ? Mathf.Clamp01(baseCol.a * pulse) : 0f;
            holdBurstRenderers[col].color = c;
        }

        // 3) Emit fresh sparks for any column that's been active long enough since
        //    the last emission. Per-column timestamps keep the stream uniform even
        //    when multiple columns are held simultaneously.
        for (int col = 0; col < columnCount; col++)
        {
            if (!holdActive[col]) continue;
            if (Time.time < holdNextSparkTime[col]) continue;
            SpawnHoldSpark(col);
            holdNextSparkTime[col] = Time.time + holdSparkInterval;
        }

        // 3b) Long-hold drama: thin particle streams shooting out the LEFT and
        //     RIGHT sides of the tile at the hit line. The stream intensifies the
        //     longer the note is held — emission gets denser (shorter interval)
        //     and each burst spits more, faster particles.
        for (int col = 0; col < columnCount; col++)
        {
            if (!longHoldActive[col]) continue;
            float intensity = Mathf.Clamp01(longHoldHeld[col] / Mathf.Max(0.01f, longHoldRampSeconds));
            // Denser cadence as intensity climbs (sparser early, machine-gun late).
            float streamInterval = Mathf.Lerp(holdSparkInterval * 0.6f, holdSparkInterval * 0.15f, intensity);
            if (Time.time < holdNextStreamTime[col]) continue;
            SpawnLongHoldStream(col, intensity);
            holdNextStreamTime[col] = Time.time + streamInterval;
        }

        // 4) Step every live spark: move it, fade it, kill it when its lifetime expires.
        for (int i = holdSparks.Count - 1; i >= 0; i--)
        {
            var s = holdSparks[i];
            if (s == null || s.go == null) { holdSparks.RemoveAt(i); continue; }
            float age = Time.time - s.spawnTime;
            if (age >= s.lifetime)
            {
                Destroy(s.go);
                holdSparks.RemoveAt(i);
                continue;
            }
            s.go.transform.position += (Vector3)(s.velocity * Time.deltaTime);
            float frac = 1f - age / s.lifetime;
            var col = s.color;
            col.a = s.color.a * frac;
            s.sr.color = col;
        }
    }

    // "Hold tile" = the player has to keep the key down while it passes under the
    // hit line. Anything with durationBeats > 1 qualifies naturally; tall single-
    // beat tiles (tileHeightScale cranked up) also qualify once their visualHeight
    // exceeds holdTileMinHeight, so the player can't just tap them out from under
    // themselves before the tile has visually crossed.
    bool IsHoldTile(Tile t) => t.durationBeats > 1 || t.visualHeight > holdTileMinHeight;

    // Trim a caught multi-beat tile so the portion below the hit line is hidden.
    // Both outline and body are clipped to [naturalTop, hitLineY]; the body still
    // keeps its top/lateral inset, but is flush with the outline at the cut edge
    // (no bottom border on the trimmed side, matching mobile Piano Tiles).
    void ApplyHoldClip(Tile t)
    {
        float parentY = t.transform.position.y;
        float naturalTop    = parentY + t.visualHeight / 2f;
        float naturalBottom = parentY - t.visualHeight / 2f;
        if (naturalBottom > hitLineY) return; // tile hasn't reached the hit line yet

        if (t.outlineRenderer != null)
            SetChildVertical(t.outlineRenderer.transform, parentY, naturalTop, hitLineY);

        if (t.spriteRenderer != null)
        {
            float bodyTop = naturalTop - effectiveOutline;
            if (bodyTop > hitLineY)
                SetChildVertical(t.spriteRenderer.transform, parentY, bodyTop, hitLineY);
            else
                // Visible region is thinner than the outline border — body collapses.
                SetChildVertical(t.spriteRenderer.transform, parentY, hitLineY, hitLineY);
        }
    }

    static void SetChildVertical(Transform child, float parentWorldY, float topWorldY, float bottomWorldY)
    {
        // localScale.y is in world units here because the parent has no scale.
        float h = Mathf.Max(0.0001f, topWorldY - bottomWorldY);
        var s = child.localScale;
        s.y = h;
        child.localScale = s;
        var p = child.localPosition;
        p.y = (topWorldY + bottomWorldY) * 0.5f - parentWorldY;
        child.localPosition = p;
    }

    void SpawnHoldSpark(int col)
    {
        // Pick a random outward direction; speed gets a small jitter so the sparks
        // don't all fly at the same rate. Lifetime jitter prevents synchronized fade.
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float speed = holdSparkSpeed * Random.Range(0.7f, 1.3f);
        float size = Random.Range(0.10f, 0.18f);

        var go = MakeQuad(
            "HoldSpark",
            new Vector3(ColumnCenterX(col), hitLineY, 0f),
            size, size,
            holdEffectColor,
            6 // above tiles so sparks read clearly
        );

        var spark = new HoldSpark
        {
            go = go,
            sr = go.GetComponent<SpriteRenderer>(),
            velocity = new Vector2(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed),
            spawnTime = Time.time,
            lifetime = holdSparkLifetime * Random.Range(0.7f, 1.3f),
            color = holdEffectColor,
        };
        holdSparks.Add(spark);
    }

    // Long-hold drama: thin orange streaks shooting out of the tile's LEFT and
    // RIGHT sides at the hit line, in a narrow cone around the horizontal. The
    // `intensity` (0..1, rising with how long the note is held) scales how MANY
    // particles spit per burst and how fast they fly — so the jets visibly
    // ramp up the longer you hold. Shares the holdSparks pool, so the existing
    // step-4 move/fade/destroy logic handles them; they're drawn orange via the
    // shared color in step 4 below being replaced per-spark here.
    void SpawnLongHoldStream(int col, float intensity)
    {
        const float coneHalfAngle = 0.28f; // radians (~16°) of spread around the base angle
        // Base angle is tilted UP off the horizontal so the jets arc high rather
        // than spraying flat sideways — right side fires up-and-right, left side
        // up-and-left. (~52° above horizontal.)
        const float upTilt = 0.9f; // radians
        float tileWidth = columnWidth * tileWidthRatio;
        float sideX = tileWidth / 2f;
        // 1 particle per side early → 3 per side at full intensity.
        int perSide = 1 + Mathf.FloorToInt(intensity * 2f + 0.001f);

        for (int side = 0; side < 2; side++)
        {
            // side 0 = right, side 1 = left. Origin is the tile's edge.
            float dirSign = (side == 0) ? 1f : -1f;
            float originX = ColumnCenterX(col) + dirSign * sideX;

            for (int n = 0; n < perSide; n++)
            {
                // Right base = upTilt above 0°; left base = upTilt above 180°.
                float baseAngle = (side == 0) ? upTilt : (Mathf.PI - upTilt);
                float angle = baseAngle + Random.Range(-coneHalfAngle, coneHalfAngle);
                // Faster as intensity climbs, plus per-particle jitter.
                float speed = holdSparkSpeed * Mathf.Lerp(1.1f, 2.1f, intensity) * Random.Range(0.85f, 1.2f);

                // Thin and elongated, taller than wide so the streak aligns with
                // its now mostly-upward motion.
                float w = Random.Range(0.035f, 0.06f);
                float hgt = Random.Range(0.22f, 0.42f);
                // Slight vertical jitter on the origin so the jet has a little height.
                float oy = Random.Range(-0.12f, 0.12f);

                var go = MakeQuad(
                    "HoldStream",
                    new Vector3(originX, hitLineY + oy, 0f),
                    w, hgt,
                    longHoldGlowColor,
                    6 // above tiles so the stream reads clearly
                );

                var spark = new HoldSpark
                {
                    go = go,
                    sr = go.GetComponent<SpriteRenderer>(),
                    velocity = new Vector2(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed),
                    spawnTime = Time.time,
                    lifetime = holdSparkLifetime * Random.Range(0.5f, 0.8f),
                    color = longHoldGlowColor,
                };
                holdSparks.Add(spark);
            }
        }
    }

    void DrawMissCounter()
    {
        var go = new GameObject("MissCounter");
        go.transform.SetParent(transform);

        missCounterText = go.AddComponent<TextMeshPro>();
        if (playfieldFont != null) missCounterText.font = playfieldFont;
        missCounterText.text = "Misses: 0";
        missCounterText.fontSize = 5f;
        // Muted red — matches the dead screen's title so the fail-feedback hue
        // is consistent, and sits more comfortably over song backgrounds than
        // pure RGB red.
        missCounterText.color = new Color(0.95f, 0.38f, 0.38f);
        missCounterText.fontStyle = FontStyles.Bold;
        missCounterText.alignment = TextAlignmentOptions.TopRight;
        missCounterText.sortingOrder = 20;

        var rt = missCounterText.rectTransform;
        rt.sizeDelta = new Vector2(6f, 1.5f);
        float margin = 0.3f;
        rt.position = new Vector3(
            screenRightX - margin - rt.sizeDelta.x / 2f,
            screenTopY  - margin - rt.sizeDelta.y / 2f,
            0f
        );
    }

    // Marks a tile's catch as secured (a successful hit) and counts it exactly once,
    // no matter how many code paths confirm the same tile. Used everywhere a catch
    // becomes final so the hit tally stays in lockstep with catchSecured.
    void SecureCatch(Tile t)
    {
        if (t == null || t.catchSecured) return;
        t.catchSecured = true;
        hitCount++;
    }

    // Visual for a missed tile: a soft muted-red flash that fades to nothing as the tile
    // falls from where it was missed down off the bottom of the screen. Deliberately
    // understated — it cues "that one's gone" without pulling focus from live tiles.
    void ApplyMissDissolve(Tile t, float top, float threshold)
    {
        // Alpha hits 0 right as the tile's top reaches the bottom of the screen.
        float a = Mathf.Clamp01((top - screenBottomY) / Mathf.Max(0.01f, threshold - screenBottomY));
        // Ease the red tint in over a short window so it's a gentle flash, not a pop.
        float redIn = Mathf.Clamp01((Time.time - t.dissolveStart) / 0.12f);
        if (t.spriteRenderer != null)
        {
            Color c = Color.Lerp(t.bodyColor, missDissolveColor, redIn);
            c.a = a;
            t.spriteRenderer.color = c;
        }
        if (t.outlineRenderer != null)
        {
            var oc = t.outlineColor;
            oc.a = t.outlineColor.a * a;
            t.outlineRenderer.color = oc;
        }
    }

    // Per-frame fade of the wrong-press flash: each column's hit-zone box (the one
    // showing the key's letter) eases from wrongPressFlashColor back to its normal
    // hitZoneColor over wrongPressFlashDuration after a wrong-time press on that key.
    void UpdateWrongPressFlashes()
    {
        if (hitZoneRenderers == null || wrongPressFlashTime == null) return;
        for (int col = 0; col < columnCount; col++)
        {
            if (hitZoneRenderers[col] == null) continue;
            float since = Time.time - wrongPressFlashTime[col];
            float f = wrongPressFlashDuration > 0.0001f ? Mathf.Clamp01(1f - since / wrongPressFlashDuration) : 0f;
            hitZoneRenderers[col].color = (f > 0f) ? Color.Lerp(hitZoneColor, wrongPressFlashColor, f) : hitZoneColor;
        }
    }

    void RegisterMiss(int column)
    {
        missCount++;
        // First half of the columns is the left hand (a s d f + left SPACE = g),
        // second half is the right hand (h = right SPACE, j k l ;). For SPACE
        // empty-presses we pass the first SPACE-bound column we encountered (4),
        // which keeps SPACE ambiguities consistently attributed to the left hand.
        if (column < columnCount / 2) leftHandMissCount++;
        else                          rightHandMissCount++;

        // In-game counter still only displays the running total.
        if (missCounterText != null)
            missCounterText.text = "Misses: " + missCount;
    }
}