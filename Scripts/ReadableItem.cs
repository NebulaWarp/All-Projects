using UnityEngine;

/// <summary>
/// Attach to any overworld object the player can read. When the player presses
/// SPACE while adjacent (same bounds-overlap rule as Piano), the description
/// text appears in a bar at the bottom of the screen.
/// </summary>
public class ReadableItem : MonoBehaviour
{
    [Tooltip("Extra world-unit margin added to the player's bounds when checking adjacency.")]
    public float adjacencyMargin = 0.1f;

    [Tooltip("The text shown when the player interacts with this item.")]
    [TextArea(3, 10)]
    public string description = "It's a thing.";

    [Tooltip("Font size used for this item's description in the bottom text bar.")]
    [Range(8, 64)] public int fontSize = 20;

    SpriteRenderer sr;
    Collider2D col;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
    }

    public bool IsAdjacentTo(Bounds playerBounds)
    {
        // Prefer the sprite's bounds for adjacency; fall back to the collider
        // for invisible signs/triggers that don't have a SpriteRenderer.
        Bounds itemBounds;
        if (sr != null) itemBounds = sr.bounds;
        else if (col != null) itemBounds = col.bounds;
        else return false;

        playerBounds.Expand(adjacencyMargin * 2f);
        return playerBounds.Intersects(itemBounds);
    }
}
