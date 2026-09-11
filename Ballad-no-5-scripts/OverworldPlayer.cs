using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(Animator))]
public class OverworldPlayer : MonoBehaviour
{
    [Tooltip("World units per second.")]
    public float moveSpeed = 4f;

    [Tooltip("World units per second while holding Shift to sprint. The walk " +
             "animation is sped up proportionally (sprintSpeed / moveSpeed) so the " +
             "feet keep pace with the faster movement.")]
    public float sprintSpeed = 7f;

    [HideInInspector] public bool movementLocked = false; // set by GameDirector during dialogue choices

    Rigidbody2D rb;
    Animator animator;
    Vector2 moveInput;
    bool sprinting;            // a Shift key held while actually moving
    Vector2 lastFacing = new Vector2(0f, -1f); // default: facing down

    /// <summary>Current facing direction (the way the player is/was last walking).
    /// Used by GameDirector to glide the room camera along the entry path.</summary>
    public Vector2 Facing => lastFacing;

    // Configurable overworld movement/sprint binds, pushed by GameDirector from the
    // saved (and in-menu editable) values via SetOverworldBinds. Defaults mirror
    // GameDirector.DefaultOverworldKeyBinds so the player still works in a scene
    // without a GameDirector.
    KeyCode bindForward  = KeyCode.W;
    KeyCode bindBackward = KeyCode.S;
    KeyCode bindRight    = KeyCode.D;
    KeyCode bindLeft     = KeyCode.A;
    KeyCode bindSprint   = KeyCode.LeftShift;

    // Movement keys still held, in the order they were pressed. The first entry
    // drives the walk animation, so the direction you pressed first is the one the
    // sprite faces — even while moving diagonally.
    readonly List<KeyCode> heldOrder = new List<KeyCode>();
    // The four bound movement keys PLUS the always-on arrow-key alternates (arrows
    // stay hard-wired to forward/back/right/left regardless of the binds). Rebuilt
    // whenever the binds change.
    KeyCode[] movementKeys;

    /// <summary>Set by GameDirector from the saved / in-menu-edited overworld keybinds.</summary>
    public void SetOverworldBinds(KeyCode forward, KeyCode backward, KeyCode right, KeyCode left, KeyCode sprint)
    {
        bindForward = forward; bindBackward = backward; bindRight = right; bindLeft = left; bindSprint = sprint;
        RebuildMovementKeys();
        heldOrder.Clear(); // drop stale held keys so a mid-play rebind leaves no ghost
    }

    void RebuildMovementKeys()
    {
        movementKeys = new[]
        {
            bindForward, bindBackward, bindRight, bindLeft,
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.RightArrow, KeyCode.LeftArrow,
        };
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;       // top-down: no gravity
        rb.freezeRotation = true;   // don't spin from collisions
        // Smooth the rendered transform between the ~50 Hz physics steps and the
        // (faster) render framerate. Without this, the body's transform only moves
        // on physics ticks, so a camera that follows it every frame stutters.
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        // Continuous detection so a fast (sprinting) step can't briefly sink into a
        // wall and trigger a hard depenetration shove. Cheap for one dynamic body.
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // Round the collider's corners WITHOUT changing its footprint: shrink the box
        // by the radius on each side, then re-add that radius as a rounded edge, so the
        // outer extents are unchanged and only the corners are clipped. Sharp box
        // corners catch on the seams between the many separate tile/box wall colliders
        // (the walls have no CompositeCollider2D merging them), which is what makes the
        // player snag and stop while hugging a wall — rounded corners glide across those
        // seams instead. Guarded so it only applies once (and respects a radius already
        // set in the Inspector).
        var box = GetComponent<BoxCollider2D>();
        if (box != null && box.edgeRadius <= 0f)
        {
            const float r = 0.06f;
            box.size = new Vector2(
                Mathf.Max(0.05f, box.size.x - 2f * r),
                Mathf.Max(0.05f, box.size.y - 2f * r));
            box.edgeRadius = r;
        }

        animator = GetComponent<Animator>();
        RebuildMovementKeys(); // in case there's no GameDirector to push binds
    }

    void Update()
    {
        // When something else (e.g. a dialogue choice) locks movement, stop reading
        // input AND zero out any motion so the player doesn't keep gliding.
        if (movementLocked)
        {
            moveInput = Vector2.zero;
            sprinting = false;
            heldOrder.Clear();
            if (animator != null) animator.speed = 0f;
            return;
        }

        // Track press/release order for the four movement keys.
        for (int i = 0; i < movementKeys.Length; i++)
        {
            var key = movementKeys[i];
            if (Input.GetKeyDown(key) && !heldOrder.Contains(key)) heldOrder.Add(key);
            if (Input.GetKeyUp(key)) heldOrder.Remove(key);
        }

        // Read input (all four directions; vector-normalized below so diagonals
        // don't exceed unit length).
        float h = 0f, v = 0f;
        if (Input.GetKey(bindForward)  || Input.GetKey(KeyCode.UpArrow))    v += 1f;
        if (Input.GetKey(bindBackward) || Input.GetKey(KeyCode.DownArrow))  v -= 1f;
        if (Input.GetKey(bindRight)    || Input.GetKey(KeyCode.RightArrow)) h += 1f;
        if (Input.GetKey(bindLeft)     || Input.GetKey(KeyCode.LeftArrow))  h -= 1f;

        moveInput = new Vector2(h, v);
        if (moveInput.sqrMagnitude > 1f) moveInput.Normalize(); // no diagonal speed boost

        bool moving = moveInput.sqrMagnitude > 0.01f;

        // Sprint while the sprint key is held and we're actually moving.
        sprinting = moving && SprintHeld();

        // Facing direction = the first movement key still being held.
        // (Falls back to the dominant axis of moveInput if nothing is in heldOrder,
        // which covers e.g. focus loss that swallowed a KeyDown.)
        if (moving)
        {
            if (heldOrder.Count > 0)
            {
                lastFacing = DirOf(heldOrder[0]);
            }
            else
            {
                if (Mathf.Abs(moveInput.x) >= Mathf.Abs(moveInput.y))
                    lastFacing = new Vector2(Mathf.Sign(moveInput.x), 0f);
                else
                    lastFacing = new Vector2(0f, Mathf.Sign(moveInput.y));
            }
        }

        // Drive the Animator
        animator.SetFloat("MoveX", lastFacing.x);
        animator.SetFloat("MoveY", lastFacing.y);
        // Freeze on the current frame when idle; otherwise play the walk cycle, sped
        // up proportionally while sprinting so the feet keep pace (no foot-sliding).
        float walkAnimSpeed = sprinting ? sprintSpeed / Mathf.Max(0.01f, moveSpeed) : 1f;
        animator.speed = moving ? walkAnimSpeed : 0f;
    }

    void FixedUpdate()
    {
        // Apply movement through the physics step so colliders block the player.
        rb.linearVelocity = moveInput * (sprinting ? sprintSpeed : moveSpeed);
    }

    // Direction a held movement key represents, honoring the current binds and the
    // fixed arrow alternates. (First match wins if a key is bound to two actions.)
    Vector2 DirOf(KeyCode k)
    {
        if (k == bindForward  || k == KeyCode.UpArrow)    return new Vector2(0f, 1f);
        if (k == bindBackward || k == KeyCode.DownArrow)  return new Vector2(0f, -1f);
        if (k == bindRight    || k == KeyCode.RightArrow) return new Vector2(1f, 0f);
        if (k == bindLeft     || k == KeyCode.LeftArrow)  return new Vector2(-1f, 0f);
        return new Vector2(0f, -1f);
    }

    // True while the sprint bind is held. The two Shift keys are treated as one
    // "Shift" so the default works with either hand; any other bind matches exactly.
    bool SprintHeld()
    {
        if (bindSprint == KeyCode.LeftShift || bindSprint == KeyCode.RightShift)
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        return Input.GetKey(bindSprint);
    }
}
