using UnityEngine;
using Oculus.Interaction;

public class BallFloat : MonoBehaviour
{
    public float amplitude = 0.1f;
    public float frequency = 1f;

    private float baseY;

    void Start()
    {
        baseY = transform.position.y;
    }

    void Update()
    {
        float y = baseY + Mathf.Sin(Time.time * frequency) * amplitude;
        transform.position = new Vector3(transform.position.x, y, transform.position.z);
    }

    public void ResetBase()
    {
        baseY = transform.position.y;
    }
}

