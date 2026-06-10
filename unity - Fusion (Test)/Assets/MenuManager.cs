using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    public void LoadArenaMatch()
    {
        Debug.Log("Loading Arena Match...");
        SceneManager.LoadScene("ArenaMatch");
    }

    public void LoadTrainingNets()
    {
        Debug.Log("Loading Training Nets...");
        SceneManager.LoadScene("TrainingNets");
    }
}