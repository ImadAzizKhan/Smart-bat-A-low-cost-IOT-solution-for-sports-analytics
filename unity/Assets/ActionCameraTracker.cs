using UnityEngine;

public class ActionCameraTracker : MonoBehaviour
{
    [Header("Tracking Settings")]
    public float smoothSpeed = 10f; 

    [Header("TV Zoom Settings")]
    public Camera actionCam; // Inspector mein Action Camera yahan drag karein
    public float normalFOV = 60f;  // Jab ball paas ho (No Zoom)
    public float maxZoomFOV = 20f; // Jab ball door ho (Full Zoom)
    public float zoomSpeed = 5f;
    public float distanceForMaxZoom = 60f; // Kitni door jane par full zoom ho

    private Transform targetBall;

    void LateUpdate()
    {
        if (targetBall == null)
        {
            GameObject ball = GameObject.FindGameObjectWithTag("Ball");
            if (ball != null) targetBall = ball.transform;
        }

        if (targetBall != null)
        {
            // 1. Camera Rotation (Tracking)
            Vector3 direction = targetBall.position - transform.position;
            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, smoothSpeed * Time.deltaTime);
            }

            // 2. Dynamic Zoom (Naya Jadoo)
            if (actionCam != null)
            {
                float distance = Vector3.Distance(transform.position, targetBall.position);
                
                // Ball kitni door hai, us hisaab se zoom calculate karo
                float targetFOV = Mathf.Lerp(normalFOV, maxZoomFOV, distance / distanceForMaxZoom);
                targetFOV = Mathf.Clamp(targetFOV, maxZoomFOV, normalFOV); // Limit mein rakho
                
                // Smoothly zoom in/out karo
                actionCam.fieldOfView = Mathf.Lerp(actionCam.fieldOfView, targetFOV, zoomSpeed * Time.deltaTime);
            }
        }
    }
}