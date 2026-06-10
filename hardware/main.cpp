#include <Arduino.h>
#include <Adafruit_BNO08x.h>
#include <WiFi.h>
#include <WiFiUdp.h>

// --- Network Settings ---
// const char* ssid = "iliadbox-2A7CAF";
// const char* password = "933x3qbv6tqtctnsq5shsr";
const char* ssid = "Your Wifi SSID"; // Replace with your Wi-Fi SSID
const char* password = "Your Wifi Password"; // Replace with your Wi-Fi password
const char* targetIP = "IP OF THE UNITY MACHINE"; // IP of the Unity machine on the local network
const int targetPort = 5005; //Unity port
const int PYTHON_IMU_PORT = 5004; // New port for the Python script
const int MOTOR_PIN = 15; 
const int LISTEN_PORT = 4210;
unsigned long lastUdpTime = 0;

WiFiUDP udp;
Adafruit_BNO08x bno08x;

// --- Global Memory for Sensor Data ---
float qX = 0, qY = 0, qZ = 0, qW = 1;
float aX = 0, aY = 0, aZ = 0;
float gX = 0, gY = 0, gZ = 0;

// --- Non-Blocking Haptic State ---
unsigned long lastMotorTime = 0;
bool motorActive = false;

// --- WiFi Recovery State ---
unsigned long lastWiFiCheck = 0;

void setup() {
  Serial.begin(115200);
  delay(3000); 

  pinMode(MOTOR_PIN, OUTPUT);
  digitalWrite(MOTOR_PIN, LOW);

  // Connect to Wi-Fi
  WiFi.mode(WIFI_STA);
  WiFi.setSleep(false);
  WiFi.begin(ssid, password);
  Serial.print("Connecting to Wi-Fi");
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }
  Serial.println("\nWi-Fi Connected!");
  udp.begin(LISTEN_PORT); 

  // Start the sensor
  if (!bno08x.begin_I2C()) {
    Serial.println("ERROR: Could not find the BNO085 sensor.");
    while (1) { delay(10); } 
  }
  
  bno08x.enableReport(SH2_GAME_ROTATION_VECTOR);
  bno08x.enableReport(SH2_LINEAR_ACCELERATION);
  bno08x.enableReport(SH2_GYROSCOPE_CALIBRATED);
  
  Serial.println("All sensors enabled. Broadcasting UDP...");
  Serial.println(WiFi.localIP());
}

void loop() {
  // 1. NON-BLOCKING WIFI RECOVERY
  if (WiFi.status() != WL_CONNECTED) {
    if (millis() - lastWiFiCheck > 5000) { // Check every 5 seconds
      Serial.println("WiFi lost! Attempting to reconnect...");
      WiFi.disconnect();
      WiFi.begin(ssid, password);
      lastWiFiCheck = millis();
    }
  }

  // 2. SENSOR READING & UDP BROADCAST
  if (bno08x.wasReset()) {
    bno08x.enableReport(SH2_GAME_ROTATION_VECTOR);
    bno08x.enableReport(SH2_LINEAR_ACCELERATION);
    bno08x.enableReport(SH2_GYROSCOPE_CALIBRATED);
  }

  sh2_SensorValue_t sensorValue;
  if (bno08x.getSensorEvent(&sensorValue)) {
    
    // Update Accel (Stored in memory)
    if (sensorValue.sensorId == SH2_LINEAR_ACCELERATION) {                 // ← FIXED
      aX = sensorValue.un.linearAcceleration.x;                           // ← FIXED
      aY = sensorValue.un.linearAcceleration.y;                           // ← FIXED
      aZ = sensorValue.un.linearAcceleration.z;                           // ← FIXED
    }
    // Update Gyro (Stored in memory)
    else if (sensorValue.sensorId == SH2_GYROSCOPE_CALIBRATED) {
      gX = sensorValue.un.gyroscope.x;
      gY = sensorValue.un.gyroscope.y;
      gZ = sensorValue.un.gyroscope.z;
    }
    // Update Rotation & FIRE PACKET
    else if (sensorValue.sensorId == SH2_GAME_ROTATION_VECTOR) {
      qX = sensorValue.un.gameRotationVector.i;
      qY = sensorValue.un.gameRotationVector.j;
      qZ = sensorValue.un.gameRotationVector.k;
      qW = sensorValue.un.gameRotationVector.real;
    }
  }
    // 3. THE FIX: CONTINUOUS UDP BROADCAST (100Hz)
  // This guarantees Python gets its 20 calibration frames instantly, even if the bat is dead still
  if (millis() - lastUdpTime >= 10) {
      lastUdpTime = millis();
      
      char dataString[128];
      snprintf(dataString, sizeof(dataString), 
               "%.4f,%.4f,%.4f,%.4f,%.2f,%.2f,%.2f,%.2f,%.2f,%.2f", 
               qX, qY, qZ, qW, aX, aY, aZ, gX, gY, gZ);
                          
      // Fire to Unity (Rotation)
      udp.beginPacket(targetIP, targetPort);
      udp.print(dataString);
      udp.endPacket();

      // Fire to Python (IMU Position Math)
      udp.beginPacket(targetIP, PYTHON_IMU_PORT);
      udp.print(dataString);
      udp.endPacket();
      
      // Optional: Uncomment for debugging, but it will flood your monitor fast!
      // Serial.println(dataString); 
  }

  // 3. NON-BLOCKING UDP RECEIVE FOR HAPTICS
  int packetSize = udp.parsePacket();
  if (packetSize) {
    char incomingPacket[256];
    int len = udp.read(incomingPacket, 255);
    
    // Prevent Buffer Overflow
    if (len > 0 && len < 255) {
      incomingPacket[len] = '\0'; // Null-terminate
      
      String msg = String(incomingPacket);
      if (msg.indexOf("HIT") >= 0) {
        motorActive = true;
        lastMotorTime = millis();
        digitalWrite(MOTOR_PIN, HIGH); // Turn motor ON
      }
    }
  }

  // 4. NON-BLOCKING HAPTIC TIMER (Turns motor off after 150ms)
  if (motorActive && (millis() - lastMotorTime > 150)) {
    digitalWrite(MOTOR_PIN, LOW); // Turn motor OFF
    motorActive = false;
  }
}