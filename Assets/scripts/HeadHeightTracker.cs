using UnityEngine;

public class HeadHeightTracker : MonoBehaviour
{
    public Transform headTransform;

    [Header("Runtime")]
    public float currentHeight;

    [Header("Baseline")]
    public float baselineHeight;
    public float normalizedHeight;

    private bool baselineSet = false;

    void Update()
    {
        if (headTransform == null) return;

        currentHeight = headTransform.position.y;

        if (!baselineSet) return;

        normalizedHeight = currentHeight - baselineHeight;
    }

    public void SetBaselineFromCurrent()
    {
        if (headTransform == null) return;
        baselineHeight = headTransform.position.y;
        baselineSet = true;
    }

    public void SetBaselineHeight(float h)
    {
        baselineHeight = h;
        baselineSet = true;
    }
}
