using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class VRPostureLogger : MonoBehaviour
{
    public PoseManager1 poseManager;
    public PostureNeck userPosture;

    private List<FrameEntry> frameLog = new List<FrameEntry>();
    private List<EpisodeEntry> episodeLog = new List<EpisodeEntry>();

    private string lastPosture = "Neutral";
    private float episodeStartTime;
    private int currentEpisodeMaxRula = 0;

    private int forwardCount = 0;
    private int backwardCount = 0;
    private int slouchCount = 0;
    private int lateralCount = 0;

    private bool isLogging = false;

    void Update()
    {
        if (!isLogging) return;
        if (!poseManager || !userPosture) return;

        string current = userPosture.CurrentPosture;
        bool isLateral = current == "Lateral";

        frameLog.Add(new FrameEntry()
        {
            time = Time.time,
            pitch = poseManager.normalizedPitch,
            height = poseManager.normalizedHeight,
            roll = poseManager.normalizedRoll,
            posture = current,
            rulaScore = userPosture.currentRulaScore,
            isLateral = isLateral
        });

        currentEpisodeMaxRula = Mathf.Max(currentEpisodeMaxRula, userPosture.currentRulaScore);

        if (current != lastPosture)
        {
            float now = Time.time;

            episodeLog.Add(new EpisodeEntry()
            {
                posture = lastPosture,
                startTime = episodeStartTime,
                endTime = now,
                duration = now - episodeStartTime,
                maxRulaScore = currentEpisodeMaxRula
            });

            if (current == "Forward") forwardCount++;
            if (current == "Backward") backwardCount++;
            if (current == "Slouch") slouchCount++;
            if (current == "Lateral") lateralCount++;

            lastPosture = current;
            episodeStartTime = now;
            currentEpisodeMaxRula = userPosture.currentRulaScore;
        }
    }

    public void StartLogging()
    {
        Debug.Log("LOGGER STARTED");

        isLogging = true;

        frameLog.Clear();
        episodeLog.Clear();

        lastPosture = userPosture.CurrentPosture;
        episodeStartTime = Time.time;
        currentEpisodeMaxRula = userPosture.currentRulaScore;

        forwardCount = backwardCount = slouchCount = lateralCount = 0;
    }

    public void StopAndSave()
    {
        if (!isLogging) return;
        isLogging = false;

        FinalizeLastEpisode();
        SaveEpisodeCSV();
        SaveFrameCSV();

        Debug.Log("LOGGER STOPPED & SAVED");
    }

    private void FinalizeLastEpisode()
    {
        float now = Time.time;

        episodeLog.Add(new EpisodeEntry()
        {
            posture = lastPosture,
            startTime = episodeStartTime,
            endTime = now,
            duration = now - episodeStartTime,
            maxRulaScore = currentEpisodeMaxRula
        });
    }

    private void SaveFrameCSV()
    {
        string folder = Application.dataPath + "/PostureLogs/";
        Directory.CreateDirectory(folder);

        string path = folder + "FrameLog_" +
            DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";

        using (StreamWriter sw = new StreamWriter(path))
        {
            sw.WriteLine("Time,Pitch,Height,Roll,RulaScore,Posture,IsLateral");

            foreach (var e in frameLog)
                sw.WriteLine(
                    $"{e.time:F3}," +
                    $"{e.pitch:F3}," +
                    $"{e.height:F3}," +
                    $"{e.roll:F3}," +
                    $"{e.rulaScore}," +
                    $"{e.posture}," +
                    $"{(e.isLateral ? 1 : 0)}"
                );
        }

        Debug.Log("Saved Frame CSV: " + path);
    }

    private void SaveEpisodeCSV()
    {
        string folder = Application.dataPath + "/PostureLogs/";
        Directory.CreateDirectory(folder);

        string path = folder + "Episodes_" +
            DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";

        using (StreamWriter sw = new StreamWriter(path))
        {
            sw.WriteLine("Posture,StartTime,EndTime,Duration,MaxRulaScore");

            foreach (var e in episodeLog)
                sw.WriteLine(
                    $"{e.posture}," +
                    $"{e.startTime:F3}," +
                    $"{e.endTime:F3}," +
                    $"{e.duration:F3}," +
                    $"{e.maxRulaScore}"
                );

            sw.WriteLine();
            sw.WriteLine("SUMMARY");
            sw.WriteLine($"ForwardEpisodes,{forwardCount}");
            sw.WriteLine($"BackwardEpisodes,{backwardCount}");
            sw.WriteLine($"SlouchEpisodes,{slouchCount}");
            sw.WriteLine($"LateralEpisodes,{lateralCount}");
        }

        Debug.Log("Saved Episodes CSV: " + path);
    }
}

[System.Serializable]
public class FrameEntry
{
    public float time;
    public float pitch;
    public float height;
    public float roll;
    public string posture;
    public int rulaScore;
    public bool isLateral;
}

[System.Serializable]
public class EpisodeEntry
{
    public string posture;
    public float startTime;
    public float endTime;
    public float duration;
    public int maxRulaScore;
}
