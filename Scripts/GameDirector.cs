using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal; // for Light2D — needs the URP package

public class GameDirector : MonoBehaviour
{
    [Header("Scene references")]
    [Tooltip("Empty parent GameObject containing the overworld player and piano sprites.")]
    public GameObject overworldRoot;
    [Tooltip("The PianoTilesManager component in the scene. Its GameObject is toggled active when entering/exiting the minigame.")]
    public PianoTilesManager pianoTiles;
    public OverworldPlayer player;
    [Tooltip("Every interactable piano in the scene. Each one launches the minigame with its own SongConfig.")]
    public Piano[] pianos;

    [Tooltip("Optional. The one special piano that runs the guided tutorial " +
             "(dialogue boxes interleaved with four charts) instead of the normal " +
             "speed prompt.")]
    public TutorialPiano tutorialPiano;
    [Tooltip("Every readable item in the scene. Each one shows its description text at the bottom of the screen when interacted with.")]
    public ReadableItem[] readableItems;
    [Tooltip("Every NPC in the scene the player can talk to.")]
    public Npc[] npcs;
    [Tooltip("Defaults to Camera.main if left empty.")]
    public Camera mainCamera;
    [Tooltip("Optional. The scene's Global Light 2D. While the piano minigame is " +
             "active, this is forced to white at intensity 1 so the playfield " +
             "always renders at full brightness regardless of how dim/cinematic " +
             "the overworld lighting is set. The original intensity and color are " +
             "snapshotted on entry and restored on exit, so your dim cinematic " +
             "ambient comes back exactly as it was. Leave empty to disable.")]
    public Light2D globalLight2D;

    [Header("Camera Regions")]
    [Tooltip("Optional. Rectangular regions that confine the camera so it never " +
             "shows blank space outside your level. While the player is inside a " +
             "region the camera clamps to its edges; walking into an adjacent region " +
             "hands off to that one; outside every region the camera follows freely. " +
             "Leave empty for plain follow everywhere.")]
    public CameraRegion[] cameraRegions;
    [Tooltip("How gently the camera eases into a region edge as it approaches one. " +
             "The camera otherwise follows the player instantly — in the open AND the " +
             "instant you turn back away from an edge. Higher = the slow-down starts " +
             "further from the edge; 0 = no slow-down (hard stop at the edge).")]
    [Range(0f, 1f)] public float cameraSmoothTime = 0.2f;
    [Tooltip("How fast the camera catches up when you cross into a new region, as a " +
             "multiple of the player's walk speed. The camera always moves faster than " +
             "the player, so normal following stays glued — this only eases the larger " +
             "re-framing pan at a region boundary. Higher = snappier transition; lower " +
             "= a slower, smoother glide.")]
    [Range(1.5f, 12f)] public float cameraCatchUpSpeed = 5f;

    [Header("Background Music")]
    [Tooltip("Looping music for the overworld. Optional — leave empty for silence.")]
    public AudioClip backgroundMusic;
    [Range(0f, 1f)] public float backgroundMusicVolume = 0.6f;
    [Tooltip("Seconds to fade the background music out when entering a piano.")]
    public float bgmFadeOutDuration = 0.4f;
    [Tooltip("Seconds to fade the background music back in when returning to the overworld.")]
    public float bgmFadeInDuration = 0.8f;
    [Tooltip("Seconds to fade the minigame song out when you press exit (the Menu/Back key) on the " +
             "SONG COMPLETE screen. The song otherwise keeps playing on that screen " +
             "until it ends on its own.")]
    public float songCompleteExitFade = 0.4f;

    [Header("UI")]
    [Tooltip("Optional. Font for ALL menu / prompt / overlay text — Settings, Key " +
             "Binds, Audio, the practice & section prompts, the death/finish screens, " +
             "and readable-item bubbles. NPC dialogue is NOT affected (it uses each " +
             "NPC's own dialogueFont). This is a legacy Font, since the menus are " +
             "IMGUI; leave empty for Unity's built-in UI font.")]
    public Font menuFont;

    // Settings: top-level menu with "Key Binds" / "Audio" rows.
    // KeyBinds:  scroll through column slots (9 selectable, since the two SPACE
    //            columns 4/5 are treated as one selection) and rebind any of them.
    // AudioSettings: choose "Bluetooth" / "No Bluetooth" — toggles the global
    //            audio offset between -0.200 s and 0 s.
    enum Mode { Overworld, PromptingSpeed, PromptingSection, Minigame, Dead, Finished, TutorialDialogue, Settings, KeyBinds, OverworldKeyBinds, AudioSettings }
    Mode mode;
    float speedSelection = 1f;                          // currently-selected music-speed in the prompt
    float tileSpeedSelection = 1f;                       // currently-selected tile-fall-speed (= tile height scale) in the prompt
    float audioOffsetSelection = 0f;                     // currently-selected global audio-output offset in the prompt (seconds; negative = music earlier)
    int speedPromptField = 0;                            // 0 = music speed, 1 = tile fall speed, 2 = audio offset

    // Player-set audio-output latency compensation. Persisted across sessions in
    // PlayerPrefs so swapping headphones once-and-for-all "sticks" — typical use
    // is around -0.150 s to -0.200 s while on Bluetooth, 0 on laptop speakers.
    const string AudioOffsetPrefsKey = "BalladeNo5_AudioOffset";
    // Bluetooth toggle from the Audio settings menu. 0 = No Bluetooth (offset
    // forced to 0), 1 = Bluetooth (offset forced to -0.200 s).
    const string BluetoothPrefsKey = "BalladeNo5_Bluetooth";
    const float BluetoothAudioOffset = -0.200f;
    // Per-column key-bind PlayerPrefs keys: "BalladeNo5_KeyBind_0" .. "_9".
    const string KeyBindPrefsKeyPrefix = "BalladeNo5_KeyBind_";

    // ESC-menu navigation state.
    int settingsSelection = 0;      // 0 = "Minigame Keybinds", 1 = "Overworld Keybinds", 2 = "Audio"
    int keyBindSelection = 0;       // index of the highlighted slot in whichever keybind menu is open
    bool capturingRebind = false;   // true after pressing ENTER on a slot — the next key pressed becomes its bind
    int audioSelection = 0;         // 0 = "Bluetooth", 1 = "No Bluetooth"
    KeyCode[] keyBinds;             // length 10 — runtime minigame column key-binds
    KeyCode[] overworldKeyBinds;    // length 6 — Forward, Backward, Right, Left, Sprint, Interact

    // Default minigame layout — used when no PlayerPrefs entries exist or the
    // saved entries are corrupt. Matches the original hard-coded layout in
    // PianoTilesManager so first-launch behavior is unchanged.
    static readonly KeyCode[] DefaultKeyBinds = new[]
    {
        KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F,
        KeyCode.Space, KeyCode.Space,
        KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.Semicolon
    };

    // Overworld action order: 0 Forward, 1 Backward, 2 Right, 3 Left, 4 Sprint, 5 Interact, 6 Menu/Back.
    static readonly KeyCode[] DefaultOverworldKeyBinds = new[]
    {
        KeyCode.W, KeyCode.S, KeyCode.D, KeyCode.A, KeyCode.LeftShift, KeyCode.Space, KeyCode.B
    };
    static readonly string[] OverworldActionLabels =
    {
        "Move Forward", "Move Backward", "Move Right", "Move Left", "Sprint", "Interact", "Menu / Back"
    };
    const string OWKeyBindPrefsKeyPrefix = "BalladeNo5_OWKeyBind_";

    // The KeyBinds menu now has one slot per column (10 total), so the two
    // space-bar columns (4 and 5) are independently rebindable. Slot index maps
    // directly to column index.
    Piano pendingPiano;                                  // piano queued while the player is choosing speed/section
    PianoTilesManager.Section[] sectionOptions;          // populated when entering PromptingSection
    int sectionSelection;                                // index into sectionOptions
    SpriteRenderer playerSprite;
    Collider2D playerCollider;
    int currentMissLimit = 10;     // captured from the active song's SongConfig
    SongConfig activeSongConfig;   // the active session's config, so R can restart it (a piano OR a tutorial chart)
    Piano activePiano;             // the piano whose song is currently playing (null for the tutorial)
    // Fewest mistakes the player has scored on each piano this run. In-memory only —
    // resets when Play mode is stopped/started in the editor; a piano absent from the
    // map has never been played (shown as "N/A").
    readonly System.Collections.Generic.Dictionary<Piano, int> pianoBestMistakes =
        new System.Collections.Generic.Dictionary<Piano, int>();

    // ----- Tutorial flow state -----
    bool tutorialActive;           // true while the special Tutorial Piano sequence is running
    int tutorialIndex;             // index into tutorialEntries (dialogue / song steps, in order)
    string tutorialDialogueText = "";
    System.Collections.Generic.List<TutorialEntry> tutorialEntries;
    struct TutorialEntry { public bool isDialogue; public string text; public int songIndex; }
    int finalMissCount;        // captured before EndSession so the overlay can display it
    int finalLeftMissCount;    // misses attributed to the left hand (asdfg)
    int finalRightMissCount;   // misses attributed to the right hand (hjkl;)
    int finalHitCount;         // tiles successfully hit (shown on the SONG COMPLETE screen)
    int finalTotalTiles;       // total tiles in the song (denominator of the hit ratio)
    string currentDescription = ""; // non-empty while a ReadableItem's text bubble is showing
    int currentFontSize = 20;       // font size for the active description (from the item)
    ReadableItem currentReadable;   // item that opened the bubble — when player leaves its bounds, the bubble auto-clears
    Npc currentNpc;                 // NPC currently mid-conversation
    Color currentNpcBoxColor = Color.black; // dialog-box background = top-right pixel of the NPC's portrait
    Color currentNpcTextColor = Color.black; // NPC dialogue text is always black (never white)

    // Tree-based dialogue runtime: a call stack of (sequence, index) frames.
    // The top of the stack is the sequence we're currently iterating. When a
    // choice is taken, a new frame for the chosen branch is pushed; when that
    // frame's index reaches the end, it pops back to the parent sequence and
    // resumes from the element AFTER the choice. currentDialogueElement is the
    // element we're displaying right now (a speaker line OR a choice).
    class DialogueFrame { public DialogueElement[] sequence; public int index; }
    System.Collections.Generic.Stack<DialogueFrame> dialogueStack;
    DialogueElement currentDialogueElement;
    // Snapshotted Global Light 2D state from before the minigame started, so
    // EnterOverworld can put the cinematic lighting back exactly as it was. The
    // "saved" flag prevents the initial-scene EnterOverworld() call from
    // restoring whatever the default Color/float values are over the user's
    // Inspector-configured starting intensity.
    bool globalLightStateSaved = false;
    float savedGlobalLightIntensity = 1f;
    Color savedGlobalLightColor = Color.white;
    int choiceSelection;            // 0 = A, 1 = B (only meaningful while currentDialogueElement is a choice)

    AudioSource bgmSource;
    Coroutine bgmFadeCoroutine;

    // Exposed so other systems (e.g. Door) can tell when an NPC conversation is in
    // progress and avoid stealing the SPACE press meant for advancing dialogue.
    public static GameDirector Instance { get; private set; }
    public bool InNpcDialogue => currentNpc != null;

    /// <summary>The configurable overworld "Interact" key (default Space). Read live by
    /// the overworld interaction handler and by Doors so a rebind takes effect at once.</summary>
    public KeyCode InteractKey =>
        (overworldKeyBinds != null && overworldKeyBinds.Length > 5) ? overworldKeyBinds[5] : KeyCode.Space;

    /// <summary>The configurable "Menu / Back" key (default B). Replaces Escape everywhere —
    /// opens the settings menu, backs out of menus, exits the minigame, cancels rebinds, etc.
    /// The real Escape key is intentionally left doing nothing (it exits fullscreen on the web).</summary>
    public KeyCode MenuKey =>
        (overworldKeyBinds != null && overworldKeyBinds.Length > 6) ? overworldKeyBinds[6] : KeyCode.B;
    string MenuKeyLabel => PianoTilesManager.KeyCodeToColumnLabel(MenuKey);

    void Start()
    {
        Instance = this;
        if (mainCamera == null) mainCamera = Camera.main;
        if (player != null)
        {
            playerSprite = player.GetComponent<SpriteRenderer>();
            playerCollider = player.GetComponent<Collider2D>();
        }
        // Load the saved audio-output offset (e.g. -0.200 for Bluetooth) and
        // push it to the manager up-front, so even the very first BeginSession
        // honors the player's saved compensation.
        audioOffsetSelection = PlayerPrefs.GetFloat(AudioOffsetPrefsKey, 0f);
        if (pianoTiles != null) pianoTiles.globalAudioOffset = audioOffsetSelection;

        // Load the saved key binds (per-column KeyCodes) and push them too. With
        // no saved entries (fresh install) the defaults are used.
        keyBinds = LoadBinds(KeyBindPrefsKeyPrefix, DefaultKeyBinds);
        if (pianoTiles != null) pianoTiles.customColumnKeys = (KeyCode[])keyBinds.Clone();

        // Load + push the overworld binds (movement/sprint/interact). Movement and
        // sprint go to the player; interact is read live via InteractKey.
        overworldKeyBinds = LoadBinds(OWKeyBindPrefsKeyPrefix, DefaultOverworldKeyBinds);
        PushOverworldBinds();

        // Initial selection in the Audio menu reflects the current saved offset
        // — Bluetooth row is highlighted if the offset matches the BT value.
        audioSelection = (Mathf.Abs(audioOffsetSelection - BluetoothAudioOffset) < 0.001f) ? 0 : 1;
        SetupBackgroundMusic();
        EnterOverworld();
    }

    void Update()
    {
        if (mode == Mode.Overworld)
        {
            // (Camera follow moved to LateUpdate so it reads the player's
            // interpolated position AFTER movement is applied this frame —
            // following in Update against an interpolated body causes jitter.)

            // Auto-clear the bubble when the player walks out of an item's bounds.
            TryGetPlayerBounds(out var autoPb);
            if (currentReadable != null && !currentReadable.IsAdjacentTo(autoPb))
            {
                currentDescription = "";
                currentReadable = null;
            }
            // Auto-exit NPC conversation when out of range.
            if (currentNpc != null && !currentNpc.IsAdjacentTo(autoPb))
            {
                EndNpcConversation();
            }

            // ESC behavior in the overworld:
            //   • If an NPC conversation is active → close it.
            //   • Otherwise → open the SETTINGS menu (Key Binds / Audio).
            if (Input.GetKeyDown(MenuKey))
            {
                if (currentNpc != null)
                {
                    EndNpcConversation();
                }
                else
                {
                    EnterSettingsMenu();
                    return; // skip the movement / SPACE chain below this frame
                }
            }

            // Lock the player ONLY while a choice is being shown. Regular dialogue
            // lines (NPC talking) leave movement free. (Done BEFORE the arrow/space
            // chain below so we don't break the if/else-if linkage between them.)
            if (player != null)
                player.movementLocked = (currentNpc != null && currentDialogueElement != null && currentDialogueElement.choice != null);

            // Arrow keys OR W/S cycle the choice selection while a choice is being shown.
            // Movement is locked above so W/S don't also move the character. The
            // choice may have any number of options (2+, depending on how many
            // unique 'P:' lines diverge at this point in the text-file conversation
            // tree), so we wrap modulo the option count.
            //
            // The arrow-key branch is part of an if/else-if chain with the SPACE
            // handler below — so the outer condition must INCLUDE the key check
            // (not just "are we at a choice"), otherwise pressing SPACE while at a
            // choice enters this empty arm and the `else if (Space)` below is
            // skipped, making SPACE a no-op.
            if (currentNpc != null && currentDialogueElement != null
                && currentDialogueElement.choice != null
                && currentDialogueElement.choice.options != null
                && currentDialogueElement.choice.options.Length > 0
                && (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow)
                    || Input.GetKeyDown(KeyCode.W)       || Input.GetKeyDown(KeyCode.S)))
            {
                int n = currentDialogueElement.choice.options.Length;
                if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
                    choiceSelection = (choiceSelection - 1 + n) % n;
                else
                    choiceSelection = (choiceSelection + 1) % n; // must be Down / S
            }

            // SPACE handling (chained as else-if so an arrow press in the same
            // frame doesn't ALSO advance the dialogue). Priority:
            //   1) advance NPC dialogue (if one is open) — may confirm a choice, advance a branch, or advance the main thread
            //   2) start piano minigame
            //   3) dismiss readable text
            //   4) start NPC conversation
            //   5) open readable text
            else if (Input.GetKeyDown(InteractKey))
            {
                if (currentNpc != null)
                {
                    AdvanceNpcDialogue();
                }
                else if (tutorialPiano != null && TryGetPlayerBounds(out var tpb) && tutorialPiano.IsAdjacentTo(tpb))
                {
                    currentDescription = "";
                    currentReadable = null;
                    EndNpcConversation();
                    StartTutorial();
                }
                else
                {
                    Piano targetPiano = FindAdjacentPiano();
                    if (targetPiano != null)
                    {
                        currentDescription = "";
                        currentReadable = null;
                        EndNpcConversation();
                        EnterSpeedPrompt(targetPiano);
                    }
                    else if (!string.IsNullOrEmpty(currentDescription))
                    {
                        currentDescription = "";
                        currentReadable = null;
                    }
                    else
                    {
                        Npc npc = FindAdjacentNpc();
                        if (npc != null)
                        {
                            var tree = npc.BuildDialogueTree();
                            if (tree != null && tree.Length > 0)
                            {
                                currentNpc = npc;
                                dialogueStack = new System.Collections.Generic.Stack<DialogueFrame>();
                                dialogueStack.Push(new DialogueFrame { sequence = tree, index = 0 });
                                choiceSelection = 0;
                                AdvanceDialogueElement();
                                CacheNpcBoxColors(npc);
                            }
                        }
                        else
                        {
                            ReadableItem item = FindAdjacentReadable();
                            if (item != null)
                            {
                                currentDescription = item.description;
                                currentFontSize = item.fontSize;
                                currentReadable = item;
                            }
                        }
                    }
                }
            }
        }
        else if (mode == Mode.PromptingSpeed)
        {
            // Three rows: 0 = music speed, 1 = tile fall speed, 2 = audio offset.
            // ↑/↓ or W/S moves between rows (wraps); ←/→ or A/D adjusts the
            // selected value within its own clamp range and step size.
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
                speedPromptField = (speedPromptField + 2) % 3; // wrap upward (0→2→1→0)
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
                speedPromptField = (speedPromptField + 1) % 3;

            int delta = 0;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) delta = +1;
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) delta = -1;

            if (delta != 0)
            {
                if (speedPromptField == 0)
                {
                    speedSelection = Mathf.Round((speedSelection + 0.1f * delta) * 10f) / 10f;
                    speedSelection = Mathf.Clamp(speedSelection, 0.1f, 1.0f);
                }
                else if (speedPromptField == 1)
                {
                    // Below 1.0× steps by 0.1; above 1.0× steps by 0.5 (see earlier comment).
                    float step = (delta > 0)
                        ? (tileSpeedSelection < 1.0f ? 0.1f : 0.5f)
                        : (tileSpeedSelection > 1.0f ? 0.5f : 0.1f);
                    tileSpeedSelection = Mathf.Round((tileSpeedSelection + step * delta) * 10f) / 10f;
                    tileSpeedSelection = Mathf.Clamp(tileSpeedSelection, 0.5f, 3.0f);
                }
                else // speedPromptField == 2 — audio offset, in 10 ms steps over ±500 ms.
                {
                    audioOffsetSelection = Mathf.Round((audioOffsetSelection + 0.01f * delta) * 100f) / 100f;
                    audioOffsetSelection = Mathf.Clamp(audioOffsetSelection, -0.5f, 0.5f);
                }
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (pianoTiles != null)
                {
                    pianoTiles.speedMultiplier = speedSelection;
                    pianoTiles.tileHeightScale = tileSpeedSelection;
                    pianoTiles.globalAudioOffset = audioOffsetSelection;
                }
                // Persist the audio offset across sessions — it's the only one of
                // the three that's tied to the player's hardware setup, not the
                // song they're about to play.
                PlayerPrefs.SetFloat(AudioOffsetPrefsKey, audioOffsetSelection);
                PlayerPrefs.Save();
                ContinueAfterSpeedConfirm();
            }
            else if (Input.GetKeyDown(MenuKey))
            {
                pendingPiano = null;
                if (player != null) player.movementLocked = false;
                mode = Mode.Overworld;
            }
        }
        else if (mode == Mode.PromptingSection)
        {
            if (sectionOptions != null && sectionOptions.Length > 0)
            {
                if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
                    sectionSelection = (sectionSelection - 1 + sectionOptions.Length) % sectionOptions.Length;
                else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
                    sectionSelection = (sectionSelection + 1) % sectionOptions.Length;
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                int startBeat = (sectionOptions != null && sectionOptions.Length > 0)
                    ? sectionOptions[sectionSelection].beatIndex
                    : 0;
                LaunchMinigameAt(startBeat);
            }
            else if (Input.GetKeyDown(MenuKey))
            {
                pendingPiano = null;
                sectionOptions = null;
                if (player != null) player.movementLocked = false;
                mode = Mode.Overworld;
            }
        }
        else if (mode == Mode.Minigame)
        {
            bool died = pianoTiles != null && pianoTiles.MissCount > currentMissLimit;
            if (died) EnterDeadState();
            else if (pianoTiles != null && pianoTiles.Completed) EnterFinishedState();
            else if (Input.GetKeyDown(MenuKey)) EnterOverworld();
            else if (Input.GetKeyDown(KeyCode.R)) RestartMinigame();
        }
        else if (mode == Mode.Dead)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                RestartActiveMinigame();
            else if (Input.GetKeyDown(MenuKey)) EnterOverworld();
        }
        else if (mode == Mode.Finished)
        {
            if (!finishedExiting) // ignore input while the exit fade is playing out
            {
                if (Input.GetKeyDown(KeyCode.R))
                {
                    RestartActiveMinigame();
                }
                // In the tutorial, SPACE on a non-final SONG COMPLETE advances to the
                // next dialogue/song instead of exiting.
                else if (tutorialActive && HasNextTutorialEntry() && Input.GetKeyDown(KeyCode.Space))
                {
                    AdvanceTutorialEntry();
                }
                else if (Input.GetKeyDown(MenuKey))
                {
                    finishedExiting = true;
                    StartCoroutine(ExitFinishedWithFade());
                }
            }
        }
        else if (mode == Mode.TutorialDialogue)
        {
            // Advance on SPACE (or the interact key / Enter); ESC leaves the tutorial.
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(InteractKey)
                || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                AdvanceTutorialEntry();
            else if (Input.GetKeyDown(MenuKey))
                EnterOverworld();
        }
        else if (mode == Mode.Settings)
        {
            // Three rows on the left. ↑/↓ or W/S navigate; SPACE / ENTER enters the
            // selected sub-menu; ESC returns to the overworld. The piano records list
            // on the right is a read-only side panel — not part of the navigation.
            const int rowCount = 3; // Minigame Keybinds / Overworld Keybinds / Audio
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
                settingsSelection = (settingsSelection - 1 + rowCount) % rowCount;
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
                settingsSelection = (settingsSelection + 1) % rowCount;
            else if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (settingsSelection == 0)      EnterKeyBindsMenu();
                else if (settingsSelection == 1) EnterOverworldKeyBindsMenu();
                else                             EnterAudioMenu();
            }
            else if (Input.GetKeyDown(MenuKey)) EnterOverworld();
        }
        else if (mode == Mode.KeyBinds || mode == Mode.OverworldKeyBinds)
        {
            // Shared interaction for both keybind screens:
            //   • Not capturing: W/S or ↑/↓ (and ←/→ for the horizontal minigame
            //     row) scroll the highlighted slot. ENTER starts a rebind of it.
            //     ESC returns to the Settings menu.
            //   • Capturing (after ENTER): the next bindable key pressed becomes the
            //     slot's bind (saved + pushed live). ESC cancels without changing.
            int slotCount = (mode == Mode.KeyBinds) ? keyBinds.Length : overworldKeyBinds.Length;

            if (capturingRebind)
            {
                if (Input.GetKeyDown(MenuKey))
                {
                    capturingRebind = false;
                }
                else
                {
                    KeyCode pressed = CaptureFirstBindableKey();
                    if (pressed != KeyCode.None)
                    {
                        RebindCurrentSlot(pressed);
                        capturingRebind = false;
                    }
                }
            }
            else
            {
                bool prev = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)   || Input.GetKeyDown(KeyCode.LeftArrow);
                bool next = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.RightArrow);
                if (prev)      keyBindSelection = (keyBindSelection - 1 + slotCount) % slotCount;
                else if (next) keyBindSelection = (keyBindSelection + 1) % slotCount;
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) capturingRebind = true;
                else if (Input.GetKeyDown(MenuKey)) EnterSettingsMenu();
            }
        }
        else if (mode == Mode.AudioSettings)
        {
            // Two rows: Bluetooth / No Bluetooth. ↑/↓ navigate; SPACE confirms
            // and persists; ESC returns to the Settings menu without changing.
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
                audioSelection = (audioSelection + 1) % 2;
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
                audioSelection = (audioSelection + 1) % 2;
            else if (Input.GetKeyDown(KeyCode.Space))
            {
                ApplyAudioSelection(audioSelection);
                EnterSettingsMenu();
            }
            else if (Input.GetKeyDown(MenuKey)) EnterSettingsMenu();
        }
    }

    // Camera-region runtime state.
    CameraRegion currentCameraRegion;   // region currently confining the camera (null = free follow)
    bool[] regionWasInside;             // per-region: did it contain the player last frame (for entry detection)
    bool snapCameraNext;                // snap straight to the clamped target on the next overworld frame

    // Camera follow runs in LateUpdate, after Update (input/animation) AND the
    // physics interpolation for this frame, so it reads the player's smoothed
    // rendered position. Following in Update instead reads a position that only
    // advances on physics ticks → visible stutter while moving.
    void LateUpdate()
    {
        if (mode != Mode.Overworld) return;
        if (mainCamera == null || player == null) return;

        Vector3 cp = mainCamera.transform.position;
        Vector2 pp = player.transform.position;

        // Pick the active region. If the player just STEPPED INTO a different region
        // this frame, hand off to it immediately — so transitioning A→B confines to B
        // even where A and B overlap (B wins because it's the one just entered).
        // Otherwise keep the current region while the player is still inside it (avoids
        // flicker), falling back to whichever region contains them when it no longer does.
        CameraRegion entered = null;
        if (cameraRegions != null)
        {
            if (regionWasInside == null || regionWasInside.Length != cameraRegions.Length)
                regionWasInside = new bool[cameraRegions.Length];
            for (int i = 0; i < cameraRegions.Length; i++)
            {
                bool inside = cameraRegions[i] != null && cameraRegions[i].Contains(pp);
                if (inside && !regionWasInside[i] && cameraRegions[i] != currentCameraRegion)
                    entered = cameraRegions[i];   // newly stepped into this region
                regionWasInside[i] = inside;
            }
        }
        if (entered != null)
            currentCameraRegion = entered;
        else if (currentCameraRegion == null || !currentCameraRegion.Contains(pp))
            currentCameraRegion = FindCameraRegionAt(pp);

        // Cap how fast the camera can move — well above the player's walk speed, so
        // normal following stays glued, but the larger re-framing move when crossing
        // into a new region eases into a smooth glide instead of snapping.
        float maxPanSpeed = (player != null ? player.moveSpeed : 6f) * cameraCatchUpSpeed;

        float newX, newY;
        if (currentCameraRegion == null)
        {
            if (snapCameraNext)
            {
                newX = pp.x;
                newY = pp.y;
            }
            else
            {
                newX = Mathf.MoveTowards(cp.x, pp.x, maxPanSpeed * Time.deltaTime);
                newY = Mathf.MoveTowards(cp.y, pp.y, maxPanSpeed * Time.deltaTime);
            }
        }
        else
        {
            float halfH = mainCamera.orthographicSize;
            float halfW = halfH * mainCamera.aspect;
            Bounds b = currentCameraRegion.WorldBounds;
            float minX = b.min.x + halfW, maxX = b.max.x - halfW;
            float minY = b.min.y + halfH, maxY = b.max.y - halfH;
            if (snapCameraNext)
            {
                // First overworld frame: snap straight to the clamped target.
                newX = (minX <= maxX) ? Mathf.Clamp(pp.x, minX, maxX) : b.center.x;
                newY = (minY <= maxY) ? Mathf.Clamp(pp.y, minY, maxY) : b.center.y;
            }
            else
            {
                newX = ApproachAxis(cp.x, pp.x, minX, maxX, b.center.x, maxPanSpeed);
                newY = ApproachAxis(cp.y, pp.y, minY, maxY, b.center.y, maxPanSpeed);
            }
        }

        snapCameraNext = false;
        mainCamera.transform.position = new Vector3(newX, newY, cp.z);
    }

    // Moves one axis of the camera toward the player, clamped to [min, max]. Follows
    // the player at up to maxSpeed (well above walk speed, so normal following stays
    // glued) and ONLY slows as it approaches the edge it's heading toward — easing into
    // the boundary instead of hard-stopping or lagging on the way back. maxSpeed is
    // also what eases the bigger re-framing move when crossing into a new region.
    // (min > max → the region is smaller than the view on this axis → ease to center.)
    float ApproachAxis(float cur, float playerPos, float min, float max, float center, float maxSpeed)
    {
        if (min > max) return Mathf.MoveTowards(cur, center, maxSpeed * Time.deltaTime);
        float desired = Mathf.Clamp(playerPos, min, max);
        float diff = desired - cur;
        if (Mathf.Abs(diff) < 0.0001f) return desired;

        float dir = Mathf.Sign(diff);
        float wall = (dir > 0f) ? max : min;
        float distToWall = Mathf.Max(0.0001f, (wall - cur) * dir);
        float edgeCap = distToWall / Mathf.Max(0.0001f, cameraSmoothTime);
        // Move capped by the edge-slowdown OR the global pan speed, whichever is slower.
        float cap = Mathf.Min(edgeCap, maxSpeed);
        float step = dir * Mathf.Min(Mathf.Abs(diff), cap * Time.deltaTime);
        return cur + step;
    }

    CameraRegion FindCameraRegionAt(Vector2 point)
    {
        if (cameraRegions == null) return null;
        for (int i = 0; i < cameraRegions.Length; i++)
            if (cameraRegions[i] != null && cameraRegions[i].Contains(point)) return cameraRegions[i];
        return null;
    }

    void EnterOverworld()
    {
        mode = Mode.Overworld;
        tutorialActive = false; // leaving any minigame/tutorial ends the tutorial flow
        // Snap straight to the player on the first overworld frame so we don't drift in
        // from the minigame's camera position.
        snapCameraNext = true;
        if (pianoTiles != null)
        {
            pianoTiles.EndSession();
            pianoTiles.gameObject.SetActive(false);
        }
        if (overworldRoot != null) overworldRoot.SetActive(true);
        // Restore the cinematic Global Light 2D values we snapshotted on entry.
        // The "saved" flag means the initial-scene EnterOverworld call (before
        // any minigame has run) won't clobber the user's Inspector intensity
        // with the default field values.
        if (globalLight2D != null && globalLightStateSaved)
        {
            globalLight2D.intensity = savedGlobalLightIntensity;
            globalLight2D.color = savedGlobalLightColor;
            globalLightStateSaved = false;
        }
        FadeBackgroundMusic(backgroundMusicVolume, bgmFadeInDuration);
        // Snap the room darkness back to whatever it was before the minigame,
        // so we don't see a fade-out/fade-in cycle from the trigger toggling.
        if (RoomDarknessOverlay.Instance != null) RoomDarknessOverlay.Instance.Unfreeze();
    }

    // R during the minigame: end the current session and start it again immediately
    // with the same speed and startBeatIndex (those persist on PianoTilesManager,
    // EndSession only resets per-run state like missCount). Camera, BGM, overworld
    // visibility, and the room darkness freeze all stay where they were.
    // In-minigame restart (R during play): just re-begin the active session.
    void RestartMinigame()
    {
        if (pianoTiles == null || activeSongConfig == null) return;
        pianoTiles.EndSession();
        pianoTiles.BeginSession(activeSongConfig);
    }

    // Restart the active song from the Dead / SONG COMPLETE screens — a full re-entry
    // (presentation + fresh session). Works for a normal piano OR a tutorial chart;
    // the tutorial flag is preserved so SONG COMPLETE still offers "continue".
    void RestartActiveMinigame()
    {
        if (activeSongConfig == null) { EnterOverworld(); return; }
        BeginMinigameSession(activeSongConfig, currentMissLimit, tutorialActive);
    }

    void EnterSpeedPrompt(Piano source)
    {
        mode = Mode.PromptingSpeed;
        pendingPiano = source;
        speedSelection = 1f;
        tileSpeedSelection = 1f;
        // Audio offset is the only field that PERSISTS — pull the player's last
        // saved value (e.g. -0.180 for Bluetooth) so they don't have to re-dial
        // it in every time. Music/tile speeds reset to defaults each run.
        audioOffsetSelection = PlayerPrefs.GetFloat(AudioOffsetPrefsKey, 0f);
        speedPromptField = 0;
        sectionOptions = null;
        sectionSelection = 0;
        // Lock movement so W/S adjusting speed doesn't also move the player.
        if (player != null) player.movementLocked = true;
    }

    // After the speed prompt is confirmed: parse the chart for section markers
    // (comment-only lines). If any are found, show the section prompt; otherwise
    // launch the minigame from the beginning.
    void ContinueAfterSpeedConfirm()
    {
        string chartText = GetPianoChartText(pendingPiano);
        var parsed = PianoTilesManager.ParseChart(chartText);

        // Build the option list: prepend "Beginning" if the first explicit
        // section isn't already at beat 0. Filter out sections that point past
        // the last beat (e.g. a comment at the very end of the chart).
        var opts = new System.Collections.Generic.List<PianoTilesManager.Section>();
        bool needsBeginning = (parsed.sections.Length == 0) || (parsed.sections[0].beatIndex > 0);
        if (needsBeginning)
            opts.Add(new PianoTilesManager.Section { name = "Beginning", beatIndex = 0 });
        for (int i = 0; i < parsed.sections.Length; i++)
        {
            if (parsed.sections[i].beatIndex < parsed.beats.Length)
                opts.Add(parsed.sections[i]);
        }

        if (opts.Count <= 1)
        {
            // No meaningful section choice — just start from beat 0.
            LaunchMinigameAt(0);
            return;
        }

        sectionOptions = opts.ToArray();
        sectionSelection = 0;
        mode = Mode.PromptingSection;
    }

    void LaunchMinigameAt(int startBeatIndex)
    {
        if (pianoTiles != null) pianoTiles.startBeatIndex = startBeatIndex;
        var p = pendingPiano;
        pendingPiano = null;
        sectionOptions = null;
        activePiano = p; // remember which piano this is, to record its score on finish/death
        var cfg = (p != null) ? p.songConfig : null;
        BeginMinigameSession(cfg, (cfg != null) ? cfg.missLimit : 10, tutorial: false);
    }

    static string GetPianoChartText(Piano piano)
    {
        if (piano == null || piano.songConfig == null) return null;
        return piano.songConfig.chartFile != null
            ? piano.songConfig.chartFile.text
            : piano.songConfig.chart;
    }

    // The shared "we're in a piano now" presentation: bright lighting, BGM faded out,
    // overworld hidden, camera centered on the playfield. Used both when starting a
    // song and (once, up front) when entering the tutorial so dialogue and songs share
    // the same backdrop without flicker.
    void EnterMinigamePresentation()
    {
        // Snapshot the Global Light 2D so we can restore the cinematic ambient on exit,
        // then force it bright white so the playfield renders at full brightness. The
        // "saved" flag is only consulted on the EnterOverworld path.
        if (globalLight2D != null)
        {
            if (!globalLightStateSaved)
            {
                savedGlobalLightIntensity = globalLight2D.intensity;
                savedGlobalLightColor = globalLight2D.color;
                globalLightStateSaved = true;
            }
            globalLight2D.intensity = 1f;
            globalLight2D.color = Color.white;
        }
        FadeBackgroundMusic(0f, bgmFadeOutDuration);
        // Freeze the room overlay BEFORE deactivating the overworld so the resulting
        // OnTriggerExit2D on the RoomArea doesn't trigger a fade-out.
        if (RoomDarknessOverlay.Instance != null) RoomDarknessOverlay.Instance.Freeze();
        if (overworldRoot != null) overworldRoot.SetActive(false);
        if (mainCamera != null)
        {
            var cp = mainCamera.transform.position;
            mainCamera.transform.position = new Vector3(0f, 0f, cp.z);
        }
    }

    // Start a minigame session from a SongConfig (a normal piano's, or a tutorial
    // chart's). tutorial flags whether the SONG COMPLETE screen should offer "continue".
    void BeginMinigameSession(SongConfig config, int missLimit, bool tutorial)
    {
        mode = Mode.Minigame;
        if (player != null) player.movementLocked = false; // safety unlock
        activeSongConfig = config;
        currentMissLimit = missLimit;
        tutorialActive = tutorial;
        EnterMinigamePresentation();
        if (pianoTiles != null)
        {
            pianoTiles.gameObject.SetActive(true);
            pianoTiles.BeginSession(config);
        }
    }

    // ----- TUTORIAL PIANO -------------------------------------------------------
    // A scripted sequence of dialogue boxes and four charts. The whole tutorial runs
    // inside the minigame presentation (overworld hidden the entire time); dialogue
    // and songs swap in place. SONG COMPLETE between charts offers R / ESC / SPACE.

    void StartTutorial()
    {
        if (tutorialPiano == null) return;
        BuildTutorialEntries();
        tutorialActive = true;
        tutorialIndex = 0;
        if (player != null) player.movementLocked = false;
        EnterMinigamePresentation();   // set the backdrop once for the whole tutorial
        ProcessTutorialEntry();
    }

    void BuildTutorialEntries()
    {
        tutorialEntries = new System.Collections.Generic.List<TutorialEntry>
        {
            Dlg("Hi, this is the tutorial!"),
            Dlg("Get your left hand situated first. Place your fingers over A, S, D, F, and Space. " +
                "These are the recommended keys, but feel free to change the key binds. " +
                "Then press space to begin the song."),
            Sng(0),
            Dlg("Now let's play two notes at the same time. Left hand only still. " +
                "Press space whenever you're ready."),
            Sng(1),
            Dlg("Now let's add the right hand. Place your fingers over Space, J, K, L, and ; (semicolon). " +
                "Again, these are just the recommended keys. Press space whenever you're ready."),
            Sng(2),
            Dlg("Now let's play both hands at the same time. Press space to experience the full song."),
            Sng(3),
        };
    }

    static TutorialEntry Dlg(string text) => new TutorialEntry { isDialogue = true, text = text };
    static TutorialEntry Sng(int songIndex) => new TutorialEntry { isDialogue = false, songIndex = songIndex };

    bool HasNextTutorialEntry() =>
        tutorialEntries != null && tutorialIndex + 1 < tutorialEntries.Count;

    void AdvanceTutorialEntry()
    {
        tutorialIndex++;
        ProcessTutorialEntry();
    }

    // Show the current step. Past the last step → tutorial done → back to the overworld.
    void ProcessTutorialEntry()
    {
        if (!tutorialActive || tutorialEntries == null || tutorialIndex >= tutorialEntries.Count)
        {
            EnterOverworld();
            return;
        }
        var e = tutorialEntries[tutorialIndex];
        if (e.isDialogue) EnterTutorialDialogue(e.text);
        else BeginTutorialSong(e.songIndex);
    }

    void EnterTutorialDialogue(string text)
    {
        mode = Mode.TutorialDialogue;
        tutorialDialogueText = text;
        // Tear down any running/finished song so it goes silent and the playfield
        // clears behind the dialogue. The presentation backdrop stays in place.
        if (pianoTiles != null && pianoTiles.gameObject.activeSelf)
        {
            pianoTiles.EndSession();
            pianoTiles.gameObject.SetActive(false);
        }
    }

    void BeginTutorialSong(int songIndex)
    {
        if (pianoTiles != null) pianoTiles.startBeatIndex = 0;
        activePiano = null; // the tutorial isn't one of the scored pianos
        var cfg = tutorialPiano.ConfigForChart(songIndex);
        BeginMinigameSession(cfg, tutorialPiano.missLimit, tutorial: true);
    }

    void EnterDeadState()
    {
        mode = Mode.Dead;
        // Snapshot the miss counts before ending the session (EndSession resets them to 0).
        if (pianoTiles != null)
        {
            finalMissCount = pianoTiles.MissCount;
            finalLeftMissCount = pianoTiles.LeftHandMissCount;
            finalRightMissCount = pianoTiles.RightHandMissCount;
            RecordPianoScore(finalMissCount);
            pianoTiles.EndSession();
            pianoTiles.gameObject.SetActive(false);
        }
        // Camera and overworldRoot stay where they were (minigame view, overworld hidden).
        // BGM stays paused — it will fade in only if the player escapes back to the overworld.
    }

    // Reached when a (non-looping) chart runs out of tiles. Mirrors EnterDeadState:
    // snapshot the miss count before ending the session (EndSession resets it), then
    // show the finish overlay. R re-launches the same song; ESC returns to the overworld.
    bool finishedExiting; // true while the SONG COMPLETE exit fade is running (ignore further input)

    void EnterFinishedState()
    {
        mode = Mode.Finished;
        finishedExiting = false;
        if (pianoTiles != null)
        {
            finalMissCount = pianoTiles.MissCount;
            finalLeftMissCount = pianoTiles.LeftHandMissCount;
            finalRightMissCount = pianoTiles.RightHandMissCount;
            finalHitCount = pianoTiles.HitCount;
            finalTotalTiles = pianoTiles.TotalTileCount;
            RecordPianoScore(finalMissCount);
            // Keep the song playing through the SONG COMPLETE screen (until the player
            // exits or the track ends). Gameplay still stops, but leave the GameObject
            // active so the audio keeps sounding.
            pianoTiles.EndSession(keepMusicPlaying: true);
        }
        // Camera and overworldRoot stay where they were (minigame view, overworld hidden).
    }

    // ESC on the SONG COMPLETE screen: fade the still-playing song out slightly, then
    // return to the overworld (EnterOverworld deactivates the minigame, fully stopping
    // the now-silent track).
    System.Collections.IEnumerator ExitFinishedWithFade()
    {
        var src = (pianoTiles != null) ? pianoTiles.MusicSource : null;
        if (src != null && src.isPlaying && songCompleteExitFade > 0.0001f)
        {
            float startVol = src.volume;
            float t = 0f;
            while (t < songCompleteExitFade)
            {
                t += Time.deltaTime;
                src.volume = Mathf.Lerp(startVol, 0f, t / songCompleteExitFade);
                yield return null;
            }
            src.volume = 0f;
        }
        EnterOverworld();
    }

    // ----- ESC SETTINGS MENU -----------------------------------------------

    void EnterSettingsMenu()
    {
        mode = Mode.Settings;
        settingsSelection = 0;
        if (player != null) player.movementLocked = true;
    }

    // Keep the fewest-mistakes record for the piano that was just played (lower wins).
    // Does nothing for the tutorial (activePiano is null).
    void RecordPianoScore(int mistakes)
    {
        if (activePiano == null) return;
        if (!pianoBestMistakes.TryGetValue(activePiano, out int best) || mistakes < best)
            pianoBestMistakes[activePiano] = mistakes;
    }

    // Display name for a piano in the records list: explicit displayName, else the
    // song's asset name, else the GameObject name.
    static string PianoLabel(Piano p)
    {
        if (p == null) return "—";
        if (!string.IsNullOrEmpty(p.displayName)) return p.displayName;
        if (p.songConfig != null && p.songConfig.song != null) return p.songConfig.song.name;
        return p.name;
    }

    void EnterKeyBindsMenu()
    {
        mode = Mode.KeyBinds;
        keyBindSelection = 0;
        capturingRebind = false;
    }

    void EnterOverworldKeyBindsMenu()
    {
        mode = Mode.OverworldKeyBinds;
        keyBindSelection = 0;
        capturingRebind = false;
    }

    void EnterAudioMenu()
    {
        mode = Mode.AudioSettings;
        // Initial highlight matches the currently-applied offset.
        audioSelection = (Mathf.Abs(audioOffsetSelection - BluetoothAudioOffset) < 0.001f) ? 0 : 1;
    }

    // Walks all KeyCodes and returns the first one that's down THIS frame and
    // isn't a navigation/skip key. KeyCode.None means "nothing valid pressed".
    KeyCode CaptureFirstBindableKey()
    {
        foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
        {
            if (kc == KeyCode.None) continue;
            if (kc == KeyCode.LeftArrow || kc == KeyCode.RightArrow) continue;
            if (kc == KeyCode.UpArrow   || kc == KeyCode.DownArrow)  continue;
            if (kc == KeyCode.Escape) continue;
            if (kc == KeyCode.Return || kc == KeyCode.KeypadEnter) continue; // reserved: ENTER starts/closes a rebind
            int v = (int)kc;
            if (v >= 323 && v <= 329) continue; // KeyCode.Mouse0..Mouse6
            if (v >= 330) continue;             // KeyCode.JoystickButton0 and above
            if (Input.GetKeyDown(kc)) return kc;
        }
        return KeyCode.None;
    }

    // Rebinds the currently-highlighted slot in whichever keybind menu is open,
    // persists it, and pushes the change to the live consumer immediately.
    void RebindCurrentSlot(KeyCode newKey)
    {
        if (mode == Mode.OverworldKeyBinds)
        {
            overworldKeyBinds[keyBindSelection] = newKey;
            SaveBinds(OWKeyBindPrefsKeyPrefix, overworldKeyBinds);
            PushOverworldBinds();
        }
        else
        {
            // One slot per column — including the two space-bar columns (4 and 5).
            keyBinds[keyBindSelection] = newKey;
            SaveBinds(KeyBindPrefsKeyPrefix, keyBinds);
            if (pianoTiles != null) pianoTiles.customColumnKeys = (KeyCode[])keyBinds.Clone();
        }
    }

    // Hands the movement + sprint binds to the player. Interact (slot 5) isn't
    // pushed — it's read live through InteractKey by the overworld handler / Doors.
    void PushOverworldBinds()
    {
        if (player != null && overworldKeyBinds != null && overworldKeyBinds.Length >= 5)
            player.SetOverworldBinds(overworldKeyBinds[0], overworldKeyBinds[1],
                                     overworldKeyBinds[2], overworldKeyBinds[3], overworldKeyBinds[4]);
    }

    void ApplyAudioSelection(int selection)
    {
        // 0 = Bluetooth (-200 ms), 1 = No Bluetooth (0 ms).
        float newOffset = (selection == 0) ? BluetoothAudioOffset : 0f;
        audioOffsetSelection = newOffset;
        if (pianoTiles != null) pianoTiles.globalAudioOffset = newOffset;
        PlayerPrefs.SetFloat(AudioOffsetPrefsKey, newOffset);
        PlayerPrefs.SetInt(BluetoothPrefsKey, selection == 0 ? 1 : 0);
        PlayerPrefs.Save();
    }

    // Generic per-index KeyCode persistence, shared by the minigame and overworld
    // bind arrays (each uses its own PlayerPrefs prefix). A missing entry falls
    // back to the supplied default for that index.
    KeyCode[] LoadBinds(string prefix, KeyCode[] defaults)
    {
        var arr = new KeyCode[defaults.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            int saved = PlayerPrefs.GetInt(prefix + i, -1);
            arr[i] = (saved == -1) ? defaults[i] : (KeyCode)saved;
        }
        return arr;
    }

    void SaveBinds(string prefix, KeyCode[] arr)
    {
        for (int i = 0; i < arr.Length; i++)
            PlayerPrefs.SetInt(prefix + i, (int)arr[i]);
        PlayerPrefs.Save();
    }

    bool TryGetPlayerBounds(out Bounds bounds)
    {
        // Prefer the player's collider bounds (matches what "physically touching" means);
        // fall back to the sprite for early frames where the collider isn't ready yet.
        if (playerCollider != null) { bounds = playerCollider.bounds; return true; }
        if (playerSprite != null) { bounds = playerSprite.bounds; return true; }
        bounds = new Bounds(); return false;
    }

    Piano FindAdjacentPiano()
    {
        if (pianos == null || !TryGetPlayerBounds(out var pb)) return null;
        for (int i = 0; i < pianos.Length; i++)
        {
            var p = pianos[i];
            if (p != null && p.IsAdjacentTo(pb)) return p;
        }
        return null;
    }

    ReadableItem FindAdjacentReadable()
    {
        if (readableItems == null || !TryGetPlayerBounds(out var pb)) return null;
        for (int i = 0; i < readableItems.Length; i++)
        {
            var r = readableItems[i];
            if (r != null && r.IsAdjacentTo(pb)) return r;
        }
        return null;
    }

    Npc FindAdjacentNpc()
    {
        if (npcs == null || !TryGetPlayerBounds(out var pb)) return null;
        for (int i = 0; i < npcs.Length; i++)
        {
            var n = npcs[i];
            if (n != null && n.IsAdjacentTo(pb)) return n;
        }
        return null;
    }

    // Read the top-right pixel of the NPC's portrait once at conversation start
    // and use it as the dialogue box background. Falls back to black when the
    // portrait is missing or its import settings don't allow CPU reads (the
    // "Read/Write Enabled" tickbox must be on; otherwise GetPixel throws and we
    // catch silently). Text color is chosen for contrast against the box color.
    void CacheNpcBoxColors(Npc npc)
    {
        currentNpcBoxColor = Color.black;
        currentNpcTextColor = Color.black;
        if (npc == null || npc.portrait == null) return;

        var tex = npc.portrait;
        try
        {
            // (0,0) is bottom-left in Unity's texture space, so top-right is
            // (width-1, height-1).
            currentNpcBoxColor = tex.GetPixel(tex.width - 1, tex.height - 1);
            currentNpcBoxColor.a = 1f; // force opaque so the box reads cleanly
        }
        catch
        {
            // Texture import settings probably have Read/Write disabled. Stay black.
            return;
        }

        // NPC dialogue text is always black — never white, regardless of how dark
        // the box color sampled from the portrait is. (Selected dialogue choices
        // are drawn in the medium purple over in DrawNpcDialogue; everything else
        // stays black.)
        currentNpcTextColor = Color.black;
    }

    void EndNpcConversation()
    {
        currentNpc = null;
        currentDialogueElement = null;
        dialogueStack = null;
        choiceSelection = 0;
    }

    // SPACE pressed while a conversation is active.
    //   • If the current element is a CHOICE, confirm it: push the selected
    //     branch's sequence onto the stack and advance into it.
    //   • Otherwise (current element is a speaker line), advance to the next
    //     element in the current frame; pop frames as they exhaust.
    // Either way, ending up with no more elements ends the conversation.
    void AdvanceNpcDialogue()
    {
        if (currentDialogueElement == null) { EndNpcConversation(); return; }

        if (currentDialogueElement.choice != null)
        {
            var opts = currentDialogueElement.choice.options;
            DialogueElement[] branch = null;
            if (opts != null && opts.Length > 0)
            {
                int idx = Mathf.Clamp(choiceSelection, 0, opts.Length - 1);
                branch = opts[idx].branch;
            }
            choiceSelection = 0;
            if (branch != null && branch.Length > 0)
                dialogueStack.Push(new DialogueFrame { sequence = branch, index = 0 });
            // If the chosen branch is empty, we just skip to whatever comes
            // next in the parent sequence — AdvanceDialogueElement handles that
            // by popping the (still-current) frame and continuing.
            AdvanceDialogueElement();
        }
        else
        {
            AdvanceDialogueElement();
        }
    }

    // Walk the stack forward to the next element to display, popping any frames
    // that have exhausted. Sets currentDialogueElement to that element, or null
    // (and ends the conversation) if no more elements remain anywhere on the stack.
    void AdvanceDialogueElement()
    {
        while (dialogueStack != null && dialogueStack.Count > 0)
        {
            var frame = dialogueStack.Peek();
            if (frame.index >= frame.sequence.Length)
            {
                dialogueStack.Pop();
                continue;
            }
            currentDialogueElement = frame.sequence[frame.index];
            frame.index++;
            return;
        }
        currentDialogueElement = null;
        EndNpcConversation();
    }

    void SetupBackgroundMusic()
    {
        if (backgroundMusic == null) return;
        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.clip = backgroundMusic;
        bgmSource.loop = true;
        bgmSource.playOnAwake = false;
        bgmSource.volume = 0f; // EnterOverworld will fade up from silence on first entry
    }

    void FadeBackgroundMusic(float targetVolume, float duration)
    {
        if (bgmSource == null) return;
        if (bgmFadeCoroutine != null) StopCoroutine(bgmFadeCoroutine);
        bgmFadeCoroutine = StartCoroutine(FadeBgmRoutine(targetVolume, duration));
    }

    // IMGUI is laid out in a fixed virtual resolution and scaled to the real screen by a
    // GUI.matrix (set at the top of OnGUI), so menus and fonts stay the same proportion
    // of the screen at any resolution. UiH is the virtual height; UiW follows the real
    // aspect ratio; UiScale maps virtual → real pixels. Make everything bigger overall by
    // LOWERING UiRefHeight (or smaller by raising it).
    const float UiRefHeight = 1150f; // raised from 1080 → all menu text a bit smaller on screen
    float UiScale => (Screen.height > 0) ? Screen.height / UiRefHeight : 1f;
    float UiH => UiRefHeight;
    float UiW => (Screen.height > 0) ? UiRefHeight * Screen.width / Screen.height : UiRefHeight;

    void OnGUI()
    {
        // Apply the chosen menu font to every IMGUI style: all menus/prompts/overlays
        // build their styles with `new GUIStyle(GUI.skin.label)`, so they inherit this
        // one font. NPC dialogue sets its font explicitly (currentNpc.dialogueFont), so
        // it overrides this and is unaffected. menuFont == null → the default UI font.
        GUI.skin.label.font = menuFont;

        // Scale the whole IMGUI pass so the menus keep the same PROPORTIONS at any
        // resolution. The draw code below is laid out in a fixed virtual space (UiH tall,
        // UiW wide for the current aspect); this matrix maps it onto the real screen, so
        // a 48-px font is always 48/1080 of the screen height regardless of resolution.
        GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale, UiScale, 1f));

        if (mode == Mode.Dead)
        {
            DrawDeadScreen();
            return;
        }

        if (mode == Mode.Finished)
        {
            DrawFinishedScreen();
            return;
        }

        if (mode == Mode.TutorialDialogue)
        {
            DrawTutorialDialogue();
            return;
        }

        if (mode == Mode.PromptingSpeed)
        {
            DrawSpeedPrompt();
            return;
        }

        if (mode == Mode.PromptingSection)
        {
            DrawSectionPrompt();
            return;
        }

        if (mode == Mode.Settings)         { DrawSettingsMenu();         return; }
        if (mode == Mode.KeyBinds)         { DrawKeyBindsMenu();         return; }
        if (mode == Mode.OverworldKeyBinds){ DrawOverworldKeyBindsMenu();return; }
        if (mode == Mode.AudioSettings)    { DrawAudioMenu();            return; }

        if (mode == Mode.Overworld)
        {
            if (currentNpc != null) DrawNpcDialogue();
            else if (!string.IsNullOrEmpty(currentDescription)) DrawDescriptionBubble();
        }
    }

    // ---- ESC menu draws ---------------------------------------------------

    void DrawSettingsMenu()
    {
        DrawDimOverlay();
        var rowStyle = MakeBodyStyle(48);
        var hintStyle = MakeHintStyle(40);
        Color selected = MenuAccent;
        Color unselected = new Color(0.85f, 0.85f, 0.85f);

        float h = UiH, w = UiW;

        // Title on the left.
        var titleStyle = MakeTitleStyle(86);
        titleStyle.alignment = TextAnchor.UpperLeft;
        titleStyle.fontStyle = FontStyle.Bold;
        GUI.Label(new Rect(w * 0.07f, h * 0.10f, w * 0.45f, 130f), "SETTINGS", titleStyle);

        // Left column: the navigable options, below the title.
        string[] rows = { "Minigame Keybinds", "Overworld Keybinds", "Audio" };
        for (int i = 0; i < rows.Length; i++)
        {
            float y = h * (0.34f + i * 0.12f);
            if (settingsSelection == i)
                DrawSelectionBar(new Rect(w * 0.07f, y + 10f, w * 0.40f, 70f));
            var s = new GUIStyle(rowStyle);
            s.alignment = TextAnchor.MiddleLeft;
            s.normal.textColor = (settingsSelection == i) ? selected : unselected;
            string label = (settingsSelection == i ? "▶  " : "    ") + rows[i];
            GUI.Label(new Rect(w * 0.09f, y, w * 0.42f, 80f), label, s);
        }

        // Right side: read-only piano records, filling the whole right side of the screen.
        DrawPianoRecords(new Rect(w * 0.52f, h * 0.20f, w * 0.43f, h * 0.62f));

        GUI.Label(new Rect(0f, h * 0.88f, w, 60f), "↑↓ — select        SPACE — open        " + MenuKeyLabel + " — back to game", hintStyle);
    }

    // Read-only "fewest mistakes per piano" panel drawn inside `area`. Shows N/A for
    // pianos not yet played this run. Not interactive — just a side display.
    void DrawPianoRecords(Rect area)
    {
        var headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 42, alignment = TextAnchor.LowerLeft, fontStyle = FontStyle.Bold };
        headerStyle.normal.textColor = MenuAccent;
        GUI.Label(new Rect(area.x, area.y - 64f, area.width, 56f), "PIANO RECORDS", headerStyle);
        DrawFilledRect(new Rect(area.x, area.y - 6f, area.width, 2f),
                       new Color(MenuAccent.r, MenuAccent.g, MenuAccent.b, 0.5f));

        int n = (pianos != null) ? pianos.Length : 0;
        if (n == 0)
        {
            var emptyStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.UpperLeft };
            emptyStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(new Rect(area.x, area.y + 8f, area.width, 50f), "No pianos.", emptyStyle);
            return;
        }

        // Rows fill the panel's height (capped so a single piano isn't enormous) and the
        // block is centered vertically. Font scales up with the row height — bigger list.
        float rowH = Mathf.Min(area.height / n, 130f);
        float startY = area.y + (area.height - rowH * n) * 0.5f;
        int fontSize = Mathf.Clamp(Mathf.RoundToInt(rowH * 0.4f), 22, 56);
        var nameStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, alignment = TextAnchor.MiddleLeft };
        nameStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
        var valStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
        for (int i = 0; i < n; i++)
        {
            float y = startY + i * rowH;
            int best = 0;
            bool played = pianos[i] != null && pianoBestMistakes.TryGetValue(pianos[i], out best);
            var vs = new GUIStyle(valStyle);
            vs.normal.textColor = played ? MenuAccent : new Color(0.55f, 0.55f, 0.55f);
            GUI.Label(new Rect(area.x, y, area.width, rowH), PianoLabel(pianos[i]), nameStyle);
            GUI.Label(new Rect(area.x, y, area.width, rowH), played ? best.ToString() : "N/A", vs);
        }
    }

    void DrawKeyBindsMenu()
    {
        DrawDimOverlay();
        var labelStyle = MakeBodyStyle(40);
        var hintStyle  = MakeHintStyle(36);

        Color boxFill         = new Color(0.10f, 0.10f, 0.12f, 0.95f);
        Color boxFillSelected = new Color(0.20f, 0.18f, 0.10f, 0.95f); // warm tint under the accent stroke
        Color boxStroke = new Color(0.65f, 0.65f, 0.65f);
        Color boxStrokeSelected = MenuAccent;

        float h = UiH, w = UiW;
        DrawMenuTitle("MINIGAME KEYBINDS", 0.14f, 72);

        // 10 equal-width slots, one per column. Columns 4 and 5 are the two
        // space-bar slots — independently selectable and rebindable. A column
        // currently mapped to Space shows the ⎵ glyph instead of the long word
        // "Space"; everything else shows its key label.
        const int slotCount = 10;
        float rowYCenter = h * 0.46f;
        float rowH = 220f;
        float rowTop = rowYCenter - rowH * 0.5f;

        float rowWidth = w * 0.92f;
        float gap = 12f;
        float boxW = (rowWidth - gap * (slotCount - 1)) / slotCount;
        float x = (w - rowWidth) * 0.5f;

        for (int slot = 0; slot < slotCount; slot++)
        {
            var rect = new Rect(x, rowTop, boxW, rowH);

            // Stroked box — the selected slot gets a warm fill + accent stroke.
            bool isSelected = (slot == keyBindSelection);
            Color stroke = isSelected ? boxStrokeSelected : boxStroke;
            DrawFilledRect(rect, isSelected ? boxFillSelected : boxFill);
            DrawStrokedRect(rect, stroke, isSelected ? 5f : 3f);

            // Key label stays bright even on unselected slots (the dimmer gray
            // is only for the box stroke).
            Color labelCol = isSelected ? boxStrokeSelected : new Color(0.85f, 0.85f, 0.85f);
            if (isSelected && capturingRebind)
            {
                // Awaiting the new key for this slot.
                var lbl = new GUIStyle(labelStyle);
                lbl.alignment = TextAnchor.MiddleCenter;
                lbl.normal.textColor = labelCol;
                lbl.fontStyle = FontStyle.Bold;
                lbl.fontSize = 58;
                GUI.Label(rect, "…", lbl);
            }
            else if (keyBinds[slot] == KeyCode.Space)
            {
                // Space columns draw the ⎵ glyph rather than the word "Space".
                DrawSpaceGlyph(rect, labelCol);
            }
            else
            {
                var lbl = new GUIStyle(labelStyle);
                lbl.alignment = TextAnchor.MiddleCenter;
                lbl.normal.textColor = labelCol;
                lbl.fontStyle = FontStyle.Bold;
                lbl.fontSize = 58;
                GUI.Label(rect, PianoTilesManager.KeyCodeToColumnLabel(keyBinds[slot]), lbl);
            }

            x += boxW + gap;
        }

        string kbHint = capturingRebind
            ? "Press the new key…        " + MenuKeyLabel + " — cancel"
            : "←→ or W/S — select        ENTER — rebind        " + MenuKeyLabel + " — back";
        GUI.Label(new Rect(0f, h * 0.78f, w, 60f), kbHint, hintStyle);
    }

    void DrawOverworldKeyBindsMenu()
    {
        DrawDimOverlay();
        var labelStyle = MakeBodyStyle(40);
        var hintStyle  = MakeHintStyle(36);
        Color selected = MenuAccent;
        Color unselected = new Color(0.85f, 0.85f, 0.85f);
        Color boxFill         = new Color(0.10f, 0.10f, 0.12f, 0.95f);
        Color boxFillSelected = new Color(0.20f, 0.18f, 0.10f, 0.95f);

        float h = UiH, w = UiW;
        DrawMenuTitle("OVERWORLD KEYBINDS", 0.12f, 72);

        int count = overworldKeyBinds.Length;
        // Fit however many rows there are (now 7, incl. Menu / Back) between the title and
        // the hint, so adding a row never pushes the last one off the bottom.
        float rowH = h * 0.55f / Mathf.Max(1, count);
        float startY = h * 0.28f;
        float listW = w * 0.52f;
        float listX = (w - listW) * 0.5f;

        for (int i = 0; i < count; i++)
        {
            float y = startY + i * rowH;
            bool isSel = (i == keyBindSelection);
            var rowRect = new Rect(listX, y, listW, rowH - 12f);
            if (isSel) DrawSelectionBar(rowRect);

            // Action name (left).
            var nameStyle = new GUIStyle(labelStyle);
            nameStyle.alignment = TextAnchor.MiddleLeft;
            nameStyle.normal.textColor = isSel ? selected : unselected;
            GUI.Label(new Rect(rowRect.x + 28f, y, listW * 0.6f, rowRect.height),
                      (isSel ? "▶ " : "   ") + OverworldActionLabels[i], nameStyle);

            // Key box (right).
            float boxW = listW * 0.30f, boxH = rowRect.height - 14f;
            var boxRect = new Rect(rowRect.xMax - boxW - 8f, y + 7f, boxW, boxH);
            bool capHere = isSel && capturingRebind;
            DrawFilledRect(boxRect, (isSel ? boxFillSelected : boxFill));
            DrawStrokedRect(boxRect, isSel ? MenuAccent : new Color(0.65f, 0.65f, 0.65f), isSel ? 4f : 2f);

            Color keyCol = isSel ? MenuAccent : unselected;
            if (capHere)
            {
                var ks = new GUIStyle(labelStyle); ks.alignment = TextAnchor.MiddleCenter;
                ks.fontStyle = FontStyle.Bold; ks.normal.textColor = keyCol;
                GUI.Label(boxRect, "…", ks);
            }
            else if (overworldKeyBinds[i] == KeyCode.Space)
            {
                DrawSpaceGlyph(boxRect, keyCol);
            }
            else
            {
                var ks = new GUIStyle(labelStyle); ks.alignment = TextAnchor.MiddleCenter;
                ks.fontStyle = FontStyle.Bold; ks.normal.textColor = keyCol;
                GUI.Label(boxRect, PianoTilesManager.KeyCodeToColumnLabel(overworldKeyBinds[i]), ks);
            }
        }

        string owHint = capturingRebind
            ? "Press the new key…        " + MenuKeyLabel + " — cancel"
            : "W/S or ↑↓ — select        ENTER — rebind        " + MenuKeyLabel + " — back";
        GUI.Label(new Rect(0f, h * 0.86f, w, 60f), owHint, hintStyle);
    }

    void DrawAudioMenu()
    {
        DrawDimOverlay();
        var rowStyle = MakeBodyStyle(56);
        var hintStyle = MakeHintStyle(40);
        Color selected = MenuAccent;
        Color unselected = new Color(0.85f, 0.85f, 0.85f);

        float h = UiH, w = UiW;
        DrawMenuTitle("AUDIO", 0.18f, 96);

        string[] rows =
        {
            "Bluetooth      (audio offset: -200 ms)",
            "No Bluetooth   (audio offset:    0 ms)"
        };
        for (int i = 0; i < rows.Length; i++)
        {
            float y = h * (0.40f + i * 0.10f);
            if (audioSelection == i)
                DrawSelectionBar(new Rect(w * 0.18f, y + 12f, w * 0.64f, 76f));
            var s = new GUIStyle(rowStyle);
            s.normal.textColor = (audioSelection == i) ? selected : unselected;
            string label = (audioSelection == i ? "▶ " : "   ") + rows[i];
            GUI.Label(new Rect(0f, y, w, 100f), label, s);
        }

        GUI.Label(new Rect(0f, h * 0.78f, w, 60f),
                  "↑↓ — select        SPACE — apply & back        " + MenuKeyLabel + " — back without changing",
                  hintStyle);
    }

    // ---- Menu draw helpers ------------------------------------------------

    void DrawDimOverlay()
    {
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.78f);
        GUI.DrawTexture(new Rect(0f, 0f, UiW, UiH), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    // Shared menu accent (warm yellow — same hue the playfield uses for catches).
    static readonly Color MenuAccent = new Color(1f, 0.92f, 0.4f);

    // Bold title with a soft drop shadow and a thin accent underline. Every
    // full-screen menu draws its header through this so they all read the same.
    void DrawMenuTitle(string text, float yRatio, int size)
    {
        DrawMenuTitle(text, yRatio, size, Color.white, MenuAccent);
    }

    void DrawMenuTitle(string text, float yRatio, int size, Color textColor, Color accent)
    {
        float h = UiH, w = UiW;
        var rect = new Rect(0f, h * yRatio, w, 160f);

        var style = MakeTitleStyle(size);
        style.fontStyle = FontStyle.Bold;

        var shadow = new GUIStyle(style);
        shadow.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
        GUI.Label(new Rect(rect.x + 3f, rect.y + 4f, rect.width, rect.height), text, shadow);

        style.normal.textColor = textColor;
        GUI.Label(rect, text, style);

        float lineW = Mathf.Clamp(size * 4.5f, 240f, w * 0.4f);
        var underline = accent; underline.a = 0.9f;
        DrawFilledRect(new Rect((w - lineW) * 0.5f, rect.y + 140f, lineW, 4f), underline);
    }

    // Soft highlight bar drawn behind the currently-selected menu row, with a
    // small accent notch on each end so the selection reads even before the
    // text color change registers.
    void DrawSelectionBar(Rect r)
    {
        DrawFilledRect(r, new Color(MenuAccent.r, MenuAccent.g, MenuAccent.b, 0.08f));
        DrawFilledRect(new Rect(r.x, r.y, 5f, r.height), new Color(MenuAccent.r, MenuAccent.g, MenuAccent.b, 0.85f));
        DrawFilledRect(new Rect(r.xMax - 5f, r.y, 5f, r.height), new Color(MenuAccent.r, MenuAccent.g, MenuAccent.b, 0.85f));
    }

    GUIStyle MakeTitleStyle(int size)
    {
        var s = new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            alignment = TextAnchor.MiddleCenter,
        };
        s.normal.textColor = Color.white;
        return s;
    }

    GUIStyle MakeBodyStyle(int size)
    {
        var s = new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            alignment = TextAnchor.MiddleCenter,
        };
        s.normal.textColor = Color.white;
        return s;
    }

    GUIStyle MakeHintStyle(int size)
    {
        var s = new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
        };
        s.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        return s;
    }

    void DrawFilledRect(Rect r, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    void DrawStrokedRect(Rect r, Color color, float thickness)
    {
        DrawFilledRect(new Rect(r.x, r.y, r.width, thickness), color);                           // top
        DrawFilledRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);            // bottom
        DrawFilledRect(new Rect(r.x, r.y, thickness, r.height), color);                          // left
        DrawFilledRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);           // right
    }

    void DrawSpaceGlyph(Rect r, Color color)
    {
        // Mini space-bar symbol inside the slot 4 box: thin horizontal bar near
        // the bottom-center with two short upright caps.
        float w = r.width * 0.6f;
        float capH = r.height * 0.4f;
        float thickness = 6f;
        float cx = r.center.x;
        float bottom = r.center.y + capH * 0.5f;
        // bar
        DrawFilledRect(new Rect(cx - w * 0.5f, bottom - thickness, w, thickness), color);
        // left cap
        DrawFilledRect(new Rect(cx - w * 0.5f, bottom - capH, thickness, capH), color);
        // right cap
        DrawFilledRect(new Rect(cx + w * 0.5f - thickness, bottom - capH, thickness, capH), color);
    }

    void DrawSectionPrompt()
    {
        DrawDimOverlay();
        var hintStyle = MakeHintStyle(48);

        float h = UiH;
        const float listTopRatio = 0.34f;
        const float listBottomRatio = 0.82f; // keep a gap above the hints (which start at 0.86)
        float availableHeight = h * (listBottomRatio - listTopRatio);

        DrawMenuTitle("START FROM SECTION", 0.16f, 96);

        // List the sections vertically. Row height + font size shrink to fit when
        // there are many sections; clamp to sane min/max so few sections don't blow up
        // and many sections don't become unreadable.
        if (sectionOptions != null && sectionOptions.Length > 0)
        {
            int n = sectionOptions.Length;
            float idealRowH = availableHeight / n;
            float rowH = Mathf.Min(idealRowH, 120f);
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(rowH * 0.7f), 16, 84);

            // Center the block vertically in the available area when it doesn't fill it.
            float totalH = rowH * n;
            float startY = h * listTopRatio + (availableHeight - totalH) * 0.5f;

            var optionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleCenter,
            };

            for (int i = 0; i < sectionOptions.Length; i++)
            {
                var st = new GUIStyle(optionStyle);
                bool sel = (i == sectionSelection);
                if (sel)
                    DrawSelectionBar(new Rect(UiW * 0.25f, startY + i * rowH + rowH * 0.1f,
                                              UiW * 0.50f, rowH * 0.8f));
                st.normal.textColor = sel ? MenuAccent : Color.white;
                string label = (sel ? "▶ " : "   ") + sectionOptions[i].name;
                GUI.Label(new Rect(0f, startY + i * rowH, UiW, rowH), label, st);
            }
        }

        GUI.Label(new Rect(0f, h * 0.86f, UiW, 90f), "↑↓ or W/S — select", hintStyle);
        GUI.Label(new Rect(0f, h * 0.91f, UiW, 90f), "SPACE — start        " + MenuKeyLabel + " — cancel", hintStyle);
    }

    void DrawSpeedPrompt()
    {
        DrawDimOverlay();

        var labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 60,
            alignment = TextAnchor.MiddleRight,
        };

        var valueStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 96,
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
        };

        var hintStyle = MakeHintStyle(48);

        Color selected = MenuAccent;
        Color unselected = new Color(0.85f, 0.85f, 0.85f);

        float h = UiH;
        float w = UiW;

        DrawMenuTitle("PRACTICE OPTIONS", 0.14f, 96);

        // Three rows of "label : value". The selected row is brightened to yellow.
        float gap = 30f;
        float rowH = 130f;
        float row0Y = h * 0.32f;
        float row1Y = h * 0.44f;
        float row2Y = h * 0.56f;
        float labelW = w * 0.50f - gap * 0.5f;
        float valueX = w * 0.5f + gap * 0.5f;
        float valueW = w * 0.5f - gap * 0.5f;

        DrawSpeedPromptRow(0, "Music speed:",     $"{speedSelection:F1}x",
                           row0Y, rowH, labelW, valueX, valueW,
                           labelStyle, valueStyle, selected, unselected);
        DrawSpeedPromptRow(1, "Tile fall speed:", $"{tileSpeedSelection:F1}x",
                           row1Y, rowH, labelW, valueX, valueW,
                           labelStyle, valueStyle, selected, unselected);

        // Row 2 — audio offset rendered in milliseconds with explicit sign so the
        // direction is obvious at a glance ("+150 ms" / "-180 ms" / "0 ms").
        int offsetMs = Mathf.RoundToInt(audioOffsetSelection * 1000f);
        string offsetStr = offsetMs == 0
            ? "0 ms"
            : (offsetMs > 0 ? $"+{offsetMs} ms" : $"{offsetMs} ms");
        DrawSpeedPromptRow(2, "Audio offset:", offsetStr,
                           row2Y, rowH, labelW, valueX, valueW,
                           labelStyle, valueStyle, selected, unselected);

        GUI.Label(new Rect(0f, h * 0.72f, w, 70f),
                  "↑↓ or W/S — select row     ←→ or A/D — adjust",
                  hintStyle);
        GUI.Label(new Rect(0f, h * 0.77f, w, 70f),
                  "SPACE — start        " + MenuKeyLabel + " — cancel",
                  hintStyle);
        // This note is long and wraps to ~2 lines, so it needs a taller box than the
        // single-line hints above — otherwise the second line gets clipped at the bottom.
        GUI.Label(new Rect(0f, h * 0.83f, w, 150f),
                  "(audio offset: negative = music plays earlier — set ~-150 to -200 ms on Bluetooth. Saved across sessions.)",
                  hintStyle);
    }

    // Helper for the three-row label/value layout above. Brightens both halves of
    // the selected row to yellow and prefixes the label with ▶.
    void DrawSpeedPromptRow(int rowIdx, string label, string value,
                            float y, float rowH, float labelW, float valueX, float valueW,
                            GUIStyle labelStyle, GUIStyle valueStyle,
                            Color selected, Color unselected)
    {
        var l = new GUIStyle(labelStyle);
        var v = new GUIStyle(valueStyle);
        bool isSelected = (speedPromptField == rowIdx);
        if (isSelected)
            DrawSelectionBar(new Rect(UiW * 0.16f, y + rowH * 0.12f,
                                      UiW * 0.68f, rowH * 0.76f));
        l.normal.textColor = isSelected ? selected : unselected;
        v.normal.textColor = isSelected ? selected : unselected;
        string prefixedLabel = (isSelected ? "▶ " : "   ") + label;
        GUI.Label(new Rect(0f,     y, labelW, rowH), prefixedLabel, l);
        GUI.Label(new Rect(valueX, y, valueW, rowH), value,         v);
    }

    void DrawDeadScreen()
    {
        DrawDimOverlay();

        var bodyStyle = MakeBodyStyle(32);
        bodyStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
        var hintStyle = MakeHintStyle(36);

        // Muted red title — reads as a fail state without being garish.
        Color deadRed = new Color(0.95f, 0.38f, 0.38f);

        float h = UiH;
        DrawMenuTitle("YOU DIED", 0.26f, 72, deadRed, deadRed);
        GUI.Label(new Rect(0f, h * 0.46f, UiW, 50f),
                  $"Left hand (asdfg): {finalLeftMissCount}", bodyStyle);
        GUI.Label(new Rect(0f, h * 0.52f, UiW, 50f),
                  $"Right hand (hjkl;): {finalRightMissCount}", bodyStyle);
        GUI.Label(new Rect(0f, h * 0.66f, UiW, 60f),
                  "ENTER — retry        " + MenuKeyLabel + " — exit", hintStyle);
    }

    void DrawFinishedScreen()
    {
        DrawDimOverlay();

        var bodyStyle = MakeBodyStyle(32);
        bodyStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
        var hintStyle = MakeHintStyle(36);

        float h = UiH;
        // Title in the shared warm accent — a small "victory" note against the
        // muted red of the dead screen.
        DrawMenuTitle("SONG COMPLETE", 0.24f, 72, MenuAccent, MenuAccent);

        // Tiles hit (the positive metric) — a hit/total ratio, independent of the
        // miss tallies below. Drawn in a soft green to set it apart from the misses.
        var hitStyle = new GUIStyle(bodyStyle);
        hitStyle.normal.textColor = new Color(0.55f, 0.85f, 0.6f);
        GUI.Label(new Rect(0f, h * 0.42f, UiW, 50f),
                  $"Tiles Hit: {finalHitCount} / {finalTotalTiles}", hitStyle);

        var missStyle = new GUIStyle(bodyStyle);
        missStyle.normal.textColor = new Color(0.95f, 0.38f, 0.38f); // red, matching the in-game miss counter
        GUI.Label(new Rect(0f, h * 0.50f, UiW, 50f),
                  $"Total Mistakes: {finalMissCount}", missStyle);
        GUI.Label(new Rect(0f, h * 0.56f, UiW, 50f),
                  $"Left Hand Mistakes: {finalLeftMissCount}", bodyStyle);
        GUI.Label(new Rect(0f, h * 0.62f, UiW, 50f),
                  $"Right Hand Mistakes: {finalRightMissCount}", bodyStyle);
        // During the tutorial, a non-final SONG COMPLETE also offers SPACE to continue.
        string finishHint = (tutorialActive && HasNextTutorialEntry())
            ? "R — restart        " + MenuKeyLabel + " — exit        SPACE — continue"
            : "R — restart        " + MenuKeyLabel + " — exit";
        GUI.Label(new Rect(0f, h * 0.72f, UiW, 60f), finishHint, hintStyle);
    }

    void DrawTutorialDialogue()
    {
        DrawDimOverlay();
        float w = UiW, h = UiH;

        // Big, word-wrapped dialogue filling a decent chunk of the screen.
        var textStyle = MakeBodyStyle(Mathf.Max(20, Mathf.RoundToInt(h * 0.05f)));
        textStyle.wordWrap = true;
        GUI.Label(new Rect(w * 0.12f, h * 0.16f, w * 0.76f, h * 0.56f), tutorialDialogueText, textStyle);

        var hintStyle = MakeHintStyle(36);
        GUI.Label(new Rect(0f, h * 0.82f, w, 60f), "Press SPACE to continue", hintStyle);
    }

    void DrawDescriptionBubble()
    {
        // Dim bar across the bottom of the screen.
        float boxHeight = Mathf.Clamp(UiH * 0.22f, 110f, 200f);
        float boxY = UiH - boxHeight;

        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.82f);
        GUI.DrawTexture(new Rect(0f, boxY, UiW, boxHeight), Texture2D.whiteTexture);
        GUI.color = prev;

        // Thin accent line along the bubble's top edge so it reads as a panel
        // rather than a raw black bar.
        DrawFilledRect(new Rect(0f, boxY, UiW, 3f),
                       new Color(MenuAccent.r, MenuAccent.g, MenuAccent.b, 0.7f));

        // Description text — font size comes from the item that's currently showing.
        var textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = currentFontSize,
            wordWrap = true,
            alignment = TextAnchor.UpperCenter,
            richText = true,
        };
        textStyle.normal.textColor = Color.white;

        const float padding = 24f;
        const float hintHeight = 22f;
        var textRect = new Rect(
            padding,
            boxY + padding,
            UiW - 2f * padding,
            boxHeight - 2f * padding - hintHeight);
        GUI.Label(textRect, currentDescription, textStyle);

        // Hint at the bottom-right of the bubble.
        var hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.LowerRight,
        };
        hintStyle.normal.textColor = new Color(0.78f, 0.78f, 0.78f);
        GUI.Label(
            new Rect(0f, UiH - hintHeight - 4f, UiW - padding, hintHeight),
            "Press SPACE to close",
            hintStyle);
    }

    void DrawNpcDialogue()
    {
        // Layout: text box is 50% of screen width; portrait sits to its left,
        // bottom-aligned with the text box. The whole group (portrait + box) is
        // centered horizontally. A small bottom margin keeps a sliver of the
        // overworld visible below the box.
        const float bottomMargin = 28f;
        const float padding = 28f;
        const float hintHeight = 24f;

        float boxHeight = Mathf.Clamp(UiH * 0.48f, 300f, 480f);
        float textBoxWidth = UiW * 0.5f;
        float boxY = UiH - boxHeight - bottomMargin;
        float boxBottom = boxY + boxHeight;

        // Portrait matches the text box's height exactly.
        float portraitWidth = 0f;
        float portraitHeight = 0f;
        if (currentNpc.portrait != null)
        {
            portraitHeight = boxHeight;
            var tex = currentNpc.portrait;
            float aspect = tex.height > 0 ? (float)tex.width / tex.height : 1f;
            portraitWidth = portraitHeight * aspect;
        }

        float groupWidth = portraitWidth + textBoxWidth;
        float groupX = (UiW - groupWidth) * 0.5f;
        float portraitX = groupX;
        float textBoxX = groupX + portraitWidth;

        // Portrait — bottom-aligned with the text box.
        if (currentNpc.portrait != null)
        {
            float portraitY = boxBottom - portraitHeight;
            GUI.DrawTexture(
                new Rect(portraitX, portraitY, portraitWidth, portraitHeight),
                currentNpc.portrait);
        }

        // Text box — colored to match the top-right pixel of the NPC's portrait
        // (sampled once on conversation start by CacheNpcBoxColors).
        var prev = GUI.color;
        GUI.color = currentNpcBoxColor;
        GUI.DrawTexture(new Rect(textBoxX, boxY, textBoxWidth, boxHeight), Texture2D.whiteTexture);
        GUI.color = prev;

        // Hairline outline in the contrast text color so the box keeps a crisp
        // edge against busy scenes regardless of the sampled portrait color.
        DrawStrokedRect(new Rect(textBoxX, boxY, textBoxWidth, boxHeight),
                        new Color(currentNpcTextColor.r, currentNpcTextColor.g, currentNpcTextColor.b, 0.25f),
                        2f);

        var textRect = new Rect(
            textBoxX + padding,
            boxY + padding,
            textBoxWidth - 2f * padding,
            boxHeight - 2f * padding - hintHeight);

        bool atChoice = currentDialogueElement != null && currentDialogueElement.choice != null;

        if (atChoice)
        {
            var opts = currentDialogueElement.choice.options;
            int n = (opts != null) ? opts.Length : 0;
            // Render the N options stacked vertically, evenly splitting the
            // available text rect. The selected one is highlighted purple and
            // prefixed with ▶; the others use the contrast-picked text color.
            var optionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = currentNpc.fontSize,
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft,
                richText = true,
                font = currentNpc.dialogueFont, // null falls back to the default GUI font
            };

            Color selectedColor = new Color(0.62f, 0.32f, 0.88f); // medium purple
            float rowH = n > 0 ? textRect.height / n : textRect.height;

            // Shrink to a single uniform size (never above the authored size) so
            // that even the longest option fits inside its row without clipping.
            // The more options there are, the shorter each row — so this naturally
            // gets smaller as the choice list grows. The 6px trim keeps stacked
            // rows from visually touching / clipping descenders.
            int fitSize = Mathf.Max(MinDialogueFontSize, currentNpc.fontSize);
            for (int i = 0; i < n; i++)
            {
                string probe = "▶ " + (opts[i] != null ? opts[i].optionText : "");
                fitSize = Mathf.Min(fitSize,
                    FitFontSize(optionStyle, probe, textRect.width, rowH - 6f, currentNpc.fontSize));
            }
            optionStyle.fontSize = fitSize;

            for (int i = 0; i < n; i++)
            {
                var rowRect = new Rect(textRect.x, textRect.y + i * rowH, textRect.width, rowH);
                var rowStyle = new GUIStyle(optionStyle);
                rowStyle.normal.textColor = (choiceSelection == i) ? selectedColor : currentNpcTextColor;
                string prefix = (choiceSelection == i ? "▶ " : "   ");
                GUI.Label(rowRect, prefix + (opts[i] != null ? opts[i].optionText : ""), rowStyle);
            }
        }
        else
        {
            // Show the current speaker line from the tree element.
            string line = currentDialogueElement != null ? currentDialogueElement.speakerLine : "";

            var textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = currentNpc.fontSize,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                richText = true,
                font = currentNpc.dialogueFont, // null falls back to the default GUI font
            };
            textStyle.normal.textColor = currentNpcTextColor;
            // Shrink long lines so they never overflow the box; short lines keep
            // the authored size. The 4px trim guards the bottom edge.
            textStyle.fontSize = FitFontSize(textStyle, line, textRect.width, textRect.height - 4f, currentNpc.fontSize);
            GUI.Label(textRect, line, textStyle);
        }

        // Hint at the bottom-right of the text box. Tint it to ~70% of the
        // chosen text color so it stays visibly secondary against either a
        // bright or dark box without disappearing.
        var hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.LowerRight,
            font = currentNpc.dialogueFont,
        };
        hintStyle.normal.textColor = new Color(
            currentNpcTextColor.r * 0.78f + (1f - 0.78f) * currentNpcBoxColor.r,
            currentNpcTextColor.g * 0.78f + (1f - 0.78f) * currentNpcBoxColor.g,
            currentNpcTextColor.b * 0.78f + (1f - 0.78f) * currentNpcBoxColor.b
        );

        // Hint text:
        //   • Choice element → "↑↓ select / SPACE confirm / ESC exit"
        //   • Last speaker line of the conversation (no more elements anywhere
        //     on the stack) → "SPACE — close"
        //   • Any other speaker line → "SPACE — next"
        string hintText;
        if (atChoice)
        {
            hintText = "↑↓ select        SPACE confirm        " + MenuKeyLabel + " exit";
        }
        else
        {
            bool moreRemaining = false;
            if (dialogueStack != null)
            {
                foreach (var f in dialogueStack)
                {
                    if (f.index < f.sequence.Length) { moreRemaining = true; break; }
                }
            }
            hintText = moreRemaining
                ? "SPACE — next        " + MenuKeyLabel + " — exit"
                : "SPACE — close        " + MenuKeyLabel + " — exit";
        }
        GUI.Label(
            new Rect(textBoxX, boxBottom - hintHeight - 6f, textBoxWidth - padding, hintHeight),
            hintText,
            hintStyle);
    }

    // Smallest the dialogue font is ever auto-shrunk to. Below the authored
    // Npc.fontSize range (12-160) so the fitter has headroom to rescue long
    // lines / many choice options, while staying readable.
    const int MinDialogueFontSize = 10;

    // Largest font size in [MinDialogueFontSize, maxSize] at which `content`,
    // word-wrapped to `maxWidth`, fits within `maxHeight`. Lets dialogue shrink
    // just enough that long speaker lines or many wrapping choice options never
    // clip. Only shrinks: if the content already fits at maxSize, maxSize is
    // returned unchanged, so short dialogue keeps its authored size.
    // NOTE: mutates style.fontSize while measuring — callers set it explicitly after.
    int FitFontSize(GUIStyle style, string content, float maxWidth, float maxHeight, int maxSize)
    {
        int hi = Mathf.Max(MinDialogueFontSize, maxSize);
        if (string.IsNullOrEmpty(content) || maxWidth <= 0f || maxHeight <= 0f)
            return hi;

        var gc = new GUIContent(content);
        style.fontSize = hi;
        if (style.CalcHeight(gc, maxWidth) <= maxHeight)
            return hi;

        int lo = MinDialogueFontSize;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            style.fontSize = mid;
            if (style.CalcHeight(gc, maxWidth) <= maxHeight) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    IEnumerator FadeBgmRoutine(float target, float duration)
    {
        // Make sure the source is actually playing (resume from pause if needed)
        // before we fade volume up; otherwise the curve goes to a silent source.
        if (target > 0f && !bgmSource.isPlaying)
        {
            if (bgmSource.time > 0f) bgmSource.UnPause();
            else bgmSource.Play();
        }

        float startVol = bgmSource.volume;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            bgmSource.volume = Mathf.Lerp(startVol, target, elapsed / duration);
            yield return null;
        }
        bgmSource.volume = target;

        // Pause (don't Stop) so playback resumes from the same spot on re-entry.
        if (target <= 0f) bgmSource.Pause();
        bgmFadeCoroutine = null;
    }
}
