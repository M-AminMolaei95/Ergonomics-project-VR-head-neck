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
    public enum ViewArcMode { Arc90 = 90, Arc180 = 180, Arc360 = 360 }
    public enum ViewArcOrderPreset
    {
        MatchConditionSequencePreset = 0,
        V1_90_180_360,
        V2_90_360_180,
        V3_180_90_360,
        V4_180_360_90,
        V5_360_90_180,
        V6_360_180_90
    }

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

    [System.Serializable]
    public class TargetSearchTemplate
    {
        public string templateName = "Template";

        [Tooltip("One-based ball indices. Example: 1 means Ball_0. If empty or invalid, a deterministic fallback path is used.")]
        public List<int> targetOrderOneBased = new List<int>();
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

    [Header("Patterns A..J (order matters; used in Inspector order per block)")]
    public List<PatternConfig> patterns = new List<PatternConfig>();

    [Header("Ball setup")]
    public GameObject ballPrefab;
    public int ballsPerPattern = 10;

    [Header("Sets / trials")]
    public int patternsPerSet = 10;
    public int trialsPerPattern = 3;

    [Header("Blocks")]
    [Tooltip("Number of blocks to run for EACH view arc inside EACH condition. Example: if this is 1 and view arcs are 90/180/360, each condition has 3 total blocks.")]
    public int blocksPerSet = 1;

    [Header("View Arc Segments per Condition")]
    [Tooltip("If true, each condition is divided into three view-arc segments: 90, 180, and 360 degrees.")]
    public bool enableViewArcSegments = true;

    [Tooltip("Preset order for the 90/180/360 view segments. MatchConditionSequencePreset maps P1..P6 to the six possible orders.")]
    public ViewArcOrderPreset viewArcOrderPreset = ViewArcOrderPreset.MatchConditionSequencePreset;

    [Tooltip("Message shown between view-arc segments. {condition}, {completedSegment}, {totalSegments}, {nextArc}, {nextBlock}, {blocksPerSet}, {totalBlocksInCondition} are supported. blocksPerSet means blocks per view arc.")]
    [TextArea(3, 8)]
    public string viewArcTransitionMessage =
        "This viewing range is complete.\n\nNext viewing range: {nextArc}°.\n\nWhen you are ready to continue, press the Right Trigger.";

    [Tooltip("When true, the current view arc is added to the small condition label.")]
    public bool showViewArcInConditionLabel = true;

    [Tooltip("When true, ball positions are rotated relative to the participant's baseline yaw/head direction.")]
    public bool useBaselineYawForBallPlacement = true;

    [Tooltip("Minimum distance from the baseline anchor. The pattern Z value still controls each ball's distance from the user; this only prevents balls from becoming too close.")]
    public float minViewArcRadius = 0.75f;

    [Tooltip("Optional empty angle kept at both left/right edges of 90/180 arcs. Example: 5 means the balls stay 5 degrees inside the arc edges.")]
    public float viewArcEdgePaddingDegrees = 0f;

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

    [Header("Baseline Fixation Target")]
    public GameObject baselineFixationTarget;
    public float lookAtSignMessageDuration = 2f;
    public float baselineFixationDuration = 10f;

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

    [Header("Fixed Block / Pattern Trial Order")]
    [Tooltip("If true, each block repeats the same fixed sequence: Pattern 1 trials, Pattern 2 trials, etc. Targets inside each pattern follow ball index order.")]
    public bool useFixedBlockPatternTrialOrder = true;

    [Header("Target Search Templates")]
    [Tooltip("If true, one fixed search-effort template is selected per pattern-block instance. The selected template then controls all trials inside that pattern without repeated target balls.")]
    public bool useTargetSearchTemplates = true;

    [Tooltip("Fixed seed for template-order cycles. This makes the order look shuffled but keeps it identical for every participant and every Play.")]
    public int targetTemplateCycleSeed = 20260525;

    [Tooltip("If true, template cycles restart at the beginning of every condition. Recommended: false, so unused templates from a shuffled cycle are consumed first in the next view arc/condition.")]
    public bool resetTargetTemplatesAtConditionStart = false;

    [Tooltip("Five fixed search-effort templates. The default values are for 11 balls and use one-based ball numbers.")]
    public List<TargetSearchTemplate> targetSearchTemplates = new List<TargetSearchTemplate>()
    {
        new TargetSearchTemplate { templateName = "T1_HighJump_Irregular", targetOrderOneBased = new List<int> { 1, 9, 3, 11, 5, 10, 2, 8, 4, 7, 6 } },
        new TargetSearchTemplate { templateName = "T2_HighMedium_Irregular", targetOrderOneBased = new List<int> { 2, 10, 5, 1, 8, 3, 11, 6, 9, 4, 7 } },
        new TargetSearchTemplate { templateName = "T3_Medium_Irregular", targetOrderOneBased = new List<int> { 3, 8, 1, 6, 10, 4, 9, 2, 7, 11, 5 } },
        new TargetSearchTemplate { templateName = "T4_LowerMedium_Irregular", targetOrderOneBased = new List<int> { 4, 7, 2, 6, 9, 5, 8, 3, 10, 1, 11 } },
        new TargetSearchTemplate { templateName = "T5_Low_Irregular", targetOrderOneBased = new List<int> { 5, 3, 7, 4, 8, 6, 2, 9, 1, 10, 11 } }
    };

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
    private int currentPatternIndex = 0;
    private int correctInCurrentPattern = 0;
    private int correctInCurrentBlock = 0;
    private int selectionsPerBlock = 1;
    private int currentTargetBallIndex = -1;
    private int currentTargetTemplateIndex = -1;
    private string currentTargetTemplateName = "None";
    private int currentTargetTemplateCycleNumberForLog = 0;
    private readonly List<int> targetTemplateCycle = new List<int>();
    private int targetTemplateCyclePointer = 0;
    private int targetTemplateCycleNumber = 0;
    private int lastTargetTemplateIndex = -1;
    private bool targetTemplateSystemInitialized = false;

    // One template is selected ONCE per pattern-block instance.
    // All trials inside that pattern then follow this selected template path.
    private int activePatternBlockTemplateIndex = -1;
    private string activePatternBlockTemplateName = "None";
    private int activePatternBlockTemplateCycleNumber = 0;
    private int[] activePatternBlockTargetPath = new int[0];
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
    private Vector3 conditionForwardFlat = Vector3.forward;
    private Vector3 conditionRightFlat = Vector3.right;
    private readonly List<ViewArcMode> currentConditionViewArcOrder = new List<ViewArcMode>();
    private int currentViewArcSegmentIndex = 0;
    private ViewArcMode currentViewArc = ViewArcMode.Arc360;
    private Coroutine flowRoutine = null;

    private enum TaskState { Idle, WaitingPracticeStart, BaselineAndCountdown, RunningCondition, WaitingEnterBetween, WaitingSecondB, WaitingBetweenBlocks, Finished }
    private TaskState state = TaskState.Idle;
    private bool skipBaselineBWait = false;
    private bool waitingForTriggerReleaseBetweenBlocks = false;
    private bool waitingForPracticeStartBRelease = false;
    private bool practicePromptPrepared = false;
    private bool studyAutoStarted = false;

    public void BeginProtocol() => StartTask();
    public void StartExperiment() => StartTask();

    public void StartTask()
    {
        if (state != TaskState.Idle && state != TaskState.Finished) return;
        if (!ValidateSetup()) return;
        practicePromptPrepared = false;

        if (flowRoutine != null) { StopCoroutine(flowRoutine); flowRoutine = null; }
        EnsureBallPool();

        if (taskRunMode == TaskRunMode.Study)
            InitializeTargetTemplateSystem(true);

        if (taskRunMode == TaskRunMode.Practice)
        {
            PreparePracticeStart();
            return;
        }

        BuildActiveTask();
        if (activeTask.Count != 3)
        {
            ShowGate("Task config error.\n\nThis preset-part must resolve to EXACTLY 3 conditions.\n" +
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
            countdownGate.HideAll();

        if (feedbackRoot != null)
            feedbackRoot.SetActive(false);

        foreach (var b in balls)
            b.gameObject.SetActive(false);

        ShowGate("Practice mode.\n\nPress the Right Trigger to start.");
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
        logger?.LogEvent("CORRECT_HIT", $"set={currentSetIndex} | block={currentBlockIndex} | blockInViewArc={GetBlockNumberWithinCurrentViewArc(currentBlockIndex)}/{blocksPerSet} | viewArc={(int)currentViewArc} | patternIndex={currentPatternIndex} | trialInPattern={correctInCurrentPattern} | rtSec={rtSec:F3} | correct={correctInCondition} | targetIndex={currentTargetBallIndex} | targetBallOneBased={currentTargetBallIndex + 1} | targetTemplateIndex={(currentTargetTemplateIndex >= 0 ? currentTargetTemplateIndex + 1 : 0)} | targetTemplateName={currentTargetTemplateName} | targetTemplateCycle={currentTargetTemplateCycleNumberForLog}");

        // Fixed block structure:
        // Each block = all patterns in Inspector order.
        // Each pattern = trialsPerPattern consecutive trials.
        // Each condition = blocksPerSet repetitions of the same block.
        if (correctInCurrentPattern >= trialsPerPattern)
        {
            currentPatternPosInSet++;

            bool finishedBlock = currentPatternPosInSet >= shuffledPatternOrder.Count;
            if (finishedBlock)
            {
                bool finishedCondition = currentBlockIndex >= GetTotalBlocksInCondition();
                if (finishedCondition)
                {
                    EndCurrentCondition();
                    return;
                }

                BeginBreakBetweenBlocks();
                return;
            }

            StartPattern();
            return;
        }

        PrepareNextTrialSamePattern();
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
                if (!studyAutoStarted)
                {
                    studyAutoStarted = true;
                    StartTask();
                    return;
                }

                if (TriggerPressedDown()) StartTask();
            }
            return;
        }

        switch (state)
        {
            case TaskState.WaitingPracticeStart:
                if (waitingForPracticeStartBRelease)
                {
                    if (!TriggerIsHeld())
                        waitingForPracticeStartBRelease = false;
                    break;
                }

                if (TriggerPressedDown())
                {
                    StartPracticeTask();
                }
                break;

            case TaskState.WaitingEnterBetween:
                if (EnterPressedDown())
                {
                    logger?.SetPhase("RESEARCHER_CONFIRMED_WAITING_USER_BASELINE");
                    if (gateText != null) gateText.text = "RESEARCHER CONFIRMED.\n\nPrepare for baseline recording.\nPress the Right Trigger to start the next condition.";
                    state = TaskState.WaitingSecondB;
                }
                break;

            case TaskState.WaitingSecondB:
                if (TriggerPressedDown())
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
                    UpdateCurrentViewArcForBlock();
                    UpdateConditionLabel(conditions[currentGlobalConditionIndex]);
                    logger?.EndBreakTimer("BLOCK_RESUME");
                    logger?.LogEvent("BLOCK_RESUME", $"set={currentSetIndex} | block={currentBlockIndex} | blockInViewArc={GetBlockNumberWithinCurrentViewArc(currentBlockIndex)}/{blocksPerSet} | totalBlocksInCondition={GetTotalBlocksInCondition()} | viewArc={(int)currentViewArc}");
                    LogCurrentViewArcSegment("VIEW_ARC_RESUME");
                    logger?.SetPhase("TASK_RUNNING");
                    state = TaskState.RunningCondition;

                    BuildPatternOrder();
                    StartPattern();
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

        HideGate();
        HideConditionLabel();

        if (countdownGate != null)
            countdownGate.HideAll();

        if (feedbackRoot != null)
            feedbackRoot.SetActive(false);

        if (baselineFixationTarget != null)
            baselineFixationTarget.SetActive(false);

        foreach (var b in balls)
            b.gameObject.SetActive(false);

        ApplyDisplayModeFromSlot(cond.presetSlot);
        hasConditionAnchor = false;
        conditionAnchor = Vector3.zero;

        logger?.SetCondition(cond.conditionName, cond.posture.ToString(), IsFeedbackEnabledFromSlot(cond.presetSlot), cond.presetSlot.ToString());
        logger?.SetBlockIndex(0);
        logger?.SetPhase("WAITING_BEFORE_BASELINE");


        HideConditionLabel();

        if (countdownGate != null)
            countdownGate.HideAll();

        foreach (var b in balls)
            b.gameObject.SetActive(false);

        if (feedbackRoot != null)
            feedbackRoot.SetActive(false);

        if (baselineFixationTarget != null)
            baselineFixationTarget.SetActive(true);

        ShowGate("Please look at the sign (+)");

        if (lookAtSignMessageDuration > 0f)
            yield return new WaitForSeconds(lookAtSignMessageDuration);

        HideGate();

        if (baselineFixationDuration > 0f)
            yield return new WaitForSeconds(baselineFixationDuration);


        if (countdownGate != null)
            countdownGate.SetReadyPostureGuideVisible(true);

        ShowGate("(Facing Forward to the Sign)\nKeep a straight, comfortable, and upright posture\nDURING EACH COUNTDOWN\n\n** When you are ready, press the Right Trigger on the controller **");

        logger?.PauseFrameLogging("WAITING_FOR_B_BEFORE_BASELINE");

        while (TriggerIsHeld())
            yield return null;

        while (!TriggerPressedDown())
            yield return null;

        logger?.LogEvent("BASELINE_TRIGGER_PRESSED", $"cond={cond.conditionName}");

        HideGate();


        logger?.SetPhase("BASELINE_START");
        logger?.ResumeFrameLogging("BASELINE_START");

        Coroutine calibRoutine = null;
        if (poseManager != null)
            calibRoutine = StartCoroutine(poseManager.CalibrateBaselinePipeline(10f));

        if (countdownGate != null)
            yield return countdownGate.RunCountdownImmediate();
        else
            yield return new WaitForSeconds(10f);

        if (countdownGate != null)
            countdownGate.HideAll();

        if (calibRoutine != null)
            yield return calibRoutine;

        CaptureConditionAnchorFromBaseline();


        if (baselineFixationTarget != null)
            baselineFixationTarget.SetActive(false);

        logger?.SetPhase("BASELINE_END");
        logger?.PauseFrameLogging("BASELINE_END");

        ShowGate("BASELINE COMPLETE.\n\nInstruction: Identify the differently colored ball, point at it with the controller, and press the trigger to select it.\nRepeat this task continuously.\n\nNow press the right trigger to start.");
        logger?.SetPhase("WAITING_BEFORE_TASK_START");

        while (!TriggerPressedDown())
            yield return null;

        HideGate();
        ApplyCurrentConditionFeedbackState();
        UpdateConditionLabel(cond);

        foreach (var b in balls)
            b.gameObject.SetActive(true);

        logger?.SetPhase("TASK_START");
        logger?.ResumeFrameLogging("TASK_START");

        correctInCondition = 0;
        currentSetIndex = 0;
        currentBlockIndex = 1;
        currentPatternPosInSet = 0;
        correctInCurrentPattern = 0;
        correctInCurrentBlock = 0;
        selectionsPerBlock = Mathf.Max(1, Mathf.Min(patternsPerSet, patterns.Count)) * Mathf.Max(1, trialsPerPattern);

        BuildConditionTargetSequence();
        ResetConditionTargetSequencePointer();
        if (useTargetSearchTemplates && resetTargetTemplatesAtConditionStart)
            InitializeTargetTemplateSystem(true);
        else if (useTargetSearchTemplates && !targetTemplateSystemInitialized)
            InitializeTargetTemplateSystem(true);
        BuildCurrentConditionViewArcOrder();
        UpdateCurrentViewArcForBlock();
        LogCurrentViewArcSegment("VIEW_ARC_START");

        logger?.SetBlockIndex(currentBlockIndex);
        logger?.LogEvent("BLOCK_STRUCTURE", $"condition={cond.conditionName} | blocksPerViewArc={blocksPerSet} | totalBlocksInCondition={GetTotalBlocksInCondition()} | patternsPerBlock={Mathf.Min(patternsPerSet, patterns.Count)} | trialsPerPattern={trialsPerPattern} | selectionsPerBlock={selectionsPerBlock} | viewArcSegments={(enableViewArcSegments ? BuildViewArcOrderText(currentConditionViewArcOrder) : "disabled")} | useTargetSearchTemplates={useTargetSearchTemplates} | targetTemplateSeed={targetTemplateCycleSeed} | resetTargetTemplatesAtConditionStart={resetTargetTemplatesAtConditionStart}");

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
        if (!canCapture)
        {
            hasConditionAnchor = false;
            conditionAnchor = Vector3.zero;
            conditionForwardFlat = Vector3.forward;
            conditionRightFlat = Vector3.right;
            return;
        }

        Transform head = poseManager.headTransform;
        Vector3 headWorldPos = head.position;
        conditionAnchor = new Vector3(headWorldPos.x, poseManager.BaselineHeight, headWorldPos.z);

        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        conditionForwardFlat = forward.normalized;
        conditionRightFlat = Vector3.Cross(Vector3.up, conditionForwardFlat).normalized;

        hasConditionAnchor = true;
    }

    private void StartNewSet()
    {
        currentSetIndex++;
        currentPatternPosInSet = 0;
        BuildPatternOrder();
        logger?.LogEvent("SET_START", $"set={currentSetIndex} | block={currentBlockIndex}");
        logger?.SetPhase("TASK_RUNNING");
        StartPattern();
    }

    private void BuildPatternOrder()
    {
        shuffledPatternOrder.Clear();

        int n = Mathf.Min(patternsPerSet, patterns.Count);
        for (int i = 0; i < n; i++)
            shuffledPatternOrder.Add(i);

    }

    private void StartPattern()
    {
        correctInCurrentPattern = 0;
        if (currentPatternPosInSet >= shuffledPatternOrder.Count) { StartNewSet(); return; }
        foreach (var b in balls) b.gameObject.SetActive(true);
        int patternIdx = shuffledPatternOrder[currentPatternPosInSet];
        currentPatternIndex = patternIdx;
        var pat = patterns[patternIdx];
        PlaceBallsForPattern(pat);
        SelectTargetTemplateForCurrentPatternBlock();
        PrepareNextTrialSamePattern();
    }

    private void AdvancePattern()
    {
        currentPatternPosInSet++;

        bool finishedBlock = currentPatternPosInSet >= shuffledPatternOrder.Count;
        if (finishedBlock)
        {
            if (currentBlockIndex >= GetTotalBlocksInCondition())
            {
                EndCurrentCondition();
                return;
            }

            BeginBreakBetweenBlocks();
            return;
        }

        StartPattern();
    }

    private void BeginBreakBetweenBlocks()
    {
        currentBlockIndex++;
        currentSetIndex = currentBlockIndex;
        correctInCurrentBlock = 0;
        currentPatternPosInSet = 0;
        correctInCurrentPattern = 0;
        ResetConditionTargetSequencePointer();

        logger?.SetPhase("BLOCK_BREAK");
        logger?.SetBlockIndex(currentBlockIndex);
        logger?.LogEvent("BLOCK_BREAK", $"set={currentSetIndex} | nextBlock={currentBlockIndex} | blockInViewArc={GetBlockNumberWithinCurrentViewArc(currentBlockIndex)}/{blocksPerSet} | totalBlocksInCondition={GetTotalBlocksInCondition()}");
        logger?.BeginBreakTimer("BLOCK_BREAK");

        foreach (var b in balls) b.gameObject.SetActive(false);
        if (feedbackRoot != null) feedbackRoot.SetActive(false);
        int completedBlock = currentBlockIndex - 1;
        int conditionNumber = currentTaskPos + 1;
        int nextSegmentIndex = GetViewArcSegmentIndexForBlock(currentBlockIndex);
        int completedSegmentIndex = GetViewArcSegmentIndexForBlock(completedBlock);
        bool isViewArcTransition = enableViewArcSegments && nextSegmentIndex != completedSegmentIndex;

        if (isViewArcTransition)
        {
            ViewArcMode nextArc = GetViewArcForSegmentIndex(nextSegmentIndex);
            string message = FormatViewArcTransitionMessage(
                conditions[currentGlobalConditionIndex],
                completedSegmentIndex + 1,
                GetViewArcSegmentCount(),
                nextArc,
                currentBlockIndex
            );
            ShowGate(message);
            logger?.LogEvent("VIEW_ARC_TRANSITION_WAIT", $"condition={conditions[currentGlobalConditionIndex].conditionName} | completedSegment={completedSegmentIndex + 1} | nextSegment={nextSegmentIndex + 1} | nextArc={(int)nextArc} | nextBlock={currentBlockIndex} | blockInNextViewArc={GetBlockNumberWithinCurrentViewArc(currentBlockIndex)}/{blocksPerSet} | totalBlocksInCondition={GetTotalBlocksInCondition()}");
        }
        else
        {
            int completedBlockInArc = GetBlockNumberWithinCurrentViewArc(completedBlock);
            ShowGate(
                $"Condition {conditionNumber} | Viewing range {(int)currentViewArc}°\n" +
                $"Block {completedBlockInArc} of {blocksPerSet} for this viewing range completed.\n" +
                $"Total block {completedBlock} of {GetTotalBlocksInCondition()} in this condition completed.\n\n" +
                $"You may rest as long as you need.\n\n" +
                $"When you are ready to continue, press the Right Trigger to continue."
            );
        }

        waitingForTriggerReleaseBetweenBlocks = true;
        state = TaskState.WaitingBetweenBlocks;
    }

    private void PrepareNextTrialSamePattern()
    {
        if (basePalette == null) basePalette = new List<Color>();
        if (basePalette.Count == 0) basePalette.Add(Color.white);

        if (useFixedBlockPatternTrialOrder)
        {
            currentTargetBallIndex = GetFixedTargetBallForCurrentPatternTrial();
        }
        else
        {
            if (conditionTargetSequence.Count == 0) BuildConditionTargetSequence();
            if (conditionTargetPointer >= conditionTargetSequence.Count) conditionTargetPointer = 0;
            currentTargetBallIndex = conditionTargetSequence[conditionTargetPointer];
            conditionTargetPointer++;
        }


        int colorIndex = currentTaskPos % basePalette.Count;
        currentBaseColor = basePalette[colorIndex];
        currentTargetColor = Color.white;

        for (int i = 0; i < balls.Count; i++)
        {
            bool isTarget = (i == currentTargetBallIndex);
            var ball = balls[i];
            ball.SetTarget(isTarget);
            ball.ApplyColor(isTarget ? currentTargetColor : currentBaseColor);
        }

        trialReadyRT = Time.realtimeSinceStartup;
        SyncLoggerTaskDetailMeta();
        logger?.LogEvent(
            "TRIAL_READY",
            $"set={currentSetIndex} | block={currentBlockIndex} | blockInViewArc={GetBlockNumberWithinCurrentViewArc(currentBlockIndex)}/{blocksPerSet} | viewArc={(int)currentViewArc} | conditionOrderIndex={currentTaskPos + 1} | targetIndex={currentTargetBallIndex} | targetBallOneBased={currentTargetBallIndex + 1} | targetTemplateIndex={(currentTargetTemplateIndex >= 0 ? currentTargetTemplateIndex + 1 : 0)} | targetTemplateName={currentTargetTemplateName} | targetTemplateCycle={currentTargetTemplateCycleNumberForLog} | targetTemplateCycleOrder={BuildTargetTemplateCycleText()} | targetTemplatePathOneBased={BuildTargetPathTextOneBased(activePatternBlockTargetPath)} | baseColorIndex={colorIndex} | targetColor=white"
        );
    }

    private int GetFixedTargetBallForCurrentPatternTrial()
    {
        int ballCount = Mathf.Max(1, balls.Count);

        if (useTargetSearchTemplates)
            return GetTargetBallFromSearchTemplate(ballCount);

        int patternIndex = Mathf.Max(0, currentPatternIndex);
        int trialIndexInPattern = Mathf.Max(0, correctInCurrentPattern);

        int[] order = BuildDeterministicTargetOrder(patternIndex, ballCount);

        currentTargetTemplateIndex = -1;
        currentTargetTemplateName = "LegacyPatternSeededOrder";
        currentTargetTemplateCycleNumberForLog = 0;

        return order[trialIndexInPattern % ballCount];
    }

    private int GetTargetBallFromSearchTemplate(int ballCount)
    {
        InitializeTargetTemplateSystem(false);

        int templateCount = GetValidTargetTemplateCount();
        if (templateCount <= 0)
            return BuildDeterministicTargetOrder(Mathf.Max(0, currentPatternIndex), ballCount)[Mathf.Max(0, correctInCurrentPattern) % ballCount];

        if (activePatternBlockTargetPath == null || activePatternBlockTargetPath.Length == 0)
            SelectTargetTemplateForCurrentPatternBlock();

        if (activePatternBlockTargetPath == null || activePatternBlockTargetPath.Length == 0)
            return BuildDeterministicTargetOrder(Mathf.Max(0, currentPatternIndex), ballCount)[Mathf.Max(0, correctInCurrentPattern) % ballCount];

        int trialIndex = Mathf.Max(0, correctInCurrentPattern);
        int targetBallIndex = activePatternBlockTargetPath[trialIndex % activePatternBlockTargetPath.Length];

        currentTargetTemplateIndex = activePatternBlockTemplateIndex;
        currentTargetTemplateName = activePatternBlockTemplateName;
        currentTargetTemplateCycleNumberForLog = activePatternBlockTemplateCycleNumber;

        return Mathf.Clamp(targetBallIndex, 0, ballCount - 1);
    }

    private void SelectTargetTemplateForCurrentPatternBlock()
    {
        if (!useTargetSearchTemplates)
        {
            activePatternBlockTemplateIndex = -1;
            activePatternBlockTemplateName = "LegacyPatternSeededOrder";
            activePatternBlockTemplateCycleNumber = 0;
            activePatternBlockTargetPath = new int[0];
            return;
        }

        InitializeTargetTemplateSystem(false);

        int ballCount = Mathf.Max(1, balls.Count);
        int templateCount = GetValidTargetTemplateCount();
        if (templateCount <= 0)
        {
            activePatternBlockTemplateIndex = -1;
            activePatternBlockTemplateName = "FallbackPatternSeededOrder";
            activePatternBlockTemplateCycleNumber = 0;
            activePatternBlockTargetPath = BuildDeterministicTargetOrder(Mathf.Max(0, currentPatternIndex), ballCount);
            return;
        }

        int templateIndex = GetNextTargetTemplateIndex(templateCount);
        activePatternBlockTemplateIndex = templateIndex;
        activePatternBlockTemplateName = GetTargetTemplateName(templateIndex);
        activePatternBlockTemplateCycleNumber = targetTemplateCycleNumber;
        activePatternBlockTargetPath = GetTargetTemplatePath(templateIndex, ballCount);

        currentTargetTemplateIndex = activePatternBlockTemplateIndex;
        currentTargetTemplateName = activePatternBlockTemplateName;
        currentTargetTemplateCycleNumberForLog = activePatternBlockTemplateCycleNumber;

        logger?.LogEvent(
            "TARGET_TEMPLATE_SELECTED",
            $"condition={conditions[currentGlobalConditionIndex].conditionName} | set={currentSetIndex} | block={currentBlockIndex} | blockInViewArc={GetBlockNumberWithinCurrentViewArc(currentBlockIndex)}/{blocksPerSet} | viewArc={(int)currentViewArc} | patternIndex={currentPatternIndex} | patternName={patterns[currentPatternIndex].patternName} | templateIndex={activePatternBlockTemplateIndex + 1} | templateName={activePatternBlockTemplateName} | templateCycle={activePatternBlockTemplateCycleNumber} | templateCycleOrder={BuildTargetTemplateCycleText()} | targetOrderOneBased={BuildTargetPathTextOneBased(activePatternBlockTargetPath)}"
        );
    }

    private void InitializeTargetTemplateSystem(bool forceReset)
    {
        if (!forceReset && targetTemplateSystemInitialized) return;

        int templateCount = GetValidTargetTemplateCount();
        if (templateCount <= 0)
        {
            targetTemplateSystemInitialized = true;
            targetTemplateCycle.Clear();
            activePatternBlockTargetPath = new int[0];
            return;
        }

        targetTemplateCycle.Clear();
        targetTemplateCyclePointer = 0;
        targetTemplateCycleNumber = 0;
        lastTargetTemplateIndex = -1;
        activePatternBlockTemplateIndex = -1;
        activePatternBlockTemplateName = "None";
        activePatternBlockTemplateCycleNumber = 0;
        activePatternBlockTargetPath = new int[0];
        targetTemplateSystemInitialized = true;

        BuildNextTargetTemplateCycle(templateCount);
    }

    private int GetValidTargetTemplateCount()
    {
        if (targetSearchTemplates == null) return 0;
        return Mathf.Max(0, targetSearchTemplates.Count);
    }

    private int GetNextTargetTemplateIndex(int templateCount)
    {
        if (targetTemplateCycle.Count == 0 || targetTemplateCyclePointer >= targetTemplateCycle.Count)
            BuildNextTargetTemplateCycle(templateCount);

        if (targetTemplateCycle.Count == 0) return 0;

        int templateIndex = targetTemplateCycle[targetTemplateCyclePointer];
        targetTemplateCyclePointer++;
        lastTargetTemplateIndex = templateIndex;
        return templateIndex;
    }

    private void BuildNextTargetTemplateCycle(int templateCount)
    {
        targetTemplateCycle.Clear();
        targetTemplateCyclePointer = 0;
        targetTemplateCycleNumber++;

        for (int i = 0; i < templateCount; i++)
            targetTemplateCycle.Add(i);


        int seed = targetTemplateCycleSeed + targetTemplateCycleNumber * 7919;
        System.Random rng = new System.Random(seed);
        for (int i = targetTemplateCycle.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            int temp = targetTemplateCycle[i];
            targetTemplateCycle[i] = targetTemplateCycle[j];
            targetTemplateCycle[j] = temp;
        }

        if (targetTemplateCycle.Count > 1 && targetTemplateCycle[0] == lastTargetTemplateIndex)
        {
            int swapIndex = 1;
            int temp = targetTemplateCycle[0];
            targetTemplateCycle[0] = targetTemplateCycle[swapIndex];
            targetTemplateCycle[swapIndex] = temp;
        }
    }

    private int[] GetTargetTemplatePath(int templateIndex, int ballCount)
    {
        ballCount = Mathf.Max(1, ballCount);

        if (targetSearchTemplates != null && templateIndex >= 0 && templateIndex < targetSearchTemplates.Count)
        {
            var raw = targetSearchTemplates[templateIndex]?.targetOrderOneBased;
            int[] sanitized = SanitizeOneBasedTargetOrder(raw, ballCount);
            if (sanitized.Length > 0) return sanitized;
        }

        return BuildFallbackIrregularTargetPath(templateIndex, ballCount);
    }

    private int[] SanitizeOneBasedTargetOrder(List<int> oneBasedOrder, int ballCount)
    {
        if (oneBasedOrder == null || oneBasedOrder.Count == 0)
            return new int[0];

        List<int> result = new List<int>();
        HashSet<int> used = new HashSet<int>();

        foreach (int oneBased in oneBasedOrder)
        {
            int zeroBased = oneBased - 1;
            if (zeroBased < 0 || zeroBased >= ballCount) continue;
            if (used.Contains(zeroBased)) continue;
            used.Add(zeroBased);
            result.Add(zeroBased);
        }


        if (result.Count != ballCount)
            return new int[0];

        return result.ToArray();
    }

    private int[] BuildFallbackIrregularTargetPath(int templateIndex, int ballCount)
    {
        ballCount = Mathf.Max(1, ballCount);
        List<int> order = new List<int>();
        for (int i = 0; i < ballCount; i++) order.Add(i);

        int seed = 3109 + templateIndex * 101 + ballCount * 17;
        System.Random rng = new System.Random(seed);

        int current = Mathf.Clamp(templateIndex % ballCount, 0, ballCount - 1);
        List<int> remaining = new List<int>(order);
        List<int> result = new List<int>();
        result.Add(current);
        remaining.Remove(current);

        while (remaining.Count > 0)
        {
            int bestIndex = 0;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < remaining.Count; i++)
            {
                int candidate = remaining[i];
                float normalizedDistance = Mathf.Abs(candidate - current) / Mathf.Max(1f, ballCount - 1f);
                float effortWeight = Mathf.Lerp(1.4f, 0.25f, templateIndex / 4f);
                float irregularNoise = (float)rng.NextDouble() * 0.35f;
                float centerPenalty = Mathf.Abs(candidate - (ballCount - 1) * 0.5f) / Mathf.Max(1f, ballCount - 1f) * 0.08f;
                float score = normalizedDistance * effortWeight + irregularNoise - centerPenalty;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            current = remaining[bestIndex];
            result.Add(current);
            remaining.RemoveAt(bestIndex);
        }

        return result.ToArray();
    }

    private string GetTargetTemplateName(int templateIndex)
    {
        if (targetSearchTemplates == null || templateIndex < 0 || templateIndex >= targetSearchTemplates.Count)
            return "Template_" + (templateIndex + 1);

        string name = targetSearchTemplates[templateIndex]?.templateName;
        if (string.IsNullOrWhiteSpace(name))
            name = "Template_" + (templateIndex + 1);
        return name;
    }

    private string BuildTargetTemplateCycleText()
    {
        if (targetTemplateCycle == null || targetTemplateCycle.Count == 0) return "";
        List<string> parts = new List<string>();
        foreach (int templateIndex in targetTemplateCycle)
            parts.Add((templateIndex + 1).ToString());
        return string.Join("-", parts);
    }

    private string BuildTargetPathTextOneBased(int[] zeroBasedPath)
    {
        if (zeroBasedPath == null || zeroBasedPath.Length == 0) return "";
        List<string> parts = new List<string>();
        for (int i = 0; i < zeroBasedPath.Length; i++)
            parts.Add((zeroBasedPath[i] + 1).ToString());
        return string.Join("-", parts);
    }

    private int[] BuildDeterministicTargetOrder(int patternIndex, int ballCount)
    {
        int[] order = new int[ballCount];

        for (int i = 0; i < ballCount; i++)
            order[i] = i;

        int seed = 1000 + patternIndex * 97;
        System.Random rng = new System.Random(seed);

        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);

            int temp = order[i];
            order[i] = order[j];
            order[j] = temp;
        }

        return order;
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

        string viewArcText = "";
        if (showViewArcInConditionLabel && enableViewArcSegments)
            viewArcText = $" | View: {(int)currentViewArc}°";

        conditionLabelText.text = $"{prefix} {cond.posture} Mode{viewArcText}";
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
            Vector3 arcOffset = enableViewArcSegments
                ? RemapOffsetIntoCurrentViewArc(transformedOffset, i, balls.Count)
                : transformedOffset;

            if (hasConditionAnchor)
            {
                Vector3 horizontalOffset;
                if (useBaselineYawForBallPlacement)
                    horizontalOffset = conditionRightFlat * arcOffset.x + conditionForwardFlat * arcOffset.z;
                else
                    horizontalOffset = new Vector3(arcOffset.x, 0f, arcOffset.z);

                Vector3 worldPos = conditionAnchor + horizontalOffset + Vector3.up * arcOffset.y;
                balls[i].transform.position = worldPos;
            }
            else
            {
                balls[i].transform.localPosition = arcOffset;
            }
        }
    }


    private void BuildCurrentConditionViewArcOrder()
    {
        currentConditionViewArcOrder.Clear();
        currentConditionViewArcOrder.AddRange(GetViewArcOrder(GetResolvedViewArcOrderPreset()));
        if (currentConditionViewArcOrder.Count == 0)
        {
            currentConditionViewArcOrder.Add(ViewArcMode.Arc90);
            currentConditionViewArcOrder.Add(ViewArcMode.Arc180);
            currentConditionViewArcOrder.Add(ViewArcMode.Arc360);
        }

        currentViewArcSegmentIndex = 0;
        currentViewArc = currentConditionViewArcOrder[0];

    }

    private ViewArcOrderPreset GetResolvedViewArcOrderPreset()
    {
        if (viewArcOrderPreset != ViewArcOrderPreset.MatchConditionSequencePreset)
            return viewArcOrderPreset;

        switch (conditionSequencePreset)
        {
            case ConditionSequencePreset.P1: return ViewArcOrderPreset.V1_90_180_360;
            case ConditionSequencePreset.P2: return ViewArcOrderPreset.V2_90_360_180;
            case ConditionSequencePreset.P3: return ViewArcOrderPreset.V3_180_90_360;
            case ConditionSequencePreset.P4: return ViewArcOrderPreset.V4_180_360_90;
            case ConditionSequencePreset.P5: return ViewArcOrderPreset.V5_360_90_180;
            case ConditionSequencePreset.P6: return ViewArcOrderPreset.V6_360_180_90;
            default: return ViewArcOrderPreset.V1_90_180_360;
        }
    }

    private List<ViewArcMode> GetViewArcOrder(ViewArcOrderPreset preset)
    {
        switch (preset)
        {
            case ViewArcOrderPreset.V1_90_180_360:
                return new List<ViewArcMode> { ViewArcMode.Arc90, ViewArcMode.Arc180, ViewArcMode.Arc360 };
            case ViewArcOrderPreset.V2_90_360_180:
                return new List<ViewArcMode> { ViewArcMode.Arc90, ViewArcMode.Arc360, ViewArcMode.Arc180 };
            case ViewArcOrderPreset.V3_180_90_360:
                return new List<ViewArcMode> { ViewArcMode.Arc180, ViewArcMode.Arc90, ViewArcMode.Arc360 };
            case ViewArcOrderPreset.V4_180_360_90:
                return new List<ViewArcMode> { ViewArcMode.Arc180, ViewArcMode.Arc360, ViewArcMode.Arc90 };
            case ViewArcOrderPreset.V5_360_90_180:
                return new List<ViewArcMode> { ViewArcMode.Arc360, ViewArcMode.Arc90, ViewArcMode.Arc180 };
            case ViewArcOrderPreset.V6_360_180_90:
                return new List<ViewArcMode> { ViewArcMode.Arc360, ViewArcMode.Arc180, ViewArcMode.Arc90 };
            default:
                return new List<ViewArcMode> { ViewArcMode.Arc90, ViewArcMode.Arc180, ViewArcMode.Arc360 };
        }
    }

    private int GetViewArcSegmentCount()
    {
        return Mathf.Max(1, currentConditionViewArcOrder.Count);
    }

    private int GetTotalBlocksInCondition()
    {
        int blocksPerArc = Mathf.Max(1, blocksPerSet);
        if (!enableViewArcSegments) return blocksPerArc;
        return blocksPerArc * GetViewArcSegmentCount();
    }

    private int GetViewArcSegmentIndexForBlock(int blockIndex)
    {
        if (!enableViewArcSegments || blocksPerSet <= 0) return 0;

        int blocksPerArc = Mathf.Max(1, blocksPerSet);
        int zeroBasedBlock = Mathf.Max(0, blockIndex - 1);
        int segmentIndex = zeroBasedBlock / blocksPerArc;
        return Mathf.Clamp(segmentIndex, 0, GetViewArcSegmentCount() - 1);
    }

    private int GetBlockNumberWithinCurrentViewArc(int blockIndex)
    {
        int blocksPerArc = Mathf.Max(1, blocksPerSet);
        return ((Mathf.Max(1, blockIndex) - 1) % blocksPerArc) + 1;
    }

    private ViewArcMode GetViewArcForSegmentIndex(int segmentIndex)
    {
        if (currentConditionViewArcOrder.Count == 0)
            BuildCurrentConditionViewArcOrder();

        segmentIndex = Mathf.Clamp(segmentIndex, 0, currentConditionViewArcOrder.Count - 1);
        return currentConditionViewArcOrder[segmentIndex];
    }

    private void UpdateCurrentViewArcForBlock()
    {
        if (!enableViewArcSegments)
        {
            currentViewArcSegmentIndex = 0;
            currentViewArc = ViewArcMode.Arc360;
            SyncLoggerTaskDetailMeta();
            return;
        }

        if (currentConditionViewArcOrder.Count == 0)
            BuildCurrentConditionViewArcOrder();

        currentViewArcSegmentIndex = GetViewArcSegmentIndexForBlock(currentBlockIndex);
        currentViewArc = GetViewArcForSegmentIndex(currentViewArcSegmentIndex);
        SyncLoggerTaskDetailMeta();
    }

    private string BuildViewArcOrderText(List<ViewArcMode> order)
    {
        if (order == null || order.Count == 0) return "";
        List<string> parts = new List<string>();
        foreach (ViewArcMode arc in order)
            parts.Add(((int)arc).ToString());
        return string.Join("->", parts);
    }

    private string FormatViewArcTransitionMessage(ConditionConfig cond, int completedSegment, int totalSegments, ViewArcMode nextArc, int nextBlock)
    {
        string message = string.IsNullOrWhiteSpace(viewArcTransitionMessage)
            ? "This viewing range is complete.\n\nNext viewing range: {nextArc}°.\n\nWhen you are ready to continue, press the Right Trigger."
            : viewArcTransitionMessage;

        string conditionName = cond != null ? cond.conditionName : "";
        message = message.Replace("{condition}", conditionName);
        message = message.Replace("{completedSegment}", completedSegment.ToString());
        message = message.Replace("{totalSegments}", totalSegments.ToString());
        message = message.Replace("{nextArc}", ((int)nextArc).ToString());
        message = message.Replace("{nextBlock}", nextBlock.ToString());
        message = message.Replace("{blocksPerSet}", blocksPerSet.ToString());
        message = message.Replace("{totalBlocksInCondition}", GetTotalBlocksInCondition().ToString());
        return message;
    }

    private void LogCurrentViewArcSegment(string eventName)
    {
        if (!enableViewArcSegments) return;
        logger?.LogEvent(eventName, $"condition={conditions[currentGlobalConditionIndex].conditionName} | segment={currentViewArcSegmentIndex + 1}/{GetViewArcSegmentCount()} | viewArc={(int)currentViewArc} | blockInViewArc={GetBlockNumberWithinCurrentViewArc(currentBlockIndex)}/{blocksPerSet} | totalBlock={currentBlockIndex}/{GetTotalBlocksInCondition()}");
    }

    private void SyncLoggerTaskDetailMeta()
    {
        if (logger == null) return;

        logger.SetViewArcMeta(
            (int)currentViewArc,
            enableViewArcSegments ? currentViewArcSegmentIndex + 1 : 0,
            GetBlockNumberWithinCurrentViewArc(currentBlockIndex),
            Mathf.Max(1, blocksPerSet),
            GetTotalBlocksInCondition()
        );

        int templateIndexOneBased = currentTargetTemplateIndex >= 0 ? currentTargetTemplateIndex + 1 : 0;
        int targetBallOneBased = currentTargetBallIndex >= 0 ? currentTargetBallIndex + 1 : 0;

        logger.SetTargetTemplateMeta(
            templateIndexOneBased,
            currentTargetTemplateName,
            targetBallOneBased
        );

        string patternName = "None";
        if (patterns != null && currentPatternIndex >= 0 && currentPatternIndex < patterns.Count && patterns[currentPatternIndex] != null)
            patternName = patterns[currentPatternIndex].patternName;

        logger.SetPatternTrialMeta(
            currentPatternIndex + 1,
            patternName,
            Mathf.Max(0, correctInCurrentPattern) + 1
        );
    }


    private Vector3 RemapOffsetIntoCurrentViewArc(Vector3 local, int ballIndex, int ballCount)
    {
        // Important design choice:
        // - Pattern Z controls distance from the user/baseline anchor.
        // - Pattern Y controls vertical placement.
        // - Pattern X is ignored during view-arc segmentation, so the pattern no longer controls left/right spacing.
        // - Left/right angular spread is generated automatically from the current view arc: 90, 180, or 360 degrees.
        float arcDegrees = Mathf.Clamp((float)currentViewArc, 1f, 360f);
        int count = Mathf.Max(1, ballCount);

        float radius = Mathf.Abs(local.z);
        radius = Mathf.Max(minViewArcRadius, radius);

        float angleDegrees;
        if (arcDegrees >= 359.9f)
        {
            // Full circle: distribute balls evenly around the user.
            float step = 360f / count;
            angleDegrees = -180f + step * ballIndex;
        }
        else
        {
            // Front arc: distribute balls evenly inside [-arc/2, +arc/2].
            // Edge padding can keep balls away from the exact arc boundary.
            float halfArc = arcDegrees * 0.5f;
            float padding = Mathf.Clamp(viewArcEdgePaddingDegrees, 0f, Mathf.Max(0f, halfArc - 0.01f));
            float left = -halfArc + padding;
            float right = halfArc - padding;

            if (count == 1)
                angleDegrees = 0f;
            else
            {
                float t = ballIndex / (float)(count - 1);
                angleDegrees = Mathf.Lerp(left, right, t);
            }
        }

        float rad = angleDegrees * Mathf.Deg2Rad;
        float x = Mathf.Sin(rad) * radius;
        float z = Mathf.Cos(rad) * radius;

        return new Vector3(x, local.y, z);
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
        if (countdownGate != null) countdownGate.gameObject.SetActive(false);
        if (baselineFixationTarget != null) baselineFixationTarget.SetActive(false);
    }
}
