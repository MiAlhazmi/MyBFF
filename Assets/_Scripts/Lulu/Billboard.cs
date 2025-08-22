using Unity.VisualScripting;
using UnityEngine;

public class Billboard : MonoBehaviour
{
    public Camera cam;
    void Awake()
    {
        if (cam.IsUnityNull()) cam = Camera.main;
    }
    void LateUpdate()
    {
        if (!cam) { cam = Camera.main; if (!cam) return; }
        // Face camera, keep upright
        Vector3 fwd = transform.position - cam.transform.position;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = cam.transform.forward;
        transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
    }
}