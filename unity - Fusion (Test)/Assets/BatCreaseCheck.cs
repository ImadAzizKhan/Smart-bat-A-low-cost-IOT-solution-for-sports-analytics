using UnityEngine;

public class BatCreaseCheck : MonoBehaviour
{
    private CricketMatchManager manager;

    void Start()
    {
        manager = FindFirstObjectByType<CricketMatchManager>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("CreaseSafeZone") && manager != null)
        {
            manager.isBatsmanSafe = true;
            Debug.Log("Safe: Bat Crease Ke Andar Hai!");
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("CreaseSafeZone") && manager != null)
        {
            manager.isBatsmanSafe = false;
            Debug.Log("Danger: Bat Crease Se Bahar Hai!");
        }
    }
}