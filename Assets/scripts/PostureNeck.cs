using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class PostureNeck : MonoBehaviour
{
    public enum FeedbackDisplayMode
    {
        ForwardAndSlouchOnly,
        ForwardSlouchBackwardTilt
    }

    [Header("References")]
    public PoseManager1 poseManager;
    public Transform headTransform;

    [Header("UI Elements")]
    public TextMeshProUGUI feedbackText;
    public CanvasGroup textGroup;
    public Image feedbackIcon;
    public CanvasGroup iconGroup;

    [Header("Display Mode")]
    public FeedbackDisplayMode displayMode = FeedbackDisplayMode.ForwardAndSlouchOnly;

    [Header("Mode 1 Sprites (Forward + Neutral + Slouch only)")]
    public Sprite mode1ForwardHighSprite;
    public Sprite mode1ForwardMidSprite;
    public Sprite mode1ForwardLowSprite;
    public Sprite mode1NeutralSprite;
    public Sprite mode1SlouchSprite;

    [Header("Mode 2 Sprites (Forward + Neutral + Slouch + Backward + Tilt)")]
    public Sprite mode2ForwardHighSprite;
    public Sprite mode2ForwardMidSprite;
    public Sprite mode2ForwardLowSprite;
    public Sprite mode2NeutralSprite;
    public Sprite mode2BackwardSprite;
    public Sprite mode2LateralRightSprite;
    public Sprite mode2LateralLeftSprite;
    public Sprite mode2LateralNeutralSprite;
    public Sprite mode2SlouchSprite;

    [Header("Display thresholds (degrees)")]
    public float forwardHighMin = 20f;
    public float forwardMidMin = 10f;
    public float forwardLowMin = 5f;
    public float neutralMin = -10f;
    public float neutralMax = 3f;
    public float lateralDisplayThreshold = 5f;
    public float lateralNeutralHoldSeconds = 1.5f;

    [Header("Neutral display hold (forward/backward return)")]
    public float neutralHoldSeconds = 1.5f;

    [Header("Slouch detection (meters)")]
    public float slouchHeightDropThreshold = 0.03f;
    public float slouchMaxPitchForDisplay = 5f;

    [Header("Text (optional)")]
    public bool showText = false;

    [Header("Fade Settings")]
    public float fadeDuration = 0.15f;

    private Coroutine fadeTextCoroutine;
    private Coroutine fadeIconCoroutine;
    private bool feedbackVisible = false;

    private string _lastMessage = null;
    private Sprite _lastSprite = null;

    private bool _wasLateral = false;
    private float _lateralNeutralUntil = 0f;

    private bool _wasCalibratedLastFrame = false;
    private bool _hasBaselineHeadHeight = false;
    private float _baselineHeadHeight = 0f;

    private bool _wasInPitchNeutral = false;
    private float _pitchNeutralUntil = 0f;

    public void SetDisplayMode(FeedbackDisplayMode mode)
    {
        displayMode = mode;

        _wasLateral = false;
        _lateralNeutralUntil = 0f;
        _wasInPitchNeutral = false;
        _pitchNeutralUntil = 0f;
        _wasCalibratedLastFrame = false;
        _hasBaselineHeadHeight = false;

        HideFeedback();
    }

    private void Update()
    {
        if (poseManager == null || !poseManager.IsCalibrated)
        {
            HideFeedback();
            _wasLateral = false;
            _lateralNeutralUntil = 0f;
            _wasCalibratedLastFrame = false;
            _hasBaselineHeadHeight = false;
            _wasInPitchNeutral = false;
            _pitchNeutralUntil = 0f;
            return;
        }

        if (!_wasCalibratedLastFrame)
        {
            CaptureBaselineHeadHeight();
            _wasCalibratedLastFrame = true;
        }

        float pitch = poseManager.normalizedPitch;
        float roll = poseManager.normalizedRoll;

        bool modeHasTiltAndBackward = (displayMode == FeedbackDisplayMode.ForwardSlouchBackwardTilt);

        bool isLateralRight = roll > lateralDisplayThreshold;
        bool isLateralLeft = roll < -lateralDisplayThreshold;
        bool isPitchNeutral = (pitch >= neutralMin && pitch <= neutralMax) || (pitch > neutralMax && pitch < forwardLowMin);

        if (modeHasTiltAndBackward && isLateralRight)
        {
            _wasLateral = true;
            _lateralNeutralUntil = 0f;
            _wasInPitchNeutral = false;
            _pitchNeutralUntil = 0f;

            Sprite lateralRight = GetLateralRightSprite();
            if (lateralRight != null)
            {
                ShowIfChanged(lateralRight, showText ? "" : "");
                return;
            }
        }

        if (modeHasTiltAndBackward && isLateralLeft)
        {
            _wasLateral = true;
            _lateralNeutralUntil = 0f;
            _wasInPitchNeutral = false;
            _pitchNeutralUntil = 0f;

            Sprite lateralLeft = GetLateralLeftSprite();
            if (lateralLeft != null)
            {
                ShowIfChanged(lateralLeft, showText ? "" : "");
                return;
            }
        }

        if (_wasLateral)
        {
            _wasLateral = false;
            _lateralNeutralUntil = Time.time + Mathf.Max(0f, lateralNeutralHoldSeconds);
            _wasInPitchNeutral = isPitchNeutral;
            _pitchNeutralUntil = 0f;
        }

        if (IsSlouching(pitch))
        {
            _wasInPitchNeutral = false;
            _pitchNeutralUntil = 0f;

            Sprite slouch = GetSlouchSprite();
            if (slouch != null)
            {
                ShowIfChanged(slouch, showText ? "Slouching!!!" : "");
                return;
            }
        }

        if (modeHasTiltAndBackward &&
            _lateralNeutralUntil > 0f &&
            Time.time < _lateralNeutralUntil)
        {
            Sprite lateralNeutral = GetLateralNeutralSprite();
            if (lateralNeutral != null)
            {
                _wasInPitchNeutral = isPitchNeutral;
                _pitchNeutralUntil = 0f;

                ShowIfChanged(lateralNeutral, showText ? "" : "");
                return;
            }
        }

        if (isPitchNeutral && !_wasInPitchNeutral)
        {
            _pitchNeutralUntil = Time.time + Mathf.Max(0f, neutralHoldSeconds);
        }

        _wasInPitchNeutral = isPitchNeutral;

        Sprite sprite = null;
        string msg = "";

        if (pitch > forwardHighMin)
        {
            _pitchNeutralUntil = 0f;
            sprite = GetForwardHighSprite();
        }
        else if (pitch >= forwardMidMin)
        {
            _pitchNeutralUntil = 0f;
            sprite = GetForwardMidSprite();
        }
        else if (pitch >= forwardLowMin)
        {
            _pitchNeutralUntil = 0f;
            sprite = GetForwardLowSprite();
        }
        else if (pitch < neutralMin)
        {
            _pitchNeutralUntil = 0f;

            if (modeHasTiltAndBackward)
                sprite = GetBackwardSprite();
        }
        else if (isPitchNeutral)
        {
            if (_pitchNeutralUntil > 0f && Time.time < _pitchNeutralUntil)
                sprite = GetNeutralSprite();
        }

        if (sprite != null || (feedbackText != null && showText))
            ShowIfChanged(sprite, msg);
        else
            HideFeedback();
    }

    private Sprite GetForwardHighSprite()
    {
        return displayMode == FeedbackDisplayMode.ForwardAndSlouchOnly
            ? mode1ForwardHighSprite
            : mode2ForwardHighSprite;
    }

    private Sprite GetForwardMidSprite()
    {
        return displayMode == FeedbackDisplayMode.ForwardAndSlouchOnly
            ? mode1ForwardMidSprite
            : mode2ForwardMidSprite;
    }

    private Sprite GetForwardLowSprite()
    {
        return displayMode == FeedbackDisplayMode.ForwardAndSlouchOnly
            ? mode1ForwardLowSprite
            : mode2ForwardLowSprite;
    }

    private Sprite GetNeutralSprite()
    {
        return displayMode == FeedbackDisplayMode.ForwardAndSlouchOnly
            ? mode1NeutralSprite
            : mode2NeutralSprite;
    }

    private Sprite GetBackwardSprite()
    {
        return displayMode == FeedbackDisplayMode.ForwardSlouchBackwardTilt
            ? mode2BackwardSprite
            : null;
    }

    private Sprite GetLateralRightSprite()
    {
        return displayMode == FeedbackDisplayMode.ForwardSlouchBackwardTilt
            ? mode2LateralRightSprite
            : null;
    }

    private Sprite GetLateralLeftSprite()
    {
        return displayMode == FeedbackDisplayMode.ForwardSlouchBackwardTilt
            ? mode2LateralLeftSprite
            : null;
    }

    private Sprite GetLateralNeutralSprite()
    {
        return displayMode == FeedbackDisplayMode.ForwardSlouchBackwardTilt
            ? mode2LateralNeutralSprite
            : null;
    }

    private Sprite GetSlouchSprite()
    {
        return displayMode == FeedbackDisplayMode.ForwardAndSlouchOnly
            ? mode1SlouchSprite
            : mode2SlouchSprite;
    }

    private void CaptureBaselineHeadHeight()
    {
        if (headTransform == null)
        {
            _hasBaselineHeadHeight = false;
            Debug.LogWarning("PostureNeck: headTransform is not assigned. Slouch feedback cannot be computed.");
            return;
        }

        _baselineHeadHeight = headTransform.position.y;
        _hasBaselineHeadHeight = true;
    }

    private bool IsSlouching(float pitch)
    {
        if (!_hasBaselineHeadHeight || headTransform == null)
            return false;

        float currentHeadHeight = headTransform.position.y;
        float heightDrop = _baselineHeadHeight - currentHeadHeight;

        bool enoughHeightDrop = heightDrop > slouchHeightDropThreshold;
        bool pitchStillNearNeutral = pitch <= slouchMaxPitchForDisplay;

        return enoughHeightDrop && pitchStillNearNeutral;
    }

    private void ShowIfChanged(Sprite sprite, string message)
    {
        if (_lastSprite == sprite && _lastMessage == message && feedbackVisible)
            return;

        _lastSprite = sprite;
        _lastMessage = message;

        ShowFeedback(message, sprite);
    }

    private void ShowFeedback(string message, Sprite iconSprite)
    {
        if (feedbackText != null)
        {
            feedbackText.text = message ?? "";
            feedbackText.gameObject.SetActive(true);

            if (fadeTextCoroutine != null)
                StopCoroutine(fadeTextCoroutine);

            float targetTextAlpha = (showText && !string.IsNullOrEmpty(message)) ? 1f : 0f;
            fadeTextCoroutine = StartCoroutine(Fade(textGroup, targetTextAlpha, fadeDuration));
        }

        if (feedbackIcon != null)
        {
            feedbackIcon.sprite = iconSprite;
            feedbackIcon.gameObject.SetActive(iconSprite != null);

            if (fadeIconCoroutine != null)
                StopCoroutine(fadeIconCoroutine);

            float targetIconAlpha = iconSprite != null ? 1f : 0f;
            fadeIconCoroutine = StartCoroutine(Fade(iconGroup, targetIconAlpha, fadeDuration));
        }

        feedbackVisible = true;
    }

    private void HideFeedback()
    {
        if (fadeTextCoroutine != null)
            StopCoroutine(fadeTextCoroutine);

        if (fadeIconCoroutine != null)
            StopCoroutine(fadeIconCoroutine);

        if (textGroup != null)
            textGroup.alpha = 0f;

        if (iconGroup != null)
            iconGroup.alpha = 0f;

        if (feedbackText != null)
        {
            feedbackText.text = "";
            feedbackText.gameObject.SetActive(false);
        }

        if (feedbackIcon != null)
        {
            feedbackIcon.sprite = null;
            feedbackIcon.gameObject.SetActive(false);
        }

        _lastSprite = null;
        _lastMessage = null;
        feedbackVisible = false;
    }

    private IEnumerator Fade(CanvasGroup group, float target, float duration)
    {
        if (group == null)
            yield break;

        float start = group.alpha;
        float t = 0f;

        if (duration <= 0f)
        {
            group.alpha = target;
            yield break;
        }

        while (t < duration)
        {
            t += Time.deltaTime;
            group.alpha = Mathf.Lerp(start, target, t / duration);
            yield return null;
        }

        group.alpha = target;
    }
}
