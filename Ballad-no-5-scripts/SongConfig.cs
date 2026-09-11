using UnityEngine;

[System.Serializable]
public class SongConfig
{
    [Tooltip("Audio clip played in chart mode. Optional — leave empty for chart-only.")]
    public AudioClip song;

    [Tooltip("Beats per minute. Drives both spawning cadence and fall speed.")]
    public float bpm = 120f;

    [Tooltip("Tile height in world units. Fall distance per beat equals this value, so consecutive beats touch top-to-bottom with no gap.")]
    public float tileHeight = 2.2f;

    [Tooltip("Optional. Drag a .txt asset here for long charts. If set, overrides the typed Chart below.")]
    public TextAsset chartFile;

    [Tooltip("One line = one beat. Same syntax as PianoTilesManager.chart.\n" +
             "Tokens: a s d f g h j k l ; (optionally with digit for hold beats, e.g. g2).\n" +
             "'-' = rest, '|' or blank line = visual separator, '//' = comment.\n" +
             "Ignored when Chart File is set.")]
    [TextArea(10, 30)]
    public string chart;

    public bool loopChart = true;

    [Tooltip("Seconds added to the auto-computed music delay. " +
             "The music naturally starts when the first tile's bottom edge reaches the hit line; " +
             "positive values delay it further, negative values move it earlier.")]
    public float musicLeadOffset = 0f;

    [Tooltip("The player is forced out of the minigame the moment MissCount exceeds this number.")]
    public int missLimit = 10;

    [Tooltip("Optional background image shown behind the tiles during this song. Drag any imported PNG/JPEG here.")]
    public Sprite background;
    [Tooltip("Alpha of the background image (0 = invisible, 1 = fully opaque). Lower to keep tiles legible.")]
    [Range(0f, 1f)] public float backgroundOpacity = 0.7f;
    [Tooltip("Brightness of the background image (1 = full color, 0 = black). Lower to dim the image.")]
    [Range(0f, 1f)] public float backgroundBrightness = 0.45f;
}
