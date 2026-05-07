using System.Collections;
using UnityEngine;

public class PoseManager1 : MonoBehaviour
{
    [Header("References")]
    public Transform headTransform;
    public HeadHeightTracker heightTracker; // required for height baseline pipeline

    [Header("Runtime Posture (relative to baseline)")]
    public float currentPitch;
    public float currentRoll;
    public float normalizedHeight;
    public bool isSlouching;

    // Compatibility for PostureNeck fields
    public float normalizedPitch;
    public float normalizedRoll;

    [Header("Slouch Detection (simple fallback for logger)")]
    public float slouchHeightThreshold = 0.03f;   // meters (height drop)
    public float slouchPitchNeutralBand = 12f;    // degrees
    public float slouchRollLimit = 10f;           // degrees

    private float baselinePitch;
    private float baselineRoll;

    private bool calibrated = false;
    public bool IsCalibrated => calibrated;

    // For logs
    public float BaselinePitch => baselinePitch;
    public float BaselineRoll => baselineRoll;
    public float BaselineHeight => heightTracker != null ? heightTracker.baselineHeight : 0f;

    private void Update()
    {
        if (!headTransform) return;

        Vector3 localEuler = headTransform.localEulerAngles;
        float rawPitch = NormalizeAngle(localEuler.x);
        float rawRoll = NormalizeAngle(localEuler.z);

        if (!calibrated)
        {
            normalizedPitch = rawPitch;
            normalizedRoll = rawRoll;

            currentPitch = 0f;
            currentRoll = 0f;

            normalizedHeight = 0f;
            isSlouching = false;
            return;
        }

        currentPitch = rawPitch - baselinePitch;
        currentRoll = rawRoll - baselineRoll;

        normalizedPitch = currentPitch;
        normalizedRoll = currentRoll;

        if (heightTracker != null)
            normalizedHeight = heightTracker.normalizedHeight;
        else
            normalizedHeight = 0f;

        isSlouching =
            (normalizedHeight < -slouchHeightThreshold) &&
            Mathf.Abs(currentPitch) <= slouchPitchNeutralBand &&
            Mathf.Abs(currentRoll) < slouchRollLimit;
    }

    // Keep old call so SetManager or old code won't break
    public void CalibrateNow()
    {
        StartCoroutine(CalibrateBaselinePipeline(10f));
    }

    // Clean single baseline pipeline: averages pitch/roll + height over 'seconds'
    public IEnumerator CalibrateBaselinePipeline(float seconds)
    {
        if (seconds <= 0f) seconds = 1f;

        if (!headTransform)
        {
            Debug.LogError("PoseManager1: headTransform missing.");
            yield break;
        }

        if (heightTracker == null)
        {
            Debug.LogError("PoseManager1: heightTracker is not assigned (needed for height baseline).");
            yield break;
        }

        calibrated = false;

        float sumPitch = 0f, sumRoll = 0f, sumH = 0f;
        int count = 0;
        float t = 0f;

        // ensure tracker has at least one update
        yield return null;

        while (t < seconds)
        {
            Vector3 localEuler = headTransform.localEulerAngles;

            sumPitch += NormalizeAngle(localEuler.x);
            sumRoll += NormalizeAngle(localEuler.z);

            sumH += heightTracker.currentHeight; // requires HeadHeightTracker update below

            count++;
            t += Time.deltaTime;
            yield return null;
        }

        if (count <= 0) count = 1;

        baselinePitch = sumPitch / count;
        baselineRoll = sumRoll / count;

        heightTracker.SetBaselineHeight(sumH / count);

        calibrated = true;

        Debug.Log($"Calibrated AVG({seconds:F1}s) ✅ pitch0={baselinePitch:F2}, roll0={baselineRoll:F2}, height0={heightTracker.baselineHeight:F3}, n={count}");
    }

    private float NormalizeAngle(float angle)
    {
        if (angle > 180f) angle -= 360f;
        return angle;
    }
}