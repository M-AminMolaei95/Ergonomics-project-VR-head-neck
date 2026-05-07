using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

public class SetManager : MonoBehaviour
{
    public enum PostureMode { Sitting, Standing }
    public enum ConditionPresetSlot { None = 0, XA, XB, XC, YA, YB, YC }
    public enum ConditionSequencePreset { None = 0, P1, P2, P3, P4, P5, P6 }
    public enum ConditionSequencePart { Part1 = 1, Part2 = 2 }
    public enum TaskRunMode { Practice, Study }

    [System.Serializable]
    public class ConditionConfig
    {
        public string conditionName = "Sitting_NoFeedback";
        public PostureMode posture = PostureMode.Sitting;
        [Header("Preset mapping (X/Y with A/B/C task presets)")]
        public ConditionPresetSlot presetSlot = ConditionPresetSlot.None;
        public int totalCorrectSelections = 150;
        [Header("Pattern transforms (applied on positions)")]
        public float heightOffset = 0f;
        public float heightScale = 1f;
        public float spreadScale = 1f;
    }

    [System.Serializable]
    public class PatternConfig
    {
        public string patternName = "A";
        public List<Vector3> localPositions = new List<Vector3>();
    }

    [Header("Run Mode")]
    public TaskRunMode taskRunMode = TaskRunMode.Study;

    [Header("Practice Mode")]
    public int practiceCorrectSelections = 22;
    public ConditionPresetSlot practiceReferenceSlot = ConditionPresetSlot.XA;

    [Header("Conditions (define all 6: XA, XB, XC, YA, YB, YC)")]
    public List<ConditionConfig> conditions = new List<ConditionConfig>();

    [Header("Condition sequence preset")]
    public ConditionSequencePreset conditionSequencePreset = ConditionSequencePreset.P1;
    public ConditionSequencePart conditionSequencePart = ConditionSequencePart.Part1;

    [Header("Patterns A..J (order matters, but will be shuffled per set)")]
    public List<PatternConfig> patterns = new List<PatternConfig>();

    [Header("Ball setup")]
    public GameObject ballPrefab;
    public int ballsPerPattern = 10;

    [Header("Sets / trials")]
    public int patternsPerSet = 10;
    public int trialsPerPattern = 3;

    [Header("Blocks")]
    public int blocksPerSet = 3;

    [Header("Color palette")]
    public List<Color> basePalette = new List<Color>()
    {
        Color.red, Color.green, Color.blue, Color.yellow,
        new Color(1f, 0.5f, 0f),
        new Color(0.6f, 0.2f, 0.8f),
        Color.cyan, Color.magenta
    };
    public float targetBrightnessDelta = 0.25f;
    public float targetBrighterProbability = 0.5f;

    [Header("Optional references")]
    public GameObject feedbackRoot;
    public PostureNeck postureNeck;
    public VRPostureLogger logger;

    [Header("Baseline + Countdown")]
    public PoseManager1 poseManager;
    public CountdownGate countdownGate;

    [Header("Gate UI (Enter by researcher, B by participant)")]
    public TMP_Text gateText;
    public GameObject gateRoot;

    [Header("Condition Indicator (top-left small text)")]
    public TMP_Text conditionLabelText;
    public bool showConditionLabel = true;

    public bool allowKeyboardFallback = true;

    [Header("Task End UI")]
    public string taskFinishedMessage = "Task finished.\n\n(Researcher) Configure next task in Inspector,\nthen press START again.";
    [Header("Practice End UI")]
    public string practiceFinishedMessage = "Practice session is finished.";

    private readonly List<BallSelectable> balls = new List<BallSelectable>();
    private readonly List<int> activeTask = new List<int>();
    private string currentTaskSequencePresetName = "None";
    private string currentTaskSequencePartName = "Part1";
    private string currentTaskSequenceOrderText = "";
    private int currentTaskPos = 0;
    private int currentGlobalConditionIndex = -1;
    private int correctInCondition = 0;
    private int currentSetIndex = 0;
    private int currentBlockIndex = 0;
    private readonly List<int> shuffledPatternOrder = new List<int>();
    private int currentPatternPosInSet = 0;
    private int correctInCurrentPattern = 0;
    private int correctInCurrentBlock = 0;
    private int selectionsPerBlock = 1;
    private int currentTargetBallIndex = -1;
    private Color currentBaseColor = Color.white;
    private Color currentTargetColor = Color.white;
    private float trialReadyRT = 0f;

    private readonly List<int> conditionTargetSequence = new List<int>();
    private int conditionTargetPointer = 0;

    private readonly List<int> practicePatternOrder = new List<int>();
    private readonly List<int> practiceTargetSequence = new List<int>();
    private int practiceTargetPointer = 0;
    private int practicePatternPointer = 0;
    private int practiceCurrentRound = 1;

    private bool hasConditionAnchor = false;
    private Vector3 conditionAnchor = Vector3.zero;
    private Coroutine flowRoutine = null;

    private enum TaskState { Idle, WaitingPracticeStart, BaselineAndCountdown, RunningCondition, WaitingEnterBetween, WaitingSecondB, WaitingBetweenBlocks, Finished }
    private TaskState state = TaskState.Idle;
    private bool skipBaselineBWait = false;
    private bool waitingForTriggerReleaseBetweenBlocks = false;
    private bool waitingForPracticeStartBRelease = false;
    private bool practicePromptPrepared = false;

    public void BeginProtocol() => StartTask();
    public void StartExperiment() => StartTask();

    public void StartTask()
    {
        if (state != TaskState.Idle && state != TaskState.Finished) return;
        if (!ValidateSetup()) return;
        practicePromptPrepared = false;

        if (flowRoutine != null) { StopCoroutine(flowRoutine); flowRoutine = null; }
        EnsureBallPool();

        if (taskRunMode == TaskRunMode.Practice)
        {
            PreparePracticeStart();
            return;
        }

        BuildActiveTask();
        if (activeTask.Count != 3)
        {
            ShowGate("⚠️ Task config error.\n\nThis preset-part must resolve to EXACTLY 3 conditions.\n" +
                     $"Preset={conditionSequencePreset} | Part={conditionSequencePart}\nResolved count={activeTask.Count}");
            state = TaskState.Idle;
            return;
        }

        logger?.StartLogging();
        logger?.SetTaskSequenceMeta(currentTaskSequencePresetName, currentTaskSequencePartName, currentTaskSequenceOrderText);
        logger?.PauseFrameLogging("TASK_BEGIN_FRAMELOG_PAUSE");
        logger?.SetPhase("WAITING_BEFORE_BASELINE");
        logger?.LogEvent("TASK_BEGIN", $"preset={currentTaskSequencePresetName} | part={currentTaskSequencePartName} | order={currentTaskSequenceOrderText}");

        HideGate();
        currentTaskPos = 0;
        currentGlobalConditionIndex = activeTask[currentTaskPos];
        skipBaselineBWait = true;
        StartConditionFlow();
    }

    private void PreparePracticeStart()
    {
        HideConditionLabel();

        if (countdownGate != null)
            countdownGate.gameObject.SetActive(false);

        if (feedbackRoot != null)
            feedbackRoot.SetActive(false);

        foreach (var b in balls)
            b.gameObject.SetActive(false);

        ShowGate("Practice mode.\n\nPress B to start.");
        waitingForPracticeStartBRelease = true;
        state = TaskState.WaitingPracticeStart;
    }

    private void StartPracticeTask()
    {
        practicePromptPrepared = false;
        HideGate();
        HideConditionLabel();

        if (gateRoot != null) gateRoot.SetActive(false);
        if (countdownGate != null) countdownGate.gameObject.SetActive(false);
        if (feedbackRoot != null) feedbackRoot.SetActive(false);

        practicePatternOrder.Clear();
        practiceTargetSequence.Clear();
        practicePatternPointer = 0;
        practiceTargetPointer = 0;
        practiceCurrentRound = 1;

        BuildPracticePatternOrder();
        BuildPracticeTargetSequence();

        correctInCondition = 0;
        correctInCurrentPattern = 0;
        currentSetIndex = 1;
        currentBlockIndex = 1;

        foreach (var b in balls) b.gameObject.SetActive(true);

        state = TaskState.RunningCondition;
        StartPracticePattern();
    }

    public void OnBallSelected(BallSelectable selected)
    {
        if (state != TaskState.RunningCondition || selected == null) return;

        if (!selected.IsTarget)
        {
            if (taskRunMode == TaskRunMode.Study)
                logger?.LogEvent("WRONG_HIT", $"set={currentSetIndex} | block={currentBlockIndex} | ball={selected.name} | correct={correctInCondition}");
            return;
        }

        if (taskRunMode == TaskRunMode.Practice)
        {
            correctInCondition++;
            correctInCurrentPattern++;
            if (correctInCondition >= practiceCorrectSelections) { EndPracticeTask(); return; }
            AdvancePracticePattern();
            return;
        }

        float rtSec = Mathf.Max(0f, Time.realtimeSinceStartup - trialReadyRT);
        correctInCondition++;
        correctInCurrentPattern++;
        correctInCurrentBlock++;
        logger?.LogTrial(correctInCondition);
        logger?.MarkCorrectTarget();
        logger?.LogEvent("CORRECT_HIT", $"set={currentSetIndex} | block={currentBlockIndex} | rtSec={rtSec:F3} | correct={correctInCondition} | targetIndex={currentTargetBallIndex}");

        if (correctInCondition >= conditions[currentGlobalConditionIndex].totalCorrectSelections) { EndCurrentCondition(); return; }

        bool shouldBreakAfterThisHit = currentBlockIndex < blocksPerSet && correctInCurrentBlock >= selectionsPerBlock;
        if (shouldBreakAfterThisHit) { BeginBreakBetweenBlocks(); return; }

        if (correctInCurrentPattern >= trialsPerPattern) AdvancePattern();
        else PrepareNextTrialSamePattern();
    }

    private void Update()
    {
        if (state == TaskState.Idle)
        {
            if (taskRunMode == TaskRunMode.Practice)
            {
                if (!practicePromptPrepared)
                {
                    if (!ValidateSetup())
                        return;

                    EnsureBallPool();
                    PreparePracticeStart();
                    practicePromptPrepared = true;
                    return;
                }
            }
            else
            {
                if (BPressedDown()) StartTask();
            }
            return;
        }

        switch (state)
        {
            case TaskState.WaitingPracticeStart:
                if (waitingForPracticeStartBRelease)
                {
                    if (!BIsHeld())
                        waitingForPracticeStartBRelease = false;
                    break;
                }

                if (BPressedDown())
                {
                    StartPracticeTask();
                }
                break;

            case TaskState.WaitingEnterBetween:
                if (EnterPressedDown())
                {
                    logger?.SetPhase("RESEARCHER_CONFIRMED_WAITING_USER_BASELINE");
                    if (gateText != null) gateText.text = "RESEARCHER CONFIRMED.\n\nPrepare for baseline recording.\nPress B to start the next condition.";
                    state = TaskState.WaitingSecondB;
                }
                break;

            case TaskState.WaitingSecondB:
                if (BPressedDown())
                {
                    HideGate();
                    skipBaselineBWait = true;
                    StartConditionFlow();
                }
                break;

            case TaskState.WaitingBetweenBlocks:
                if (waitingForTriggerReleaseBetweenBlocks)
                {
                    if (!TriggerIsHeld()) waitingForTriggerReleaseBetweenBlocks = false;
                    break;
                }

                if (TriggerPressedDown())
                {
                    HideGate();
                    foreach (var b in balls) b.gameObject.SetActive(true);
                    ApplyCurrentConditionFeedbackState();
                    UpdateConditionLabel(conditions[currentGlobalConditionIndex]);
                    logger?.EndBreakTimer("BLOCK_RESUME");
                    logger?.LogEvent("BLOCK_RESUME", $"set={currentSetIndex} | block={currentBlockIndex}");
                    logger?.SetPhase("TASK_RUNNING");
                    state = TaskState.RunningCondition;

                    if (correctInCurrentPattern >= trialsPerPattern) AdvancePattern();
                    else PrepareNextTrialSamePattern();
                }
                break;
        }
    }

    private void StartConditionFlow()
    {
        if (flowRoutine != null) { StopCoroutine(flowRoutine); flowRoutine = null; }
        flowRoutine = StartCoroutine(RunBaselineThenCondition(skipBaselineBWait));
    }

    private IEnumerator RunBaselineThenCondition(bool skipBWait)
    {
        state = TaskState.BaselineAndCountdown;
        var cond = conditions[currentGlobalConditionIndex];
        UpdateConditionLabel(cond);
        if (feedbackRoot != null) feedbackRoot.SetActive(false);
        ApplyDisplayModeFromSlot(cond.presetSlot);
        hasConditionAnchor = false;
        conditionAnchor = Vector3.zero;

        logger?.SetCondition(cond.conditionName, cond.posture.ToString(), IsFeedbackEnabledFromSlot(cond.presetSlot), cond.presetSlot.ToString());
        logger?.SetBlockIndex(0);
        logger?.SetPhase("WAITING_BEFORE_BASELINE");

        if (countdownGate != null) countdownGate.ShowReadyMessage();

        if (!skipBWait)
        {
            logger?.PauseFrameLogging("WAITING_FOR_B");
            while (!BPressedDown()) yield return null;
            logger?.LogEvent("BASELINE_B_PRESSED", $"cond={cond.conditionName}");
        }

        logger?.SetPhase("BASELINE_START");
        logger?.ResumeFrameLogging("BASELINE_START");

        Coroutine calibRoutine = null;
        if (poseManager != null) calibRoutine = StartCoroutine(poseManager.CalibrateBaselinePipeline(10f));

        if (countdownGate != null) yield return countdownGate.RunCountdownImmediate();
        else yield return new WaitForSeconds(10f);

        if (calibRoutine != null) yield return calibRoutine;
        CaptureConditionAnchorFromBaseline();

        logger?.SetPhase("BASELINE_END");
        logger?.PauseFrameLogging("BASELINE_END");
        ShowGate("BASELINE COMPLETE.\n\nInstruction: Identify the differently colored ball, point at it with the controller, and press the trigger to select it.\nRepeat this task continuously.\n\nNow press the right trigger to start.");
        logger?.SetPhase("WAITING_BEFORE_TASK_START");

        while (!TriggerPressedDown()) yield return null;

        HideGate();
        ApplyCurrentConditionFeedbackState();
        UpdateConditionLabel(cond);
        foreach (var b in balls) b.gameObject.SetActive(true);

        logger?.SetPhase("TASK_START");
        logger?.ResumeFrameLogging("TASK_START");

        correctInCondition = 0;
        currentSetIndex = 0;
        currentBlockIndex = 1;
        currentPatternPosInSet = 0;
        correctInCurrentPattern = 0;
        correctInCurrentBlock = 0;
        selectionsPerBlock = Mathf.CeilToInt((float)cond.totalCorrectSelections / Mathf.Max(1, blocksPerSet));

        BuildConditionTargetSequence();
        ResetConditionTargetSequencePointer();
        logger?.SetBlockIndex(currentBlockIndex);
        logger?.LogEvent("BLOCK_TARGET_SEQUENCE", $"condition={cond.conditionName} | blockTemplate={BuildIntListText(conditionTargetSequence)}");

        state = TaskState.RunningCondition;
        StartNewSet();
        flowRoutine = null;
    }

    private void ApplyCurrentConditionFeedbackState()
    {
        if (currentGlobalConditionIndex < 0 || currentGlobalConditionIndex >= conditions.Count) return;
        var cond = conditions[currentGlobalConditionIndex];
        bool feedbackEnabled = IsFeedbackEnabledFromSlot(cond.presetSlot);
        if (feedbackRoot != null) feedbackRoot.SetActive(feedbackEnabled);
        ApplyDisplayModeFromSlot(cond.presetSlot);
    }

    private void ApplyDisplayModeFromSlot(ConditionPresetSlot slot)
    {
        if (postureNeck == null) return;
        postureNeck.SetDisplayMode(GetDisplayModeFromSlot(slot));
    }

    private PostureNeck.FeedbackDisplayMode GetDisplayModeFromSlot(ConditionPresetSlot slot)
    {
        switch (slot)
        {
            case ConditionPresetSlot.XC:
            case ConditionPresetSlot.YC:
                return PostureNeck.FeedbackDisplayMode.ForwardSlouchBackwardTilt;
            default:
                return PostureNeck.FeedbackDisplayMode.ForwardAndSlouchOnly;
        }
    }

    private bool IsFeedbackEnabledFromSlot(ConditionPresetSlot slot)
    {
        switch (slot)
        {
            case ConditionPresetSlot.XA:
            case ConditionPresetSlot.YA:
            case ConditionPresetSlot.None:
                return false;
            default:
                return true;
        }
    }

    private void CaptureConditionAnchorFromBaseline()
    {
        bool canCapture = poseManager != null && poseManager.headTransform != null && poseManager.IsCalibrated;
        if (!canCapture) { hasConditionAnchor = false; conditionAnchor = Vector3.zero; return; }
        Vector3 headWorldPos = poseManager.headTransform.position;
        conditionAnchor = new Vector3(headWorldPos.x, poseManager.BaselineHeight, headWorldPos.z);
        hasConditionAnchor = true;
    }

    private void StartNewSet()
    {
        currentSetIndex++;
        currentPatternPosInSet = 0;
        BuildRandomPatternOrder();
        logger?.LogEvent("SET_START", $"set={currentSetIndex} | block={currentBlockIndex}");
        logger?.SetPhase("TASK_RUNNING");
        StartPattern();
    }

    private void BuildRandomPatternOrder()
    {
        shuffledPatternOrder.Clear();
        int n = Mathf.Min(patternsPerSet, patterns.Count);
        for (int i = 0; i < n; i++) shuffledPatternOrder.Add(i);
        Shuffle(shuffledPatternOrder);
    }

    private void StartPattern()
    {
        correctInCurrentPattern = 0;
        if (currentPatternPosInSet >= shuffledPatternOrder.Count) { StartNewSet(); return; }
        foreach (var b in balls) b.gameObject.SetActive(true);
        int patternIdx = shuffledPatternOrder[currentPatternPosInSet];
        var pat = patterns[patternIdx];
        PlaceBallsForPattern(pat);
        PrepareNextTrialSamePattern();
    }

    private void AdvancePattern()
    {
        currentPatternPosInSet++;
        bool finishedSet = currentPatternPosInSet >= shuffledPatternOrder.Count;
        if (finishedSet) { StartNewSet(); return; }
        StartPattern();
    }

    private void BeginBreakBetweenBlocks()
    {
        currentBlockIndex++;
        correctInCurrentBlock = 0;
        ResetConditionTargetSequencePointer();

        logger?.SetPhase("BLOCK_BREAK");
        logger?.SetBlockIndex(currentBlockIndex);
        logger?.LogEvent("BLOCK_BREAK", $"set={currentSetIndex} | nextBlock={currentBlockIndex}");
        logger?.BeginBreakTimer("BLOCK_BREAK");

        foreach (var b in balls) b.gameObject.SetActive(false);
        if (feedbackRoot != null) feedbackRoot.SetActive(false);
        ShowGate("You may rest as long as you need.\n\nWhen you are ready to continue, press the Right Trigger to continue.");

        waitingForTriggerReleaseBetweenBlocks = true;
        state = TaskState.WaitingBetweenBlocks;
    }

    private void PrepareNextTrialSamePattern()
    {
        if (basePalette == null) basePalette = new List<Color>();
        if (basePalette.Count == 0) basePalette.Add(Color.white);

        if (conditionTargetSequence.Count == 0) BuildConditionTargetSequence();
        if (conditionTargetPointer >= conditionTargetSequence.Count) conditionTargetPointer = 0;
        currentTargetBallIndex = conditionTargetSequence[conditionTargetPointer];
        conditionTargetPointer++;

        int colorIndex = (currentBlockIndex - 1) % basePalette.Count;
        currentBaseColor = basePalette[colorIndex];

        float delta = targetBrightnessDelta;
        bool brighter = UnityEngine.Random.value < targetBrighterProbability;
        if (!brighter) delta = -delta;
        currentTargetColor = AdjustBrightness(currentBaseColor, delta);

        for (int i = 0; i < balls.Count; i++)
        {
            bool isTarget = (i == currentTargetBallIndex);
            var ball = balls[i];
            ball.SetTarget(isTarget);
            ball.ApplyColor(isTarget ? currentTargetColor : currentBaseColor);
        }

        trialReadyRT = Time.realtimeSinceStartup;
        logger?.LogEvent("TRIAL_READY", $"set={currentSetIndex} | block={currentBlockIndex} | targetIndex={currentTargetBallIndex} | baseColorIndex={colorIndex}");
    }

    private void BuildConditionTargetSequence()
    {
        conditionTargetSequence.Clear();
        conditionTargetPointer = 0;

        int ballCount = balls.Count;
        if (ballCount <= 0) return;

        int trialsNeeded = Mathf.Max(1, selectionsPerBlock);
        while (conditionTargetSequence.Count < trialsNeeded)
        {
            List<int> cycle = new List<int>();
            for (int i = 0; i < ballCount; i++) cycle.Add(i);
            Shuffle(cycle);
            int remainingNeeded = trialsNeeded - conditionTargetSequence.Count;
            int takeCount = Mathf.Min(remainingNeeded, cycle.Count);
            for (int i = 0; i < takeCount; i++) conditionTargetSequence.Add(cycle[i]);
        }
    }

    private void ResetConditionTargetSequencePointer() { conditionTargetPointer = 0; }

    private void BuildPracticePatternOrder()
    {
        practicePatternOrder.Clear();
        int n = Mathf.Min(patternsPerSet, patterns.Count);
        if (n <= 0) return;

        List<int> round1 = new List<int>();
        List<int> round2 = new List<int>();
        for (int i = 0; i < n; i++) { round1.Add(i); round2.Add(i); }
        Shuffle(round1);
        Shuffle(round2);
        practicePatternOrder.AddRange(round1);
        practicePatternOrder.AddRange(round2);
    }

    private void BuildPracticeTargetSequence()
    {
        practiceTargetSequence.Clear();
        practiceTargetPointer = 0;
        int ballCount = balls.Count;
        if (ballCount <= 0) return;
        for (int i = 0; i < practiceCorrectSelections; i++) practiceTargetSequence.Add(Random.Range(0, ballCount));
    }

    private void StartPracticePattern()
    {
        correctInCurrentPattern = 0;
        if (practicePatternPointer >= practicePatternOrder.Count) { EndPracticeTask(); return; }

        int roundSwitchIndex = Mathf.Min(patternsPerSet, patterns.Count);
        practiceCurrentRound = practicePatternPointer >= roundSwitchIndex ? 2 : 1;

        int patternIdx = practicePatternOrder[practicePatternPointer];
        var pat = patterns[patternIdx];
        foreach (var b in balls) b.gameObject.SetActive(true);
        PlacePracticeBallsForPattern(pat);
        PrepareNextPracticeTrial();
    }

    private void AdvancePracticePattern()
    {
        practicePatternPointer++;
        if (practicePatternPointer >= practicePatternOrder.Count)
        {
            if (correctInCondition >= practiceCorrectSelections) { EndPracticeTask(); return; }
            BuildPracticePatternOrder();
            practicePatternPointer = 0;
        }
        StartPracticePattern();
    }

    private void PrepareNextPracticeTrial()
    {
        if (basePalette == null) basePalette = new List<Color>();
        if (basePalette.Count == 0) basePalette.Add(Color.white);

        if (practiceTargetSequence.Count == 0) BuildPracticeTargetSequence();
        if (practiceTargetPointer >= practiceTargetSequence.Count) practiceTargetPointer = 0;
        currentTargetBallIndex = practiceTargetSequence[practiceTargetPointer];
        practiceTargetPointer++;

        int colorIndex = (practiceCurrentRound - 1) % basePalette.Count;
        currentBaseColor = basePalette[colorIndex];

        float delta = targetBrightnessDelta;
        bool brighter = UnityEngine.Random.value < targetBrighterProbability;
        if (!brighter) delta = -delta;
        currentTargetColor = AdjustBrightness(currentBaseColor, delta);

        for (int i = 0; i < balls.Count; i++)
        {
            bool isTarget = (i == currentTargetBallIndex);
            var ball = balls[i];
            ball.SetTarget(isTarget);
            ball.ApplyColor(isTarget ? currentTargetColor : currentBaseColor);
        }

        trialReadyRT = Time.realtimeSinceStartup;
    }

    private void PlacePracticeBallsForPattern(PatternConfig pat)
    {
        ConditionConfig practiceCond = GetPracticeReferenceCondition();

        bool canAnchorToHead =
            poseManager != null &&
            poseManager.headTransform != null;

        Vector3 practiceAnchor = Vector3.zero;
        if (canAnchorToHead)
        {
            Vector3 headWorldPos = poseManager.headTransform.position;
            practiceAnchor = new Vector3(headWorldPos.x, headWorldPos.y, headWorldPos.z);
        }

        for (int i = 0; i < balls.Count; i++)
        {
            Vector3 patternOffset = (pat.localPositions != null && i < pat.localPositions.Count) ? pat.localPositions[i] : Vector3.zero;
            Vector3 transformedOffset = ApplyTransforms(patternOffset, practiceCond);

            if (canAnchorToHead)
            {
                Vector3 worldPos = new Vector3(
                    practiceAnchor.x + transformedOffset.x,
                    practiceAnchor.y + transformedOffset.y,
                    practiceAnchor.z + transformedOffset.z
                );
                balls[i].transform.position = worldPos;
            }
            else
            {
                balls[i].transform.localPosition = transformedOffset;
            }
        }
    }

    private ConditionConfig GetPracticeReferenceCondition()
    {
        for (int i = 0; i < conditions.Count; i++)
            if (conditions[i].presetSlot == practiceReferenceSlot)
                return conditions[i];
        if (conditions != null && conditions.Count > 0) return conditions[0];
        return new ConditionConfig();
    }

    private void EndPracticeTask()
    {
        if (feedbackRoot != null) feedbackRoot.SetActive(false);
        foreach (var b in balls) b.gameObject.SetActive(false);
        HideConditionLabel();
        ShowGate(practiceFinishedMessage);
        state = TaskState.Finished;
    }

    private string BuildIntListText(List<int> values)
    {
        if (values == null || values.Count == 0) return "";
        return string.Join("-", values);
    }

    private void EndCurrentCondition()
    {
        logger?.SetPhase("TASK_END");
        logger?.PauseFrameLogging("TASK_END");
        if (feedbackRoot != null) feedbackRoot.SetActive(false);
        foreach (var b in balls) b.gameObject.SetActive(false);

        var finishedCond = conditions[currentGlobalConditionIndex];
        bool hasNextCondition = currentTaskPos < activeTask.Count - 1;
        if (hasNextCondition)
        {
            currentTaskPos++;
            currentGlobalConditionIndex = activeTask[currentTaskPos];
            ShowGate($"{finishedCond.conditionName} FINISHED.\n\nResearcher press ENTER to continue.");
            logger?.SetPhase("WAITING_RESEARCHER_CONFIRMATION");
            logger?.LogEvent("CONDITION_FINISHED_WAITING_NEXT", $"finished={finishedCond.conditionName} | next={conditions[currentGlobalConditionIndex].conditionName} | taskPos={currentTaskPos + 1}/{activeTask.Count}");
            state = TaskState.WaitingEnterBetween;
            return;
        }

        logger?.StopAndSave();
        HideConditionLabel();
        ShowGate(taskFinishedMessage);
        state = TaskState.Finished;
    }

    private void BuildActiveTask()
    {
        activeTask.Clear();
        List<ConditionPresetSlot> fullOrder = GetConditionPresetSlotOrder(conditionSequencePreset);
        List<ConditionPresetSlot> partOrder = GetPartOrder(fullOrder, conditionSequencePart);
        Dictionary<ConditionPresetSlot, int> slotMap = BuildConditionSlotIndexMap();

        foreach (ConditionPresetSlot slot in partOrder)
            if (slotMap.TryGetValue(slot, out int index))
                activeTask.Add(index);

        currentTaskSequencePresetName = conditionSequencePreset.ToString();
        currentTaskSequencePartName = conditionSequencePart.ToString();
        currentTaskSequenceOrderText = BuildSlotOrderText(partOrder);
    }

    private Dictionary<ConditionPresetSlot, int> BuildConditionSlotIndexMap()
    {
        Dictionary<ConditionPresetSlot, int> map = new Dictionary<ConditionPresetSlot, int>();
        for (int i = 0; i < conditions.Count; i++)
        {
            var slot = conditions[i].presetSlot;
            if (slot == ConditionPresetSlot.None) continue;
            if (!map.ContainsKey(slot)) map.Add(slot, i);
        }
        return map;
    }

    private List<ConditionPresetSlot> GetPartOrder(List<ConditionPresetSlot> fullOrder, ConditionSequencePart part)
    {
        List<ConditionPresetSlot> result = new List<ConditionPresetSlot>();
        if (fullOrder == null || fullOrder.Count < 6) return result;

        int start = part == ConditionSequencePart.Part2 ? 3 : 0;
        for (int i = start; i < start + 3 && i < fullOrder.Count; i++) result.Add(fullOrder[i]);
        return result;
    }

    private List<ConditionPresetSlot> GetConditionPresetSlotOrder(ConditionSequencePreset preset)
    {
        switch (preset)
        {
            case ConditionSequencePreset.P1: return new List<ConditionPresetSlot> { ConditionPresetSlot.XA, ConditionPresetSlot.XB, ConditionPresetSlot.XC, ConditionPresetSlot.YA, ConditionPresetSlot.YB, ConditionPresetSlot.YC };
            case ConditionSequencePreset.P2: return new List<ConditionPresetSlot> { ConditionPresetSlot.XC, ConditionPresetSlot.XA, ConditionPresetSlot.XB, ConditionPresetSlot.YC, ConditionPresetSlot.YA, ConditionPresetSlot.YB };
            case ConditionSequencePreset.P3: return new List<ConditionPresetSlot> { ConditionPresetSlot.XB, ConditionPresetSlot.XC, ConditionPresetSlot.XA, ConditionPresetSlot.YB, ConditionPresetSlot.YC, ConditionPresetSlot.YA };
            case ConditionSequencePreset.P4: return new List<ConditionPresetSlot> { ConditionPresetSlot.YA, ConditionPresetSlot.YB, ConditionPresetSlot.YC, ConditionPresetSlot.XA, ConditionPresetSlot.XB, ConditionPresetSlot.XC };
            case ConditionSequencePreset.P5: return new List<ConditionPresetSlot> { ConditionPresetSlot.YC, ConditionPresetSlot.YA, ConditionPresetSlot.YB, ConditionPresetSlot.XC, ConditionPresetSlot.XA, ConditionPresetSlot.XB };
            case ConditionSequencePreset.P6: return new List<ConditionPresetSlot> { ConditionPresetSlot.YB, ConditionPresetSlot.YC, ConditionPresetSlot.YA, ConditionPresetSlot.XB, ConditionPresetSlot.XC, ConditionPresetSlot.XA };
            default: return new List<ConditionPresetSlot>();
        }
    }

    private string BuildSlotOrderText(List<ConditionPresetSlot> slotOrder)
    {
        if (slotOrder == null || slotOrder.Count == 0) return "";
        List<string> parts = new List<string>();
        foreach (ConditionPresetSlot slot in slotOrder) parts.Add(slot.ToString());
        return string.Join("->", parts);
    }

    private void UpdateConditionLabel(ConditionConfig cond, string prefix = "Now:")
    {
        if (!showConditionLabel || conditionLabelText == null) return;
        conditionLabelText.gameObject.SetActive(true);
        conditionLabelText.text = $"{prefix} {cond.posture} Mode";
    }

    private void HideConditionLabel()
    {
        if (conditionLabelText == null) return;
        conditionLabelText.gameObject.SetActive(false);
    }

    private void ShowGate(string message)
    {
        HideConditionLabel();
        if (gateRoot != null) gateRoot.SetActive(true);
        if (gateText != null) gateText.text = message;
    }

    private void HideGate()
    {
        if (gateRoot != null) gateRoot.SetActive(false);
    }

    private bool BPressedDown()
    {
        if (Input.GetKeyDown(KeyCode.B)) return true;
        InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (rightHand.isValid)
        {
            bool pressed;
            if (rightHand.TryGetFeatureValue(CommonUsages.secondaryButton, out pressed) && pressed) return true;
        }
        return false;
    }

    private bool BIsHeld()
    {
        if (Input.GetKey(KeyCode.B)) return true;
        InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (rightHand.isValid)
        {
            bool pressed;
            if (rightHand.TryGetFeatureValue(CommonUsages.secondaryButton, out pressed) && pressed) return true;
        }
        return false;
    }

    private bool TriggerPressedDown()
    {
        if (Input.GetKeyDown(KeyCode.T)) return true;
        InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (rightHand.isValid)
        {
            bool pressedBool;
            if (rightHand.TryGetFeatureValue(CommonUsages.triggerButton, out pressedBool) && pressedBool) return true;
            float trigger;
            if (rightHand.TryGetFeatureValue(CommonUsages.trigger, out trigger) && trigger > 0.8f) return true;
        }
        return false;
    }

    private bool TriggerIsHeld()
    {
        if (Input.GetKey(KeyCode.T)) return true;
        InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (rightHand.isValid)
        {
            bool pressedBool;
            if (rightHand.TryGetFeatureValue(CommonUsages.triggerButton, out pressedBool) && pressedBool) return true;
            float trigger;
            if (rightHand.TryGetFeatureValue(CommonUsages.trigger, out trigger) && trigger > 0.2f) return true;
        }
        return false;
    }

    private bool EnterPressedDown()
    {
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
    }

    private void EnsureBallPool()
    {
        if (balls.Count > 0) return;
        if (ballPrefab == null) { Debug.LogError("ballPrefab is not set."); return; }

        for (int i = 0; i < ballsPerPattern; i++)
        {
            var go = Instantiate(ballPrefab, Vector3.zero, Quaternion.identity);
            go.name = $"Ball_{i}";
            var bs = go.GetComponent<BallSelectable>();
            if (bs == null) bs = go.AddComponent<BallSelectable>();
            bs.Initialize(this);
            balls.Add(bs);
        }
    }

    private void PlaceBallsForPattern(PatternConfig pat)
    {
        var cond = conditions[currentGlobalConditionIndex];
        for (int i = 0; i < balls.Count; i++)
        {
            Vector3 patternOffset = (pat.localPositions != null && i < pat.localPositions.Count) ? pat.localPositions[i] : Vector3.zero;
            Vector3 transformedOffset = ApplyTransforms(patternOffset, cond);
            if (hasConditionAnchor)
            {
                Vector3 worldPos = new Vector3(conditionAnchor.x + transformedOffset.x, conditionAnchor.y + transformedOffset.y, conditionAnchor.z + transformedOffset.z);
                balls[i].transform.position = worldPos;
            }
            else
            {
                balls[i].transform.localPosition = transformedOffset;
            }
        }
    }

    private Vector3 ApplyTransforms(Vector3 local, ConditionConfig cond)
    {
        local.x *= cond.spreadScale;
        local.z *= cond.spreadScale;
        local.y = local.y * cond.heightScale + cond.heightOffset;
        return local;
    }

    private bool ValidateSetup()
    {
        if (patterns == null || patterns.Count == 0) { Debug.LogError("No patterns configured."); return false; }
        if (ballPrefab == null) { Debug.LogError("ballPrefab is not set."); return false; }

        if (taskRunMode == TaskRunMode.Study)
        {
            if (conditions == null || conditions.Count == 0) { Debug.LogError("No conditions configured."); return false; }
            if (conditionSequencePreset == ConditionSequencePreset.None) { Debug.LogError("conditionSequencePreset must not be None."); return false; }
        }
        return true;
    }

    private void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    private Color AdjustBrightness(Color c, float delta)
    {
        float r = Mathf.Clamp01(c.r + delta);
        float g = Mathf.Clamp01(c.g + delta);
        float b = Mathf.Clamp01(c.b + delta);
        return new Color(r, g, b, c.a);
    }

    private void Awake()
    {
        if (gateRoot != null) gateRoot.SetActive(false);
        if (conditionLabelText != null) conditionLabelText.gameObject.SetActive(false);
        if (countdownGate != null && taskRunMode == TaskRunMode.Practice) countdownGate.gameObject.SetActive(false);
    }
}
