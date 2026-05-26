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

    [Header("Feedback Delay")]
    public float badPostureFeedbackDelaySeconds = 3f;

    private Coroutine fadeTextCoroutine;
    private Coroutine fadeIconCoroutine;
    private bool feedbackVisible = false;

    private string _currentBadPostureKey = null;
    private float _badPostureStartTime = -1f;
    private bool _badPostureFeedbackWasShown = false;

    private string _lastMessage = null;
    private Sprite _lastSprite = null;

    private bool _wasLateral = false;
    private float _lateralNeutralUntil = 0f;

    private bool _wasCalibratedLastFrame = false;
    private bool _hasBaselineHeadHeight = false;
    private float _baselineHeadHeight = 0f;

    private bool _wasInPitchNeutral = false;
    private float _pitchNeutralUntil = 0f;
    private bool _wasForwardOrSlouchBeforeNeutral = false;

    public void SetDisplayMode(FeedbackDisplayMode mode)
    {
        displayMode = mode;

        _wasLateral = false;
        _lateralNeutralUntil = 0f;
        _wasInPitchNeutral = false;
        _pitchNeutralUntil = 0f;
        _wasForwardOrSlouchBeforeNeutral = false;
        _wasCalibratedLastFrame = false;
        _hasBaselineHeadHeight = false;
        ResetBadPostureDelay();

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
            _wasForwardOrSlouchBeforeNeutral = false;
            ResetBadPostureDelay();
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
                ShowBadPostureIfDelayElapsed("LATERAL_RIGHT", lateralRight, showText ? "" : "");
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
                ShowBadPostureIfDelayElapsed("LATERAL_LEFT", lateralLeft, showText ? "" : "");
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
            _wasForwardOrSlouchBeforeNeutral = true;

            Sprite slouch = GetSlouchSprite();
            if (slouch != null)
            {
                ShowBadPostureIfDelayElapsed("SLOUCH", slouch, showText ? "Slouching!" : "");
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
            // Show the optimal/neutral icon only if the bad-posture feedback had actually appeared.
            // If the user was non-optimal for less than the delay time, do NOT show neutral feedback.
            if (_badPostureFeedbackWasShown && (modeHasTiltAndBackward || _wasForwardOrSlouchBeforeNeutral))
                _pitchNeutralUntil = Time.time + Mathf.Max(0f, neutralHoldSeconds);
            else
                _pitchNeutralUntil = 0f;
        }

        _wasInPitchNeutral = isPitchNeutral;

        if (isPitchNeutral)
        {
            _wasForwardOrSlouchBeforeNeutral = false;

            // Once we enter neutral and decide whether to show the neutral icon,
            // clear the delayed-bad-posture state so short future deviations start fresh.
            if (_pitchNeutralUntil <= 0f)
                ResetBadPostureDelay();
        }

        Sprite sprite = null;
        string msg = "";

        if (pitch > forwardHighMin)
        {
            _pitchNeutralUntil = 0f;
            _wasForwardOrSlouchBeforeNeutral = true;
            sprite = GetForwardHighSprite();
        }
        else if (pitch >= forwardMidMin)
        {
            _pitchNeutralUntil = 0f;
            _wasForwardOrSlouchBeforeNeutral = true;
            sprite = GetForwardMidSprite();
        }
        else if (pitch >= forwardLowMin)
        {
            _pitchNeutralUntil = 0f;
            _wasForwardOrSlouchBeforeNeutral = true;
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

        bool isBadPitchPosture =
            pitch > forwardHighMin ||
            pitch >= forwardMidMin ||
            pitch >= forwardLowMin ||
            (modeHasTiltAndBackward && pitch < neutralMin);

        if (sprite != null && isBadPitchPosture)
        {
            string key;

            // Treat all forward leaning levels as ONE continuous bad-posture state.
            // This means:
            // - low/mid/high forward changes do NOT reset the 3-second timer.
            // - once feedback appears, it stays visible while the user remains in any forward-leaning state.
            // - feedback resets only after returning to the optimal/neutral range.
            if (pitch >= forwardLowMin)
                key = "FORWARD";
            else
                key = "BACKWARD";

            ShowBadPostureIfDelayElapsed(key, sprite, msg);
        }
        else if (sprite != null)
        {
            // This is neutral/optimal feedback. It is only reached when _pitchNeutralUntil is active,
            // which now only happens after delayed bad-posture feedback was actually shown.
            ShowIfChanged(sprite, msg);
        }
        else
        {
            ResetBadPostureDelay();
            HideFeedback();
        }
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

    private void ShowBadPostureIfDelayElapsed(string postureKey, Sprite sprite, string message)
    {
        if (string.IsNullOrEmpty(postureKey) || sprite == null)
        {
            ResetBadPostureDelay();
            HideFeedback();
            return;
        }

        if (_currentBadPostureKey != postureKey)
        {
            _currentBadPostureKey = postureKey;
            _badPostureStartTime = Time.time;
            HideFeedback();
            return;
        }

        float elapsed = Time.time - _badPostureStartTime;

        if (elapsed >= Mathf.Max(0f, badPostureFeedbackDelaySeconds))
        {
            _badPostureFeedbackWasShown = true;
            ShowIfChanged(sprite, message);
        }
        else
        {
            HideFeedback();
        }
    }

    private void ResetBadPostureDelay()
    {
        _currentBadPostureKey = null;
        _badPostureStartTime = -1f;
        _badPostureFeedbackWasShown = false;
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


