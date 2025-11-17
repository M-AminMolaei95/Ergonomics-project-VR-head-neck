using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class PostureNeck : MonoBehaviour
{
    [Header("References")]
    public PoseManager1 poseManager;

    [Header("UI Elements")]
    public TextMeshProUGUI feedbackText;
    public CanvasGroup textGroup;
    public Image feedbackIcon;
    public CanvasGroup iconGroup;

    [Header("Sprites for each posture")]
    public Sprite forwardSprite;
    public Sprite backwardSprite;
    public Sprite slouchSprite;
    public Sprite lateralLeftSprite;
    public Sprite lateralRightSprite;

    [Header("Angle thresholds (degrees)")]
    public float forwardThreshold = 20f;
    public float backwardThreshold = 15f;
    public float lateralThreshold = 20f;   

    [Header("Neutral band for slouch (degrees)")]
    public float pitchNeutralBand = 12f;

    [Header("Height threshold (meters)")]
    public float slouchThreshold = 0.03f;

    [Header("Hold time (seconds)")]
    public float holdTime = 3f;

    [Header("Fade settings")]
    public float fadeDuration = 0.5f;

    [HideInInspector] public string CurrentPosture = "Neutral";
    [HideInInspector] public int currentRulaScore = 1;

    private float tForward, tBackward, tSlouch, tLateral;
    private bool feedbackVisible = false;
    private Coroutine fadeTextCoroutine;
    private Coroutine fadeIconCoroutine;

    void Update()
    {
        if (!poseManager) return;

        float pitch = poseManager.normalizedPitch;
        float height = poseManager.normalizedHeight;
        float roll = poseManager.normalizedRoll;

        bool isBackward = pitch < -backwardThreshold;
        bool isForward = pitch > forwardThreshold;
        bool isWithinNeutral = Mathf.Abs(pitch) <= pitchNeutralBand;

        bool isLateralLeft =
            (roll < -lateralThreshold) &&
            Mathf.Abs(pitch) < 15f;

        bool isLateralRight =
            (roll > +lateralThreshold) &&
            Mathf.Abs(pitch) < 15f;

        bool isLateral = isLateralLeft || isLateralRight;

        bool isSlouch =
            (height < -slouchThreshold) &&
            Mathf.Abs(pitch) <= pitchNeutralBand &&
            Mathf.Abs(roll) < lateralThreshold * 0.5f;

        if (isLateral)
            isSlouch = false;   
  
        int baseScore = 1;

        if (isBackward) baseScore = 4;
        else if (pitch > forwardThreshold) baseScore = 3;
        else if (pitch > pitchNeutralBand) baseScore = 2;

        if (isSlouch && baseScore < 3)
            baseScore = 3;

        int adjust = isLateral ? 1 : 0;

        currentRulaScore = Mathf.Clamp(baseScore + adjust, 1, 7);

        CurrentPosture = "Neutral";

        if (isBackward)
        {
            CurrentPosture = "Backward";
            tBackward += Time.deltaTime;
            ResetTimersExcept("Backward");

            if (tBackward >= holdTime)
                ShowSpecificFeedback("Backward", currentRulaScore);
            else HideFeedback();
            return;
        }

        if (isForward)
        {
            CurrentPosture = "Forward";
            tForward += Time.deltaTime;
            ResetTimersExcept("Forward");

            if (tForward >= holdTime)
                ShowSpecificFeedback("Forward", currentRulaScore);
            else HideFeedback();
            return;
        }

        if (isSlouch)
        {
            CurrentPosture = "Slouch";
            tSlouch += Time.deltaTime;
            ResetTimersExcept("Slouch");

            if (tSlouch >= holdTime)
                ShowSpecificFeedback("Slouch", currentRulaScore);
            else HideFeedback();
            return;
        }

        if (isLateral)
        {
            tLateral += Time.deltaTime;
            ResetTimersExcept("Lateral");

            if (isLateralLeft) CurrentPosture = "LateralLeft";
            if (isLateralRight) CurrentPosture = "LateralRight";

            if (tLateral >= holdTime)
                ShowSpecificFeedback(CurrentPosture, currentRulaScore);
            else HideFeedback();
            return;
        }

        ResetAllTimers();
        HideFeedback();
    }


    void ResetTimersExcept(string active)
    {
        if (active != "Forward") tForward = 0f;
        if (active != "Backward") tBackward = 0f;
        if (active != "Slouch") tSlouch = 0f;
        if (active != "Lateral") tLateral = 0f;
    }

    void ResetAllTimers()
    {
        tForward = tBackward = tSlouch = tLateral = 0f;
    }


    void ShowSpecificFeedback(string cause, int rula)
    {
        string msg = "";
        Sprite icon = null;

        switch (cause)
        {
            case "Forward":
                msg = $"Neck too far forward!\nRelax your posture.";
                icon = forwardSprite;
                break;

            case "Backward":
                msg = $"Neck too far backward!\nReturn to neutral.";
                icon = backwardSprite;
                break;

            case "Slouch":
                msg = $"Head lowered (possible slouch).\nStraighten up.";
                icon = slouchSprite;
                break;

            case "LateralLeft":
                msg = $"Neck tilted LEFT!";
                icon = lateralLeftSprite;
                break;

            case "LateralRight":
                msg = $"Neck tilted RIGHT!";
                icon = lateralRightSprite;
                break;
        }

        ShowFeedback(msg, icon);
    }


    void ShowFeedback(string message, Sprite iconSprite)
    {
        if (feedbackText)
        {
            feedbackText.text = message;
            feedbackText.gameObject.SetActive(true);

            if (fadeTextCoroutine != null) StopCoroutine(fadeTextCoroutine);
            fadeTextCoroutine = StartCoroutine(FadeCanvasGroup(textGroup, 1f, fadeDuration));
        }

        if (feedbackIcon)
        {
            if (iconSprite != null)
                feedbackIcon.sprite = iconSprite;

            feedbackIcon.gameObject.SetActive(true);

            if (fadeIconCoroutine != null) StopCoroutine(fadeIconCoroutine);
            fadeIconCoroutine = StartCoroutine(FadeCanvasGroup(iconGroup, 1f, fadeDuration));
        }

        feedbackVisible = true;
    }


    void HideFeedback()
    {
        if (!feedbackVisible) return;

        if (fadeTextCoroutine != null) StopCoroutine(fadeTextCoroutine);
        fadeTextCoroutine = StartCoroutine(FadeCanvasGroup(textGroup, 0f, fadeDuration));

        if (fadeIconCoroutine != null) StopCoroutine(fadeIconCoroutine);
        fadeIconCoroutine = StartCoroutine(FadeCanvasGroup(iconGroup, 0f, fadeDuration));

        feedbackVisible = false;
    }


    IEnumerator FadeCanvasGroup(CanvasGroup group, float targetAlpha, float duration)
    {
        if (group == null) yield break;

        float startAlpha = group.alpha;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            group.alpha = Mathf.Lerp(startAlpha, targetAlpha, t / duration);
            yield return null;
        }

        group.alpha = targetAlpha;
    }
}
