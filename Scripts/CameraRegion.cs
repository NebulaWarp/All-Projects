using UnityEngine;

/// <summary>
/// A rectangular camera region. While the player is inside it, GameDirector clamps
/// the camera so its view never spills past the rectangle's edges (no blank space
/// shown outside the level). Place a GameObject, set its size, and add it to
/// GameDirector.cameraRegions. Walking out of one region and into an adjacent one
/// hands the camera off to that region; outside every region the camera follows the
/// player freely.
///
/// This is purely a camera concept — no collider, so it never interferes with
/// movement or physics. The wireframe gizmo shows its bounds in the Scene view.
/// </summary>
public class CameraRegion : MonoBehaviour
{
    [Tooltip("Size of the region in world units (width × height), centered on this GameObject.")]
    public Vector2 size = new Vector2(20f, 12f);

    /// <summary>World-space rectangle this region covers.</summary>
    public Bounds WorldBounds => new Bounds(transform.position, new Vector3(size.x, size.y, 1f));

    public bool Contains(Vector2 point)
    {
        var b = WorldBounds;
        return point.x >= b.min.x && point.x <= b.max.x
            && point.y >= b.min.y && point.y <= b.max.y;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.25f, 0.7f, 1f, 0.9f);
        Gizmos.DrawWireCube(transform.position, new Vector3(size.x, size.y, 0.01f));
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.25f, 0.7f, 1f, 0.12f);
        Gizmos.DrawCube(transform.position, new Vector3(size.x, size.y, 0.01f));

        // Reference outline of the main camera's view size, centered on this region,
        // so you can confirm the region is big enough while dragging it. If this
        // region's box doesn't fully contain the yellow outline, the camera can't be
        // confined on that axis and will center on the region instead.
        var cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            float viewH = cam.orthographicSize * 2f;
            float viewW = viewH * cam.aspect;
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            Gizmos.DrawWireCube(transform.position, new Vector3(viewW, viewH, 0.01f));
        }
    }
}
