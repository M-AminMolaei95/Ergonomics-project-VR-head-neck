using UnityEngine;

public class BallSelectable : MonoBehaviour
{
    private SetManager manager;

    public bool IsTarget { get; private set; }

    [Header("Auto-detected renderers")]
    public Renderer[] renderers;

    void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);
    }

    public void Initialize(SetManager setManager)
    {
        manager = setManager;
        SetTarget(false);
    }

    public void SetTarget(bool isTarget)
    {
        IsTarget = isTarget;
    }

    public void ApplyColor(Color c)
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;

            // Important: create instance material so changing one doesn't change all prefabs globally
            var mat = renderers[i].material;

            // URP/Lit typically uses "_BaseColor"
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", c);
            else if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", c);
        }
    }

    public void OnSelected()
    {
        manager?.OnBallSelected(this);
    }
}
