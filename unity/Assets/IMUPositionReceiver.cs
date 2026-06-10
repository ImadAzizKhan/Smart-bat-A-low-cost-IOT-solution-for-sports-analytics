using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization;

public class IMUPositionReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    public int port = 5009;  // Different from vision_tracker (5006)
    
    [Header("IMU Position Data")]
    public Vector3 imuPosition = Vector3.zero;
    public bool hasValidData = false;
    private float lastDataTime = 0f;

    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = true;

    void Start()
    {
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();
        Debug.Log("Listening for IMU position on port " + port);
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
                string[] parts = text.Split(',');

                // Parse 3 values: pX,pY,pZ
                if (parts.Length >= 3)
                {
                    float pX = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    float pY = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float pZ = float.Parse(parts[2], CultureInfo.InvariantCulture);

                    imuPosition = new Vector3(pX, pY, pZ);
                    hasValidData = true;
                    lastDataTime = Time.realtimeSinceStartup;
                }
            }
            catch { }
        }
    }

    void Update()
    {
        // Check if data is stale (no update for 1 second)
        if (Time.realtimeSinceStartup - lastDataTime > 1.0f)
        {
            hasValidData = false;
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        if (udpClient != null) udpClient.Close();
    }
}
