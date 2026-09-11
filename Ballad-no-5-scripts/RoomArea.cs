using UnityEngine;
using UnityEngine.Rendering.Universal; // Light2D — needs the URP package

/// <summary>
/// One per room. Attach to a GameObject with a trigger Collider2D sized to the
/// room interior — that collider IS the room's size, so resize it to control how
/// big the room is. When the player walks in, every light on the map dims except
/// this room's own Light2D(s); on exit everything fades back to normal.
/// </summary>
public class RoomArea : MonoBehaviour
{
    [Tooltip("The Light2D(s) that belong to THIS room. While the player is inside, " +
             "these stay at full brightness and every other light on the map dims " +
             "(by RoomDarknessOverlay.dimFactor). Leave empty if you want the whole " +
             "map — including this room — to dim when the player is inside.")]
    public Light2D[] roomLights;

    Collider2D areaTrigger;
    bool playerInside;

    /// <summary>True while the player is inside this room's trigger.</summary>
    public bool PlayerInside => playerInside;

    void Awake()
    {
        areaTrigger = GetComponent<Collider2D>();
        if (areaTrigger != null && !areaTrigger.isTrigger)
            Debug.LogWarning($"[RoomArea] {name}: the room's collider should have Is Trigger = true.", this);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<OverworldPlayer>() == null) return;
        if (playerInside) return;
        playerInside = true;

        if (RoomDarknessOverlay.Instance != null)
            RoomDarknessOverlay.Instance.EnterRoom(this);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponent<OverworldPlayer>() == null) return;
        if (!playerInside) return;
        playerInside = false;

        if (RoomDarknessOverlay.Instance != null)
            RoomDarknessOverlay.Instance.ExitRoom(this);
    }
}
