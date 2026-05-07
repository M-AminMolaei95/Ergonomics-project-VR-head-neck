using UnityEngine;
using Oculus.Interaction;

[RequireComponent(typeof(RayInteractable))]
public class MetaSelectBridge : MonoBehaviour
{
    private BallSelectable ball;
    private RayInteractable interactable;

    private bool wasSelected = false;

    void Awake()
    {
        ball = GetComponent<BallSelectable>();
        interactable = GetComponent<RayInteractable>();

        if (ball == null)
            Debug.LogError("MetaSelectBridge: BallSelectable missing.");
    }

    void Update()
    {
        if (ball == null || interactable == null) return;

        bool isSelectedNow = IsSelected(interactable);

        if (isSelectedNow && !wasSelected)
        {
            wasSelected = true;
            ball.OnSelected();
        }
        else if (!isSelectedNow && wasSelected)
        {
            wasSelected = false;
        }
    }

    private bool IsSelected(object obj)
    {
        var t = obj.GetType();

        // candidate property names in different versions
        string[] props = { "HasSelectingInteractors", "HasInteractors", "IsSelected", "Selected", "Selecting" };

        foreach (var p in props)
        {
            var prop = t.GetProperty(p);
            if (prop != null && prop.PropertyType == typeof(bool))
            {
                return (bool)prop.GetValue(obj);
            }
        }

        // candidate collection property names
        string[] colProps = { "SelectingInteractors", "SelectInteractors", "Interactors", "Selectors" };

        foreach (var p in colProps)
        {
            var prop = t.GetProperty(p);
            if (prop != null)
            {
                var val = prop.GetValue(obj);
                if (val == null) continue;

                // if it's a collection, check count > 0
                var countProp = val.GetType().GetProperty("Count");
                if (countProp != null && countProp.PropertyType == typeof(int))
                {
                    int count = (int)countProp.GetValue(val);
                    return count > 0;
                }
            }
        }

        return false;
    }
}
