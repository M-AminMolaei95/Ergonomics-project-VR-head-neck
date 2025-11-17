using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using System.Collections;

public class PoseManager1 : MonoBehaviour
{
    [Header("References")]
    public Transform hmd;
    public TextMeshProUGUI countdownText;

    [Header("Calibration Settings")]
    public float calibrationTime = 5f;

    [Header("Live values")]
    public float pitch;
    public float normalizedPitch;
    public float roll;
    public float normalizedRoll;
    public float normalizedHeight;

    float baselinePitch;
    float baselineRoll;
    bool calibrated = false;
    bool isCalibrating = false;

    HeadHeightTracker heightTracker;
    SetManager setManager;
    public VRPostureLogger logger;

    void Awake()
    {
        heightTracker = FindFirstObjectByType<HeadHeightTracker>();
        setManager = FindFirstObjectByType<SetManager>();
    }

    void Update()
    {
        if (!calibrated && !isCalibrating &&
            OVRInput.GetDown(OVRInput.Button.Two))
        {
            StartCalibration();
        }

        CalculatePitch();
        CalculateRoll();

        if (calibrated)
        {
            normalizedPitch = pitch - baselinePitch;
            normalizedRoll = roll - baselineRoll;
            normalizedHeight = heightTracker ? heightTracker.normalizedHeight : 0f;
        }
    }

    void CalculatePitch()
    {
        Vector3 fwd = hmd.forward;
        Vector3 horiz = Vector3.ProjectOnPlane(fwd, Vector3.up);
        pitch = Vector3.SignedAngle(horiz, fwd, hmd.right);
    }

    void CalculateRoll()
    {
        roll = Vector3.SignedAngle(hmd.up, Vector3.up, hmd.forward);
    }

    public void StartCalibration()
    {
        if (isCalibrating) return;
        StopAllCoroutines();
        StartCoroutine(CalibrationRoutine());
    }

    IEnumerator CalibrationRoutine()
    {
        isCalibrating = true;

        float timer = calibrationTime;

        float sumPitch = 0f;
        float sumHeight = 0f;
        float sumRoll = 0f;
        int samples = 0;

        if (countdownText) countdownText.gameObject.SetActive(true);

        while (timer > 0f)
        {
            if (countdownText)
                countdownText.text = Mathf.Ceil(timer).ToString();

            CalculatePitch();
            CalculateRoll();

            sumPitch += pitch;
            sumRoll += roll;
            if (heightTracker) sumHeight += heightTracker.hmd.position.y;

            samples++;

            timer -= Time.deltaTime;
            yield return null;
        }

        if (samples > 0)
        {
            baselinePitch = sumPitch / samples;
            baselineRoll = sumRoll / samples;

            if (heightTracker)
                heightTracker.SetBaselineHeight(sumHeight / samples);

            calibrated = true;
        }
        else
        {
            Debug.LogError("Calibration failed: no samples collected.");
            calibrated = false;
        }

        if (setManager)
            setManager.CaptureCameraZero();

        if (countdownText)
        {
            countdownText.text = "START TASK";
            countdownText.fontSize = 30;
        }

        yield return new WaitForSeconds(1.5f);

        if (countdownText)
            countdownText.gameObject.SetActive(false);

        if (logger != null)
            logger.StartLogging();

        isCalibrating = false;

        Debug.Log($"Baseline Pitch: {baselinePitch:F2}, Baseline Roll: {baselineRoll:F2}");
    }
}
