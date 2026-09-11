using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class Piano : MonoBehaviour
{
    [Tooltip("Extra world-unit margin added to the player's bounds when checking adjacency. " +
             "If the (expanded) player bounds overlap the piano bounds, they're considered touching.")]
    public float adjacencyMargin = 0.1f;

    [Tooltip("Optional name shown for this piano in the menu's records list. " +
             "Leave empty to fall back to the song's name, then the GameObject name.")]
    public string displayName;

    [Header("Song")]
    [Tooltip("This piano's song & chart. Passed to PianoTilesManager when the player interacts.")]
    public SongConfig songConfig = new SongConfig();

    SpriteRenderer sr;
    Collider2D col;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
    }

    public bool IsAdjacentTo(Bounds playerBounds)
    {
        // Prefer the sprite's bounds; fall back to the collider so invisible
        // pianos (just a BoxCollider2D, no SpriteRenderer) still work.
        Bounds pianoBounds;
        if (sr != null) pianoBounds = sr.bounds;
        else if (col != null) pianoBounds = col.bounds;
        else return false;

        playerBounds.Expand(adjacencyMargin * 2f);
        return playerBounds.Intersects(pianoBounds);
    }
}
