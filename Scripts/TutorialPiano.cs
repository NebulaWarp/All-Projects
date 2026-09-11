using UnityEngine;

/// <summary>
/// A special piano that runs the guided tutorial instead of the normal speed prompt:
/// a sequence of dialogue boxes interleaved with four song charts (left hand single
/// notes → left hand chords → right hand → everything combined). All four charts share
/// one audio clip and one set of playback settings. GameDirector drives the flow when
/// the player interacts with this piano (see GameDirector.StartTutorial).
///
/// Put this on a GameObject with a BoxCollider2D (like a normal Piano) and drag it into
/// GameDirector's "Tutorial Piano" slot.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class TutorialPiano : MonoBehaviour
{
    [Tooltip("Extra world-unit margin added to the player's bounds when checking adjacency.")]
    public float adjacencyMargin = 0.1f;

    [Header("Song (shared by all four charts)")]
    [Tooltip("The audio clip played under every tutorial chart.")]
    public AudioClip song;
    [Tooltip("Beats per minute (chart tempo markers still override this).")]
    public float bpm = 120f;
    [Tooltip("Tile height in world units. Fall distance per beat equals this.")]
    public float tileHeight = 2.2f;
    [Tooltip("Mistake limit for tutorial songs. Defaults very high so a beginner plays " +
             "through to SONG COMPLETE no matter how many mistakes they make; lower it " +
             "if you want the tutorial to be able to fail.")]
    public int missLimit = 999;
    [Tooltip("Seconds added to the auto-computed music delay (same meaning as SongConfig).")]
    public float musicLeadOffset = 0f;
    [Tooltip("Optional background image shown behind the tiles for every tutorial chart.")]
    public Sprite background;
    [Range(0f, 1f)] public float backgroundOpacity = 0.7f;
    [Range(0f, 1f)] public float backgroundBrightness = 0.45f;

    [Header("Charts (.txt, played in this order)")]
    [Tooltip("1) Left hand, single notes.")]
    public TextAsset chart1_LeftHandSingle;
    [Tooltip("2) Left hand, two notes at once.")]
    public TextAsset chart2_LeftHandChords;
    [Tooltip("3) Right hand only.")]
    public TextAsset chart3_RightHand;
    [Tooltip("4) Everything combined — the full song.")]
    public TextAsset chart4_Combined;

    SpriteRenderer sr;
    Collider2D col;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
    }

    public bool IsAdjacentTo(Bounds playerBounds)
    {
        Bounds pianoBounds;
        if (sr != null) pianoBounds = sr.bounds;
        else if (col != null) pianoBounds = col.bounds;
        else return false;

        playerBounds.Expand(adjacencyMargin * 2f);
        return playerBounds.Intersects(pianoBounds);
    }

    /// <summary>The .txt chart for tutorial song index 0..3 (null if that slot is empty).</summary>
    public TextAsset ChartAt(int index)
    {
        switch (index)
        {
            case 0:  return chart1_LeftHandSingle;
            case 1:  return chart2_LeftHandChords;
            case 2:  return chart3_RightHand;
            default: return chart4_Combined;
        }
    }

    /// <summary>Builds the SongConfig for tutorial song index 0..3: the shared song +
    /// settings with that chart, forced non-looping so it ends and shows SONG COMPLETE.</summary>
    public SongConfig ConfigForChart(int index)
    {
        return new SongConfig
        {
            song = song,
            bpm = bpm,
            tileHeight = tileHeight,
            chartFile = ChartAt(index),
            chart = "",
            loopChart = false,
            musicLeadOffset = musicLeadOffset,
            missLimit = missLimit,
            background = background,
            backgroundOpacity = backgroundOpacity,
            backgroundBrightness = backgroundBrightness,
        };
    }
}
