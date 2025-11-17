using UnityEngine;

public class FaceUser : MonoBehaviour
{
    public Transform hmd;

    void LateUpdate()
    {
        if (!hmd) return;

        transform.LookAt(hmd);
        transform.Rotate(0, 180f, 0);
    }
}
