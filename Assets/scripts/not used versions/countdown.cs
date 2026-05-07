using UnityEngine;
using TMPro;
using System.Collections;

public class CountdownManager : MonoBehaviour
{
    [Header("Countdown Settings")]
    public TextMeshProUGUI countdownText;   
    public float countdownTime = 5f;        

    [Header("End Message")]
    public string endMessage = "START TASK"; 
    public float endMessageDuration = 1.5f;  

    bool isCountingDown = false;


    public void StartCountdown()
    {
        if (isCountingDown) return;

        StopAllCoroutines();
        StartCoroutine(CountdownRoutine());
    }


    IEnumerator CountdownRoutine()
    {
        isCountingDown = true;

        float timer = countdownTime;

        // activate the countdown UI text
        if (countdownText)
            countdownText.gameObject.SetActive(true);

        while (timer > 0f)
        {
            if (countdownText)
                countdownText.text = Mathf.Ceil(timer).ToString();

            timer -= Time.deltaTime;
            yield return null;
        }

        if (countdownText)
        {
            countdownText.text = endMessage;

        }

        yield return new WaitForSeconds(endMessageDuration);

        if (countdownText)
            countdownText.gameObject.SetActive(false);

        isCountingDown = false;
    }
}
