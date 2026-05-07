using System;
using System.IO;
using UnityEngine;

public class VRPostureLogger : MonoBehaviour
{
    [Header("References")]
    public PoseManager1 poseManager;

    [Header("Scoring - Lateral (Roll)")]
    public float lateral1 = 8f;
    public float lateral2 = 15f;
    public float lateral3 = 30f;

    [Header("Scoring - Pitch (Forward/Backward)")]
    public float pitch1 = 10f;
    public float pitch2 = 20f;

    [Header("Scoring - Slouch Duration (Seconds)")]
    public float slouchShort = 1f;
    public float slouchMedium = 4f;

    [Header("Logging")]
    public bool flushEveryFrame = false;

    private string frameLogPath;
    private string eventLogPath;

    private StreamWriter frameWriter;
    private StreamWriter eventWriter;

    private bool isLogging = false;
    private bool frameLoggingEnabled = false;

    private float slouchTimer = 0f;

    private string currentPhase = "None";
    private string currentCondition = "None";
    private int currentBlockIndex = 0;
    private int correctTargetPulse = 0;

    // Matches the latest SetManager_targetsAndColors.cs
    private string currentTaskSequenceSource = "Manual";
    private string currentTaskPreset = "None";
    private string currentTaskOrder = "";

    private bool breakTimerRunning = false;
    private float breakStartRealtime = -1f;
    private string breakStartTimestamp = "";
    private int breakStartBlockIndex = 0;

    public void StartLogging()
    {
        if (isLogging) return;

        if (poseManager == null)
        {
            Debug.LogError("VRPostureLogger: poseManager is not assigned.");
            return;
        }

        OpenWritersIfNeeded();
        if (frameWriter == null || eventWriter == null)
            return;

        isLogging = true;
        frameLoggingEnabled = false;

        ResetBreakTimerState();
        slouchTimer = 0f;
        currentPhase = "None";
        currentCondition = "None";
        currentBlockIndex = 0;
        correctTargetPulse = 0;

        LogEvent("PROTOCOL_BEGIN", "");
    }

    public void StopAndSave()
    {
        if (!isLogging) return;

        if (breakTimerRunning)
            EndBreakTimer("PROTOCOL_END_WHILE_BREAKING");

        LogEvent("PROTOCOL_END", "");
        CloseWriters();
        isLogging = false;
        frameLoggingEnabled = false;
    }

    public void PauseFrameLogging(string reasonLabel = "FRAMELOG_PAUSE")
    {
        if (!isLogging) return;
        frameLoggingEnabled = false;
        LogEvent(reasonLabel, "");
    }

    public void ResumeFrameLogging(string reasonLabel = "FRAMELOG_RESUME")
    {
        if (!isLogging) return;
        frameLoggingEnabled = true;
        LogEvent(reasonLabel, "");
    }

    public void SetPhase(string phaseName)
    {
        if (!isLogging) return;

        currentPhase = phaseName;
        LogEvent("PHASE_SET", phaseName);

        if (phaseName == "BASELINE_END")
        {
            if (poseManager != null && poseManager.IsCalibrated)
            {
                LogEvent(
                    "BASELINE_REFERENCE",
                    $"condition={currentCondition} | baselinePitch={poseManager.BaselinePitch:F3} | baselineRoll={poseManager.BaselineRoll:F3} | baselineHeight={poseManager.BaselineHeight:F3}"
                );
            }
            else
            {
                LogEvent(
                    "BASELINE_REFERENCE",
                    $"condition={currentCondition} | baselinePitch=NA | baselineRoll=NA | baselineHeight=NA"
                );
            }
        }

        WriteFrameSnapshot();
    }

    // Signature updated to match latest SetManager_targetsAndColors.cs
    public void SetTaskSequenceMeta(string sequenceSource, string presetName, string orderText)
    {
        currentTaskSequenceSource = sequenceSource;
        currentTaskPreset = presetName;
        currentTaskOrder = orderText;

        LogEvent(
            "TASK_SEQUENCE_META",
            $"source={currentTaskSequenceSource} | preset={currentTaskPreset} | order={currentTaskOrder}"
        );
    }

    // Main signature used by latest SetManager_targetsAndColors.cs
    public void SetCondition(string conditionName, string postureMode, bool feedbackOn, string presetSlot)
    {
        currentCondition = BuildConditionLabel(postureMode, feedbackOn, presetSlot);
        currentBlockIndex = 0;
        correctTargetPulse = 0;

        ResetBreakTimerState();

        LogEvent(
            "CONDITION_META",
            $"rawCondition={conditionName} | condition={currentCondition} | source={currentTaskSequenceSource} | preset={currentTaskPreset} | slot={presetSlot} | feedbackOn={(feedbackOn ? 1 : 0)}"
        );
    }

    // Backward-compatible overload in case another SetManager version is used later.
    public void SetCondition(string conditionName, string postureMode, string presetSlot)
    {
        bool inferredFeedbackOn = presetSlot != "XA" && presetSlot != "YA" && presetSlot != "None";
        SetCondition(conditionName, postureMode, inferredFeedbackOn, presetSlot);
    }

    public void SetBlockIndex(int blockIndex)
    {
        currentBlockIndex = blockIndex;
    }

    public void LogTrial(int correctCount)
    {
        LogEvent("TRIAL_PROGRESS", $"correct={correctCount}");
    }

    public void MarkCorrectTarget()
    {
        correctTargetPulse = 1;
    }

    public void LogEvent(string label, string info)
    {
        if (!isLogging) return;

        OpenWritersIfNeeded();
        if (eventWriter == null)
            return;

        string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        eventWriter.WriteLine($"{ts},{label},{info}");
        if (flushEveryFrame) eventWriter.Flush();
    }

    public void BeginBreakTimer(string reason = "BLOCK_BREAK")
    {
        if (!isLogging) return;
        if (breakTimerRunning) return;

        breakTimerRunning = true;
        breakStartRealtime = Time.realtimeSinceStartup;
        breakStartTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        breakStartBlockIndex = currentBlockIndex;

        LogEvent(
            "BREAK_START",
            $"condition={currentCondition} | block={breakStartBlockIndex} | phase={currentPhase} | reason={reason}"
        );
    }

    public void EndBreakTimer(string reason = "BLOCK_RESUME")
    {
        if (!isLogging) return;
        if (!breakTimerRunning) return;

        float durationSec = Mathf.Max(0f, Time.realtimeSinceStartup - breakStartRealtime);
        string endTs = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

        LogEvent(
            "BREAK_END",
            $"condition={currentCondition} | block={breakStartBlockIndex} | phase={currentPhase} | reason={reason} | durationSec={durationSec:F3} | breakStart={breakStartTimestamp} | breakEnd={endTs}"
        );

        breakTimerRunning = false;
        breakStartRealtime = -1f;
        breakStartTimestamp = "";
        breakStartBlockIndex = 0;
    }

    private void Update()
    {
        if (!isLogging) return;
        if (!frameLoggingEnabled) return;
        if (poseManager == null) return;

        WriteFrameSnapshot();
        correctTargetPulse = 0;
    }

    private void WriteFrameSnapshot()
    {
        if (poseManager == null) return;

        bool calibrated = poseManager.IsCalibrated;

        float pitch;
        float roll;
        float normalizedHeight;
        bool isSlouching;

        if (calibrated)
        {
            pitch = poseManager.currentPitch;
            roll = poseManager.currentRoll;
            normalizedHeight = poseManager.normalizedHeight;
            isSlouching = poseManager.isSlouching;
        }
        else
        {
            pitch = poseManager.normalizedPitch;
            roll = poseManager.normalizedRoll;
            normalizedHeight = 0f;
            isSlouching = false;
        }

        if (frameLoggingEnabled)
        {
            if (calibrated && isSlouching)
                slouchTimer += Time.deltaTime;
            else
                slouchTimer = 0f;
        }

        int lateralScore = calibrated ? GetLateralScore(Mathf.Abs(roll)) : 0;
        int pitchScore = calibrated ? GetPitchScore(Mathf.Abs(pitch)) : 0;
        int slouchScore = calibrated ? GetSlouchScore(slouchTimer) : 0;
        int totalScore = lateralScore + pitchScore + slouchScore;

        OpenWritersIfNeeded();
        if (frameWriter == null)
            return;

        string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

        frameWriter.WriteLine(
            $"{ts}," +
            $"{currentPhase}," +
            $"{currentCondition}," +
            $"{currentBlockIndex}," +
            $"{correctTargetPulse}," +
            $"{pitch:F3}," +
            $"{roll:F3}," +
            $"{(isSlouching ? 1 : 0)}," +
            $"{slouchTimer:F3}," +
            $"{normalizedHeight:F3}," +
            $"{lateralScore}," +
            $"{pitchScore}," +
            $"{slouchScore}," +
            $"{totalScore}"
        );

        if (flushEveryFrame) frameWriter.Flush();
    }

    private void OpenWritersIfNeeded()
    {
        if (frameWriter != null && eventWriter != null) return;

        string folder = @"C:\Users\munhciteam\Documents\Projects\amin projects\LATEST-ADV-COURSEWORK\Assets\PostureLogs";

        try
        {
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
        }
        catch (Exception e)
        {
            Debug.LogError($"VRPostureLogger: Cannot create/access log folder:\n{folder}\n{e}");
            return;
        }

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        frameLogPath = Path.Combine(folder, $"frame_{stamp}.csv");
        eventLogPath = Path.Combine(folder, $"events_{stamp}.csv");

        try
        {
            frameWriter = new StreamWriter(frameLogPath, false, new System.Text.UTF8Encoding(true));
            eventWriter = new StreamWriter(eventLogPath, false, new System.Text.UTF8Encoding(true));

            frameWriter.WriteLine(
                "timestamp,phase,condition,blockIndex,correctTarget,pitch,roll,isSlouching,slouchTimer,normalizedHeight,lateralScore,pitchScore,slouchScore,totalScore"
            );
            eventWriter.WriteLine("timestamp,label,info");

            frameWriter.Flush();
            eventWriter.Flush();
        }
        catch (Exception e)
        {
            Debug.LogError($"VRPostureLogger: Failed to open/write log files:\n{frameLogPath}\n{eventLogPath}\n{e}");
            CloseWritersSafely();
        }
    }

    private void CloseWritersSafely()
    {
        try { frameWriter?.Flush(); } catch { }
        try { eventWriter?.Flush(); } catch { }

        try { frameWriter?.Close(); } catch { }
        try { eventWriter?.Close(); } catch { }

        frameWriter = null;
        eventWriter = null;
    }

    private void CloseWriters()
    {
        try { frameWriter?.Flush(); } catch { }
        try { eventWriter?.Flush(); } catch { }

        try { frameWriter?.Close(); } catch { }
        try { eventWriter?.Close(); } catch { }

        frameWriter = null;
        eventWriter = null;
    }

    private void ResetBreakTimerState()
    {
        breakTimerRunning = false;
        breakStartRealtime = -1f;
        breakStartTimestamp = "";
        breakStartBlockIndex = 0;
    }

    private string BuildConditionLabel(string postureMode, bool feedbackOn, string presetSlot)
    {
        string posture = postureMode == "Standing" ? "standing" : "sitting";
        string feedback;

        if (!feedbackOn)
        {
            feedback = "none";
        }
        else
        {
            switch (presetSlot)
            {
                case "XB":
                case "YB":
                    feedback = "partial";
                    break;

                case "XC":
                case "YC":
                    feedback = "full";
                    break;

                default:
                    feedback = "on";
                    break;
            }
        }

        return $"{posture} {feedback}";
    }

    private int GetLateralScore(float absRoll)
    {
        if (absRoll <= lateral1) return 1;
        if (absRoll <= lateral2) return 2;
        if (absRoll <= lateral3) return 3;
        return 4;
    }

    private int GetPitchScore(float absPitch)
    {
        if (absPitch <= pitch1) return 1;
        if (absPitch <= pitch2) return 3;
        return 4;
    }

    private int GetSlouchScore(float duration)
    {
        if (duration <= slouchShort) return 0;
        if (duration < 2f) return 1;
        if (duration <= slouchMedium) return 3;
        return 5;
    }

    private void OnApplicationQuit()
    {
        try { StopAndSave(); } catch { }
    }

    private void OnDisable()
    {
        try { StopAndSave(); } catch { }
    }
}
