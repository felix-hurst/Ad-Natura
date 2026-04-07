using UnityEngine;
using System.IO;

public static class SaveSystem
{
    private static string SavePath => Path.Combine(Application.persistentDataPath, "playerdata.json");

    [System.Serializable]
    public class PlayerData
    {
        public float positionX;
        public float positionY;
        public float positionZ;
        public string checkpointID;

        public PlayerData(Vector3 position, string checkpoint)
        {
            positionX = position.x;
            positionY = position.y;
            positionZ = position.z;
            checkpointID = checkpoint;
        }

        public Vector3 GetPosition()
        {
            return new Vector3(positionX, positionY, positionZ);
        }
    }

    public static void SaveCheckpoint(Vector3 position, string checkpointID)
    {
        PlayerData data = new PlayerData(position, checkpointID);
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(SavePath, json);
        Debug.Log($"Game saved at checkpoint: {checkpointID}");
    }

    public static PlayerData LoadCheckpoint()
    {
        if (File.Exists(SavePath))
        {
            string json = File.ReadAllText(SavePath);
            PlayerData data = JsonUtility.FromJson<PlayerData>(json);
            Debug.Log($"Game loaded from checkpoint: {data.checkpointID}");
            return data;
        }
        Debug.Log("No save file found.");
        return null;
    }

    public static bool SaveExists()
    {
        return File.Exists(SavePath);
    }
}