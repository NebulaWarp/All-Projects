using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view editing for <see cref="CameraRegion"/>: select a region and drag the
/// handle dots on its box — the four CORNERS (move two edges at once) or the four
/// EDGE midpoints (move one edge) — to resize/reposition it directly in the Scene.
/// The Size field in the Inspector still works too. Edits are undoable.
/// </summary>
[CustomEditor(typeof(CameraRegion))]
[CanEditMultipleObjects]
public class CameraRegionEditor : Editor
{
    void OnSceneGUI()
    {
        var region = (CameraRegion)target;
        Transform t = region.transform;

        // Corners (sx, sy = ±1 → move both edges that meet there).
        DragControl(region, t, +1, +1);
        DragControl(region, t, -1, +1);
        DragControl(region, t, +1, -1);
        DragControl(region, t, -1, -1);
        // Edge midpoints (one axis is 0 → move only the other edge).
        DragControl(region, t, +1, 0);
        DragControl(region, t, -1, 0);
        DragControl(region, t, 0, +1);
        DragControl(region, t, 0, -1);
    }

    // Draws one draggable dot at the box side/corner picked by (sx, sy) and, on drag,
    // moves the corresponding edge(s). sx/sy: +1 = max edge, -1 = min edge, 0 = centered
    // on that axis (so it isn't edited).
    static void DragControl(CameraRegion region, Transform t, int sx, int sy)
    {
        float z = t.position.z;
        float minX = t.position.x - region.size.x * 0.5f, maxX = t.position.x + region.size.x * 0.5f;
        float minY = t.position.y - region.size.y * 0.5f, maxY = t.position.y + region.size.y * 0.5f;

        float px = sx > 0 ? maxX : sx < 0 ? minX : (minX + maxX) * 0.5f;
        float py = sy > 0 ? maxY : sy < 0 ? minY : (minY + maxY) * 0.5f;
        Vector3 pos = new Vector3(px, py, z);

        float hs = HandleUtility.GetHandleSize(pos) * 0.08f;
        EditorGUI.BeginChangeCheck();
        Vector3 np = Handles.FreeMoveHandle(pos, hs, Vector3.zero, Handles.DotHandleCap);
        if (!EditorGUI.EndChangeCheck()) return;

        if (sx > 0) maxX = np.x; else if (sx < 0) minX = np.x;
        if (sy > 0) maxY = np.y; else if (sy < 0) minY = np.y;
        // Allow dragging an edge past its opposite (the box just flips, no negative size).
        if (minX > maxX) (minX, maxX) = (maxX, minX);
        if (minY > maxY) (minY, maxY) = (maxY, minY);

        Undo.RecordObject(t, "Edit Camera Region");
        Undo.RecordObject(region, "Edit Camera Region");
        t.position = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, z);
        region.size = new Vector2(Mathf.Max(0.1f, maxX - minX), Mathf.Max(0.1f, maxY - minY));
        EditorUtility.SetDirty(region);
    }
}
