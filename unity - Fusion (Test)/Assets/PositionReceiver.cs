using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization; 

public class PositionReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    public int port = 5006;
    
    public Vector3 currentPosition = Vector3.zero;
    public bool hasValidData = false;
    private float lastDataTime = 0f;

    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = true;
    
    // Thread safety variables
    public volatile bool wantsTare = false; 
    private Vector3 _stagedPosition = Vector3.zero;
    private volatile bool _newDataAvailable = false;
    private readonly object _posLock = new object();

    public static PositionReceiver Instance { get; private set; } 

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        
        Instance = this;
        DontDestroyOnLoad(this.gameObject); 
    }

    void Start()
    {
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();
        Debug.Log("Listening for Python Position on port " + port);
    }

    // Unity Main Thread handles time and data assignment safely
    void Update()
    {
        lock (_posLock)
        {
            if (_newDataAvailable)
            {
                currentPosition = _stagedPosition;
                lastDataTime = Time.realtimeSinceStartup;
                hasValidData = true;
                _newDataAvailable = false;
            }
        }

        if (hasValidData && (Time.realtimeSinceStartup - lastDataTime > 1.0f))
        {
            hasValidData = false;
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
                string text = Encoding.UTF8.GetString(data);
                
                if (text.Trim() == "TARE")
                {
                    wantsTare = true;
                    continue; 
                }
                
                string[] parts = text.Split(',');

                if (parts.Length >= 3)
                {
                    float x = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    float y = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float z = float.Parse(parts[2], CultureInfo.InvariantCulture);

                    lock (_posLock)
                    {
                        _stagedPosition = new Vector3(-x, y, z);
                        _newDataAvailable = true;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("UDP Receive Error: " + e.Message);
            }
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        if (udpClient != null) udpClient.Close();
    }
}