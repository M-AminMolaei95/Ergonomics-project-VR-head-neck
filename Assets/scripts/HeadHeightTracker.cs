using UnityEngine;

public class HeadHeightTracker : MonoBehaviour
{
    public Transform hmd;
    public float headHeight;
    public float baselineHeight;
    public float normalizedHeight;
    public bool calibrated = false;

    public void SetBaselineHeight(float value)
    {
        baselineHeight = value;
        calibrated = true;
        Debug.Log($"Baseline head height set: {baselineHeight:F3}");
    }

    void Update()
    {
        if (!calibrated) return;
        headHeight = hmd.position.y;
        normalizedHeight = headHeight - baselineHeight;
    }
}
