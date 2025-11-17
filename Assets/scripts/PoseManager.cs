using UnityEngine;

public class PoseManager : MonoBehaviour
{
    public GameObject hmd;

    void Start()
    {

    }

    void Update()
    {
        Debug.Log("HMD rotation: " + hmd.transform.rotation);
        Debug.Log("HMD position: " + hmd.transform.position);

    }
}
