using UnityEngine;
using System.IO;
using System.Text;
using System.Collections;

public class PerformanceReporter : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float startupDelay = 5f; // Wait 5 seconds
    [SerializeField] private int framesToMeasure = 120; // Measure over 120 frames

    private string filePath;
    private int frameCount = 0;
    private float totalTime = 0;

    private bool isMeasurementStarted = false;
    private bool isReportFinished = false;

    void Start()
    {
        filePath = Path.Combine(Application.persistentDataPath, "PerformanceReport.txt");

        // Start the countdown timer
        StartCoroutine(WaitToStartMeasuring());

        Debug.Log($"Performance Report scheduled to start in {startupDelay} seconds.");
    }

    private IEnumerator WaitToStartMeasuring()
    {
        yield return new WaitForSeconds(startupDelay);
        isMeasurementStarted = true;
        Debug.Log("Measurement started now!");
    }

    void Update()
    {
        // Only run logic if the delay is over and we haven't finished the report yet
        // Essentially measures "how long does it take to do framesToMeasure frames"
        if (isMeasurementStarted && !isReportFinished)
        {
            frameCount++;
            totalTime += Time.unscaledDeltaTime;

            if (frameCount >= framesToMeasure)
            {
                GenerateReport();
                isReportFinished = true;
            }
        }
    }

    void GenerateReport()
    {
        float avgFps = frameCount / totalTime;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("==================================");
        sb.AppendLine("PERFORMANCE REPORT");
        sb.AppendLine($"Date: {System.DateTime.Now}");
        sb.AppendLine("======= Hardware");
        sb.AppendLine($"OS: {SystemInfo.operatingSystem}");
        sb.AppendLine($"CPU: {SystemInfo.processorType}");
        sb.AppendLine($"GPU: {SystemInfo.graphicsDeviceName}");
        sb.AppendLine($"VRAM: {SystemInfo.graphicsMemorySize} MB");
        sb.AppendLine($"RAM: {SystemInfo.systemMemorySize} MB");
        sb.AppendLine("======= Performance");
        sb.AppendLine($"Avg FPS: {avgFps:F2}");
        sb.AppendLine("==================================");

        File.WriteAllText(filePath, sb.ToString());
        Debug.Log($"Report saved to: {filePath}");
    }
}