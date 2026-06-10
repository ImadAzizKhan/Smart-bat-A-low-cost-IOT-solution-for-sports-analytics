using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization; // NAYI LINE: Required for CultureInfo!

public class UDPReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    public int port = 5005;
    
    // The Brain script will read this!
    public Quaternion currentRotation = Quaternion.identity;

    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = true;
    public static string espIPAddress = "";

    // Thread safety variables
    private Quaternion _stagedRotation = Quaternion.identity;
    private readonly object _rotLock = new object();

    public static UDPReceiver Instance { get; private set; } // Change to PositionReceiver in the other script

    void Awake()
    {
        // If one already exists, destroy this duplicate BEFORE it tries to open the port!
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        
        Instance = this;
        DontDestroyOnLoad(this.gameObject); // Keep this alive across all scenes
    }

    void Start()
    {
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();
        Debug.Log("Listening for ESP32 Rotation on port " + port);
    }

    // Unity Main Thread reads the data safely here
    void Update()
    {
        lock (_rotLock)
        {
            currentRotation = _stagedRotation;
        }
    }

    void ReceiveData()
    {
        udpClient = new UdpClient(port);
        IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                byte[] data = udpClient.Receive(ref anyIP);
		// --- NAYA CODE: Automatically save the ESP32's IP! ---
                espIPAddress = anyIP.Address.ToString(); 
                
                string text = Encoding.UTF8.GetString(data);
                string[] parts = text.Split(',');

                // Assuming your ESP sends Quaternions in the first 4 slots
                if (parts.Length >= 4)
                {
                    float qX = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    float qY = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float qZ = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float qW = float.Parse(parts[3], CultureInfo.InvariantCulture);

                    // Background thread writes the data safely here
                    lock (_rotLock)
                    {
                        _stagedRotation = new Quaternion(qX, qY, qZ, qW);
                    }	
                }
            }
            catch {}
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        if (udpClient != null) udpClient.Close(); // Safely shuts down the port and thread
    }
}