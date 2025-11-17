using Oculus.Interaction;
using System.Collections.Generic;
using UnityEngine;

public class SetManager : MonoBehaviour
{
    [Header("Assign your balls here")]
    public List<GameObject> Balls;

    [Header("Your ray interactable")]
    public RayInteractable rayInteractable;

    [Header("Camera Reference")]
    public Transform hmd; // 

    [Header("Spawn Distance Range (meters)")]
    public float minDistance = 3f;
    public float maxDistance = 5f;

    [Header("Horizontal Angle Limits (Yaw)")]
    public float maxYawAngle = 35f;

    [Header("Vertical Angle Limits (Pitch)")]
    public float maxUpAngle = 20f;
    public float maxDownAngle = 20f;

    [Header("Height Range (Explicit Control)")]
    public float minHeight = 1.2f;   
    public float maxHeight = 1.9f;   

    [Header("Spawn distribution")]
    [Range(0f, 1f)]
    public float frontSpawnProbability = 0.8f; 

    int clickCount = 0;
    public int maxClicks;

    public VRPostureLogger logger;

    Vector3 zeroForward, zeroRight, zeroUp;
    Vector3 spawnCenter;
    bool zeroCaptured = false;

    void Start()
    {
        if (rayInteractable != null)
            rayInteractable.WhenPointerEventRaised += HandlePointerEvent;
    }

    public void CaptureCameraZero()
    {
        spawnCenter = hmd.position;
        zeroForward = hmd.forward.normalized;
        zeroRight = hmd.right.normalized;
        zeroUp = hmd.up.normalized;
        zeroCaptured = true;

        Debug.Log("Zero camera reference captured");
    }

    void RandomMove(GameObject ball)
    {
        if (!zeroCaptured)
            CaptureCameraZero();

        float radius = ball.GetComponent<SphereCollider>()?.radius * ball.transform.localScale.x ?? 0.25f;

        int maxAttempts = 50;
        for (int attempts = 0; attempts < maxAttempts; attempts++)
        {
           
            float yaw;
            if (Random.value < frontSpawnProbability)
                yaw = Random.Range(-maxYawAngle, maxYawAngle);
            else
                yaw = 180f + Random.Range(-maxYawAngle, maxYawAngle);

            float pitch = Random.Range(-maxDownAngle, maxUpAngle);

            Vector3 dir =
                Quaternion.AngleAxis(yaw, zeroUp) *
                Quaternion.AngleAxis(pitch, zeroRight) *
                zeroForward;
            dir.Normalize();

            float dist = Random.Range(minDistance, maxDistance);
            Vector3 newPos = spawnCenter + dir * dist;

            newPos.y = Random.Range(minHeight, maxHeight);

            bool ok = true;
            foreach (var other in Balls)
            {
                if (!other || other == ball) continue;
                if (Vector3.Distance(newPos, other.transform.position) < radius * 2.2f)
                {
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;

            ball.transform.position = newPos;

            var floatScript = ball.GetComponent<BallFloat>();
            if (floatScript != null) floatScript.ResetBase();

            return;
        }
    }

    void RandomizeColors(float difference = 0.35f)
    {
        if (Balls == null || Balls.Count == 0) return;

        GameObject targetBall = rayInteractable != null ? rayInteractable.gameObject : null;
        if (targetBall == null) return;

        if (!Balls.Contains(targetBall))
            Balls.Add(targetBall);

        float h = (Random.value < 0.5f)
            ? Random.Range(0.0f, 0.22f)
            : Random.Range(0.50f, 1.0f);

        float s = Random.Range(0.6f, 1f);
        float v = Random.Range(0.7f, 1f);

        Color baseColor = Color.HSVToRGB(h, s, v);
        Color targetColor = Color.HSVToRGB(h, Mathf.Clamp01(s - difference), v);

        foreach (var ball in Balls)
        {
            Renderer r = ball.GetComponent<Renderer>();
            if (!r) continue;
            r.material.color = (ball == targetBall) ? targetColor : baseColor;
        }
    }

    private void HandlePointerEvent(PointerEvent evt)
    {
        if (evt.Type != PointerEventType.Select) return;
        if (clickCount >= maxClicks) return;

        clickCount++;

        for (int i = 0; i < Balls.Count; i++)
            RandomMove(Balls[i]);

        RandomizeColors();

        if (clickCount >= maxClicks)
        {
            foreach (var ball in Balls)
                ball.SetActive(false);

            rayInteractable.enabled = false;
            if (logger != null)
                logger.StopAndSave();

        }

    }
}


