using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class CountdownGate : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text countdownText;
    public GameObject countdownRoot;

    [Header("Optional posture guide (shown only during baseline countdown)")]
    public Image postureGuideImage;          // assign an Image under countdownRoot
    public Sprite postureGuideSprite;        // assign your schematic sprite here
    public bool showPostureGuide = true;

    [Header("Countdown")]
    public int countdownSeconds = 10;

    public void ShowReadyMessage()
    {
        if (countdownRoot != null)
            countdownRoot.SetActive(true);

        // IMPORTANT: user asked to show the schematic during the countdown only.
        // So we keep it hidden here by default.
        SetPostureGuideVisible(false);
    }

    private void SetPostureGuideVisible(bool visible)
    {
        if (!showPostureGuide)
            visible = false;

        if (postureGuideImage == null)
            return;

        if (postureGuideSprite != null)
            postureGuideImage.sprite = postureGuideSprite;

        postureGuideImage.enabled = visible;

        // In case you prefer toggling the whole GameObject:
        // postureGuideImage.gameObject.SetActive(visible);
    }

    public IEnumerator RunCountdownImmediate()
    {
        if (countdownRoot != null)
            countdownRoot.SetActive(true);

        // Show schematic during the actual baseline countdown
        SetPostureGuideVisible(true);

        for (int t = countdownSeconds; t > 0; t--)
        {
            if (countdownText != null)
                countdownText.text = $"\n\n Stand straight and comfortably \nBaseline is now recording...\nTask starting in: {t}";

            yield return new WaitForSeconds(1f);
        }

        if (countdownText != null)
            countdownText.text = "Starting...";

        // Hide after countdown
        SetPostureGuideVisible(false);

        if (countdownRoot != null)
            countdownRoot.SetActive(false);
    }
}