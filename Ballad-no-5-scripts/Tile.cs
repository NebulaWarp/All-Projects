using UnityEngine;

public class Tile : MonoBehaviour
{
    public int column;
    public int durationBeats = 1;
    public float visualHeight;
    public SpriteRenderer spriteRenderer; // cached so the fade step is allocation-free
    public SpriteRenderer outlineRenderer; // thin glowing white outline, faded alongside spriteRenderer
    public Color bodyColor;      // base body color (used as the fade target)
    public Color outlineColor;   // base outline color (used as the fade target)

    // Hold-tile runtime state
    public bool grabbed;     // key pressed while overlapping hit line
    public bool spent;       // resolved (hit or miss) — ignore for further input/scoring
    public float grabTime;

    // Catch-fade state (set when the player successfully catches this tile)
    public bool caught;
    public float caughtTime;
    // Alpha at the moment the hold ended (key released or post-natural-end auto-end).
    // The post-release fade interpolates from this down to 0, so a tile that was
    // already partly translucent from being held doesn't snap back to full opacity
    // before fading out. Defaults to 1 — TryGrab sets it explicitly on hold start.
    public float releaseAlpha = 1f;

    // Press-feedback WIDTH scale (1 = full width). Eased toward a slightly smaller
    // value once the tile is caught so it reads as "pressed". The tile's height (its
    // length) is never changed — only the parent transform's X scale.
    public float pressFactor = 1f;

    // True once the catch is irrevocable: a tap tile got tapped, OR a hold tile
    // got held for >= 1 beat, OR a hold tile's player was still holding the key
    // when the tile fell completely off-screen. While catchSecured is false on a
    // grabbed-but-resolved tile, a miss will still be registered when the tile
    // finally leaves the screen — but the miss is DEFERRED until the off-screen
    // moment, so a fast-falling tile that exits before the player could hold for
    // a full beat doesn't unfairly punish them as long as they keep holding.
    public bool catchSecured;

    // Set true the moment RegisterMiss has been called for this tile. Prevents
    // double-counting between the off-screen detection frame and the eventual
    // destruction / fade-out frame.
    public bool missFired;

    // Miss "dissolve": set true once an uncaught tile has fallen past the point where
    // it could still be hit. From then it flashes a muted red and fades out as it
    // falls off the bottom of the screen. dissolveStart anchors the brief red flash.
    public bool dissolving;
    public float dissolveStart;

    public void Fall(float deltaY)
    {
        transform.position += new Vector3(0f, -deltaY, 0f);
    }
}