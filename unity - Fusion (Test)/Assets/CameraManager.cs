using UnityEngine;
using System.Collections; // Delay ke liye zaroori hai

public class CameraManager : MonoBehaviour
{
    public static CameraManager Instance { get; private set; }

    [Header("Cameras")]
    public Camera mainCamera;       
    public Camera broadcastCamera;  
    public Camera actionCamera;     

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        SwitchToMain();
    }

    public void SwitchToMain()
    {
        if (mainCamera != null) mainCamera.gameObject.SetActive(true);
        if (broadcastCamera != null) broadcastCamera.gameObject.SetActive(false);
        if (actionCamera != null) actionCamera.gameObject.SetActive(false);
    }

    public void SwitchToBroadcast()
    {
        if (mainCamera != null) mainCamera.gameObject.SetActive(false);
        if (actionCamera != null) actionCamera.gameObject.SetActive(false);
        if (broadcastCamera != null) broadcastCamera.gameObject.SetActive(true);
    }

    public void SwitchToAction()
    {
        if (mainCamera != null) mainCamera.gameObject.SetActive(false);
        if (broadcastCamera != null) broadcastCamera.gameObject.SetActive(false);
        if (actionCamera != null) actionCamera.gameObject.SetActive(true);
    }

    // ==========================================
    // THE DELAY SYSTEM (Naya Function)
    // ==========================================
    public void SwitchToActionDelayed(float delayInSeconds)
    {
        StartCoroutine(ActionTimer(delayInSeconds));
    }

    private IEnumerator ActionTimer(float delay)
    {
        yield return new WaitForSeconds(delay); // Timer wait karega
        SwitchToAction(); // Phir action camera on karega
    }
}