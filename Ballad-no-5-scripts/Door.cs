using System.Collections;
using UnityEngine;

/// <summary>
/// A door that animates between closed and open by swapping through a sequence of
/// sprite frames — no Animator Controller needed. Put this on a GameObject with a
/// SpriteRenderer (the door leaf), assign the frames (closed → open), and give it
/// a TRIGGER Collider2D for the "you're standing here" zone.
///
/// Default behaviour: while the player is inside that trigger box, pressing SPACE
/// opens (or closes) the door and plays the animation. Switch <see cref="openMode"/>
/// to Auto for the old open-on-approach behaviour.
///
/// A door painted into a Tilemap can't animate on its own (a tile isn't a
/// GameObject), so: erase the door cell from the Tilemap and place this GameObject
/// there instead, matching the surrounding Sorting Layer / Order in Layer.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class Door : MonoBehaviour
{
    public enum OpenMode { PressSpace, Auto }

    [Header("Frames")]
    [Tooltip("Door sprites in order from CLOSED (element 0) to fully OPEN (last " +
             "element). Use the sliced frames from a Modern Interiors animated_door_* sheet.")]
    public Sprite[] frames;
    [Tooltip("Playback speed of the open/close animation, in frames per second.")]
    public float framesPerSecond = 12f;

    [Header("Blocking")]
    [Tooltip("Optional. Collider that blocks the player while the door is closed. " +
             "It's disabled the moment the door starts opening and re-enabled once " +
             "the door has finished closing. Leave empty if the doorway isn't " +
             "physically blocked. Must be a SEPARATE, non-trigger collider from the " +
             "trigger box that detects the player.")]
    public Collider2D blockingCollider;

    [Header("Interaction")]
    [Tooltip("Auto (default): the door opens as soon as the player steps into this " +
             "object's trigger box and closes when they leave. Press Space: instead, " +
             "the player presses the Interact Key while standing in the trigger to " +
             "open/close it.")]
    public OpenMode openMode = OpenMode.Auto;
    [Tooltip("Press-Space mode only: fallback key that opens/closes the door when " +
             "there's no GameDirector in the scene. Normally the global, rebindable " +
             "Interact key (Settings ▸ Overworld Keybinds) is used instead.")]
    public KeyCode interactKey = KeyCode.Space;
    [Tooltip("Press-Space mode only: also close the door automatically when the " +
             "player walks out of the trigger box. Turn off to leave it open until " +
             "SPACE is pressed again.")]
    public bool closeWhenPlayerLeaves = true;

    SpriteRenderer sr;
    Coroutine anim;
    int frameIndex;   // current frame (0 = closed)
    bool isOpen;
    bool playerInside;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (frames != null && frames.Length > 0)
        {
            frameIndex = 0;
            sr.sprite = frames[0]; // start closed
        }
    }

    void Update()
    {
        // Press-Space mode: while the player is standing in the trigger box, the
        // interact key toggles the door (and plays the open/close animation).
        // Use the global, rebindable Interact key when a GameDirector is present (so
        // the Overworld Keybinds menu drives doors too); fall back to the per-door
        // interactKey otherwise.
        KeyCode openKey = (GameDirector.Instance != null) ? GameDirector.Instance.InteractKey : interactKey;
        if (openMode == OpenMode.PressSpace && playerInside && Input.GetKeyDown(openKey))
        {
            // Don't steal the Interact press from an active NPC conversation — that
            // press should advance the dialogue, not toggle the door.
            if (GameDirector.Instance != null && GameDirector.Instance.InNpcDialogue) return;
            Toggle();
        }
    }

    /// <summary>Animate to the fully-open frame and stop blocking.</summary>
    public void Open()
    {
        if (isOpen) return;
        isOpen = true;
        // Stop blocking right away so the player isn't caught on a half-open leaf.
        if (blockingCollider != null) blockingCollider.enabled = false;
        PlayTo((frames != null && frames.Length > 0) ? frames.Length - 1 : 0);
    }

    /// <summary>Animate back to the closed frame; re-blocks once fully shut.</summary>
    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        PlayTo(0);
    }

    public void Toggle()
    {
        if (isOpen) Close(); else Open();
    }

    void PlayTo(int target)
    {
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(AnimateTo(target));
    }

    IEnumerator AnimateTo(int target)
    {
        if (frames == null || frames.Length == 0) yield break;
        target = Mathf.Clamp(target, 0, frames.Length - 1);
        float step = 1f / Mathf.Max(0.01f, framesPerSecond);
        int dir = target > frameIndex ? 1 : -1;

        while (frameIndex != target)
        {
            frameIndex += dir;
            sr.sprite = frames[frameIndex];
            yield return new WaitForSeconds(step);
        }

        // Finished closing → restore the blocker so the door is solid again.
        if (target == 0 && blockingCollider != null) blockingCollider.enabled = true;
        anim = null;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<OverworldPlayer>() == null) return;
        playerInside = true;
        if (openMode == OpenMode.Auto) Open();
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponent<OverworldPlayer>() == null) return;
        playerInside = false;
        // Auto mode always closes on exit; Press-Space mode closes only if asked to.
        if (openMode == OpenMode.Auto || closeWhenPlayerLeaves) Close();
    }
}
