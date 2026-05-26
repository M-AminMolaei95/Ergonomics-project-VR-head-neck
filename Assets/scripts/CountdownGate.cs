using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class CountdownGate : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text countdownText;
    public GameObject countdownRoot;

    [Header("Optional posture guide")]
    [Tooltip("Optional posture guide image. It can be shown before baseline and during baseline countdown.")]
    public Image postureGuideImage;

    [Tooltip("Sprite used for the optimal baseline / posture guide image.")]
    public Sprite postureGuideSprite;

    public bool showPostureGuide = true;

    [Header("Countdown")]
    public int countdownSeconds = 10;

    public void ShowReadyMessage()
    {
        SetReadyPostureGuideVisible(true);
    }

    public void SetReadyPostureGuideVisible(bool visible)
    {
        if (countdownRoot != null)
            countdownRoot.SetActive(visible);

        if (countdownText != null)
            countdownText.text = "";

        SetPostureGuideVisible(visible);
    }

    public void SetPostureGuideVisible(bool visible)
    {
        bool shouldShow = visible && showPostureGuide;

        if (postureGuideImage == null)
            return;

        if (postureGuideSprite != null)
            postureGuideImage.sprite = postureGuideSprite;

        postureGuideImage.enabled = shouldShow;
        postureGuideImage.gameObject.SetActive(shouldShow);
    }

    public void HideAll()
    {
        if (countdownText != null)
            countdownText.text = "";

        SetPostureGuideVisible(false);

        if (countdownRoot != null)
            countdownRoot.SetActive(false);
    }

    public IEnumerator RunCountdownImmediate()
    {
        if (countdownRoot != null)
            countdownRoot.SetActive(true);

        // Show schematic during the actual baseline countdown.
        SetPostureGuideVisible(true);

        for (int t = countdownSeconds; t > 0; t--)
        {
            if (countdownText != null)
                countdownText.text = $"\n\n Keep a comfortable, straight, and upright posture \nBaseline is now recording...\nTask starting in: {t}";

            yield return new WaitForSeconds(1f);
        }

        if (countdownText != null)
            countdownText.text = "Starting...";

        HideAll();
    }

    private void OnDisable()
    {
        SetPostureGuideVisible(false);
    }
}
