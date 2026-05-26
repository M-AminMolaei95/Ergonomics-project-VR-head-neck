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

    private int currentViewArc = 0;
    private int currentViewArcSegment = 0;
    private int currentBlockInViewArc = 0;
    private int currentBlocksPerViewArc = 0;
    private int currentConditionTotalBlocks = 0;

    private int currentTargetTemplateIndex = 0;
    private string currentTargetTemplateName = "None";
    private int currentTargetBallOneBased = 0;

    private int currentPatternIndex = 0;
    private string currentPatternName = "None";
    private int currentTrialInPattern = 0;

    private int correctTargetPulse = 0;

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
        ResetTaskDetailMeta();
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

    public void SetCondition(string conditionName, string postureMode, bool feedbackOn, string presetSlot)
    {
        currentCondition = BuildConditionLabel(postureMode, feedbackOn, presetSlot);
        currentBlockIndex = 0;
        ResetTaskDetailMeta();
        correctTargetPulse = 0;

        ResetBreakTimerState();

        LogEvent(
            "CONDITION_META",
            $"rawCondition={conditionName} | condition={currentCondition} | source={currentTaskSequenceSource} | preset={currentTaskPreset} | slot={presetSlot} | feedbackOn={(feedbackOn ? 1 : 0)}"
        );
    }

    public void SetCondition(string conditionName, string postureMode, string presetSlot)
    {
        bool inferredFeedbackOn = presetSlot != "XA" && presetSlot != "YA" && presetSlot != "None";
        SetCondition(conditionName, postureMode, inferredFeedbackOn, presetSlot);
    }

    public void SetBlockIndex(int blockIndex)
    {
        currentBlockIndex = blockIndex;
    }

    public void SetViewArcMeta(int viewArc, int viewArcSegment, int blockInViewArc, int blocksPerViewArc, int conditionTotalBlocks)
    {
        currentViewArc = Mathf.Max(0, viewArc);
        currentViewArcSegment = Mathf.Max(0, viewArcSegment);
        currentBlockInViewArc = Mathf.Max(0, blockInViewArc);
        currentBlocksPerViewArc = Mathf.Max(0, blocksPerViewArc);
        currentConditionTotalBlocks = Mathf.Max(0, conditionTotalBlocks);
    }

    public void SetTargetTemplateMeta(int templateIndex, string templateName, int targetBallOneBased)
    {
        currentTargetTemplateIndex = Mathf.Max(0, templateIndex);
        currentTargetTemplateName = string.IsNullOrWhiteSpace(templateName) ? "None" : SanitizeCsvText(templateName);
        currentTargetBallOneBased = Mathf.Max(0, targetBallOneBased);
    }

    public void SetPatternTrialMeta(int patternIndex, string patternName, int trialInPattern)
    {
        currentPatternIndex = Mathf.Max(0, patternIndex);
        currentPatternName = string.IsNullOrWhiteSpace(patternName) ? "None" : SanitizeCsvText(patternName);
        currentTrialInPattern = Mathf.Max(0, trialInPattern);
    }

    private void ResetTaskDetailMeta()
    {
        currentViewArc = 0;
        currentViewArcSegment = 0;
        currentBlockInViewArc = 0;
        currentBlocksPerViewArc = 0;
        currentConditionTotalBlocks = 0;
        currentTargetTemplateIndex = 0;
        currentTargetTemplateName = "None";
        currentTargetBallOneBased = 0;
        currentPatternIndex = 0;
        currentPatternName = "None";
        currentTrialInPattern = 0;
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
        eventWriter.WriteLine($"{ts},{SanitizeCsvText(label)},{SanitizeCsvText(info)}");
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
        int slouchScore = calibrated ? GetSlouchScore(slouchTimer) : 0;

        int rulaScore = calibrated ? GetRulaScore(pitch, roll) : 0;
        string rulaReason = calibrated ? GetRulaReason(pitch, roll) : "not_calibrated";

        OpenWritersIfNeeded();
        if (frameWriter == null)
            return;

        string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

        frameWriter.WriteLine(
            $"{ts}," +
            $"{currentPhase}," +
            $"{currentCondition}," +
            $"{currentBlockIndex}," +
            $"{currentViewArc}," +
            $"{currentViewArcSegment}," +
            $"{currentBlockInViewArc}," +
            $"{currentBlocksPerViewArc}," +
            $"{currentConditionTotalBlocks}," +
            $"{currentTargetTemplateIndex}," +
            $"{currentTargetTemplateName}," +
            $"{currentTargetBallOneBased}," +
            $"{currentPatternIndex}," +
            $"{currentPatternName}," +
            $"{currentTrialInPattern}," +
            $"{correctTargetPulse}," +
            $"{pitch:F3}," +
            $"{roll:F3}," +
            $"{(isSlouching ? 1 : 0)}," +
            $"{slouchTimer:F3}," +
            $"{normalizedHeight:F3}," +
            $"{lateralScore}," +
            $"{slouchScore}," +
            $"{rulaScore}," +
            $"{SanitizeCsvText(rulaReason)}"
        );

        if (flushEveryFrame) frameWriter.Flush();
    }

    private void OpenWritersIfNeeded()
    {
        if (frameWriter != null && eventWriter != null) return;

        string folder = @"C:\Users\munhciteam\Documents\Projects\amin projects\Ergonomics project VR head-neck\Assets\PostureLogs";

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
                "timestamp,phase,condition,blockIndex,viewArc,viewArcSegment,blockInViewArc,blocksPerViewArc,conditionTotalBlocks,targetTemplateIndex,targetTemplateName,targetBallOneBased,patternIndex,patternName,trialInPattern,correctTarget,pitch,roll,isSlouching,slouchTimer,normalizedHeight,lateralScore,slouchScore,rulaScore,rulaReason"
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


    private int GetRulaScore(float pitch, float roll)
    {
        int score = GetRulaBasePitchScore(pitch);

        if (Mathf.Abs(roll) > 8f)
            score += 1;

        return score;
    }

    private int GetRulaBasePitchScore(float pitch)
    {
        if (pitch >= 0f && pitch <= 10f) return 1;
        if (pitch > 10f && pitch <= 20f) return 2;
        if (pitch > 20f) return 3;
        if (pitch < 0f && pitch >= -20) return 4;

        return 5;
    }

    private string GetRulaReason(float pitch, float roll)
    {
        string pitchReason;

        if (pitch >= 0f && pitch <= 10f)
            pitchReason = "forward_0_10";
        else if (pitch > 10f && pitch <= 20f)
            pitchReason = "forward_10_20";
        else if (pitch > 20f)
            pitchReason = "forward_over_20";
        else
            pitchReason = "backward_extension";

        if (Mathf.Abs(roll) > 8f)
            return pitchReason + "+lateral_tilt_8_or_more";

        return pitchReason + "_only";
    }
    private int GetLateralScore(float absRoll)
    {
        if (absRoll <= lateral1) return 1;
        if (absRoll <= lateral2) return 2;
        if (absRoll <= lateral3) return 3;
        return 4;
    }


    private int GetSlouchScore(float duration)
    {
        if (duration <= slouchShort) return 0;
        if (duration < 2f) return 1;
        if (duration <= slouchMedium) return 3;
        return 5;
    }

    private string SanitizeCsvText(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("\r", " ").Replace("\n", " ").Replace(",", ";");
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
