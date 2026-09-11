using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal; // Light2D — needs the URP package

/// <summary>
/// Singleton that "spotlights" the room the player is in: while the player is
/// inside a room, EVERY Light2D on the map is dimmed except the ones that belong
/// to that room (its RoomArea.roomLights) and any lights in <see cref="alwaysLit"/>.
/// Leaving the room fades everything back to its normal intensity.
///
/// Replaces the old SpriteMask black-overlay approach. The class name and the
/// Freeze/Unfreeze API are kept so GameDirector and the existing scene component
/// reference keep working — leave this component where it is; the old black
/// overlay sprite (if still on this GameObject) is hidden automatically.
/// </summary>
public class RoomDarknessOverlay : MonoBehaviour
{
    public static RoomDarknessOverlay Instance { get; private set; }

    [Tooltip("Brightness of every light OUTSIDE the room you're in, as a fraction " +
             "of its normal intensity, while you're inside a room. 0 = the rest of " +
             "the map goes fully dark; higher keeps it dimly visible so it doesn't " +
             "completely disappear.")]
    [Range(0f, 1f)] public float dimFactor = 0.15f;

    [Tooltip("How much brighter the CURRENT room's lights get while the player is " +
             "inside it, as a multiple of their normal intensity — compensates for " +
             "the dimmed global light. 1 = no boost; 1.25 = 25% brighter. Applies to " +
             "every Light2D in that room's RoomArea.roomLights.")]
    [Range(1f, 3f)] public float roomLightBoost = 1.25f;

    [Tooltip("Seconds to fade to dim when entering a room.")]
    public float fadeInDuration = 0.6f;
    [Tooltip("Seconds to fade back to normal when leaving a room.")]
    public float fadeOutDuration = 0.5f;

    [Tooltip("Optional. Lights that should NEVER be dimmed — e.g. a light that " +
             "follows the player, or global effect lights. Leave empty to dim " +
             "everything that isn't part of the current room.")]
    public Light2D[] alwaysLit;

    Light2D[] sceneLights;      // every Light2D found at startup
    float[] baseIntensity;      // each light's normal (overworld) intensity
    HashSet<Light2D> alwaysSet;

    RoomArea activeRoom;        // room the player is currently inside (null = overworld)
    Coroutine fadeCo;

    bool frozen;                // while true, EnterRoom/ExitRoom are ignored (used around the minigame)
    RoomArea frozenRoom;

    void Awake()
    {
        Instance = this;

        // If this component is still sitting on the old black-overlay sprite,
        // hide that sprite — we light the scene now, we don't draw over it.
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.enabled = false;
    }

    void Start()
    {
        // Snapshot every light and its normal intensity once the scene is up.
        sceneLights = FindObjectsByType<Light2D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        baseIntensity = new float[sceneLights.Length];
        for (int i = 0; i < sceneLights.Length; i++)
            baseIntensity[i] = sceneLights[i].intensity;

        alwaysSet = new HashSet<Light2D>();
        if (alwaysLit != null)
            foreach (var l in alwaysLit) if (l != null) alwaysSet.Add(l);
    }

    /// <summary>Player entered a room: dim every light except this room's (and alwaysLit).</summary>
    public void EnterRoom(RoomArea room)
    {
        if (frozen) return;
        activeRoom = room;
        ApplyTargets(ComputeTargets(room), fadeInDuration);
    }

    /// <summary>Player left a room: fade every light back to its normal intensity.</summary>
    public void ExitRoom(RoomArea room)
    {
        if (frozen) return;
        // Ignore an exit from a room we're no longer treating as active (e.g. when
        // two room triggers overlap and we already switched to the newer one).
        if (room != null && room != activeRoom) return;
        activeRoom = null;
        ApplyTargets(ComputeTargets(null), fadeOutDuration);
    }

    // Per-light target intensity for a given active room (null = overworld, so
    // everything returns to its base intensity).
    float[] ComputeTargets(RoomArea room)
    {
        var targets = new float[sceneLights.Length];
        HashSet<Light2D> roomSet = null;
        if (room != null && room.roomLights != null)
        {
            roomSet = new HashSet<Light2D>();
            foreach (var l in room.roomLights) if (l != null) roomSet.Add(l);
        }

        for (int i = 0; i < sceneLights.Length; i++)
        {
            var light = sceneLights[i];
            if (room == null)
                // Overworld: everything back to its normal intensity.
                targets[i] = baseIntensity[i];
            else if (roomSet != null && roomSet.Contains(light))
                // The room you're in: boosted to make up for the dimmed global light.
                targets[i] = baseIntensity[i] * roomLightBoost;
            else if (alwaysSet != null && alwaysSet.Contains(light))
                // Never-dimmed lights (e.g. a player light): left at normal.
                targets[i] = baseIntensity[i];
            else
                // Everything else on the map: dimmed.
                targets[i] = baseIntensity[i] * dimFactor;
        }
        return targets;
    }

    void ApplyTargets(float[] targets, float duration)
    {
        if (fadeCo != null) StopCoroutine(fadeCo);
        fadeCo = StartCoroutine(FadeRoutine(targets, duration));
    }

    IEnumerator FadeRoutine(float[] targets, float duration)
    {
        int n = sceneLights.Length;
        var starts = new float[n];
        for (int i = 0; i < n; i++) starts[i] = sceneLights[i] != null ? sceneLights[i].intensity : 0f;

        if (duration > 0f)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / duration);
                for (int i = 0; i < n; i++)
                    if (sceneLights[i] != null) sceneLights[i].intensity = Mathf.Lerp(starts[i], targets[i], u);
                yield return null;
            }
        }
        for (int i = 0; i < n; i++)
            if (sceneLights[i] != null) sceneLights[i].intensity = targets[i];
        fadeCo = null;
    }

    // --- minigame coordination (called by GameDirector) --------------------
    // Stop reacting (and stop any in-flight fade) so the piano minigame's own
    // lighting override doesn't fight the room dimming; snap back on Unfreeze.

    public void Freeze()
    {
        if (frozen) return;
        frozen = true;
        if (fadeCo != null) { StopCoroutine(fadeCo); fadeCo = null; }
        frozenRoom = activeRoom;
    }

    public void Unfreeze()
    {
        if (!frozen) return;
        frozen = false;
        if (fadeCo != null) { StopCoroutine(fadeCo); fadeCo = null; }
        activeRoom = frozenRoom;
        // Snap (no fade) straight back to the room's lighting state.
        if (sceneLights == null) return;
        var targets = ComputeTargets(activeRoom);
        for (int i = 0; i < sceneLights.Length; i++)
            if (sceneLights[i] != null) sceneLights[i].intensity = targets[i];
    }
}
