using System;
using System.IO;
using UnityEngine;

public static class UserIdManager
{
    private const string FileName = "user_id.txt";
    private static string _cached;

    public static string GetUserId()
    {
        if (!string.IsNullOrEmpty(_cached))
            return _cached;

        string path = Path.Combine(Application.persistentDataPath, FileName);

        try
        {
            if (File.Exists(path))
            {
                _cached = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(_cached))
                    return _cached;
            }

            // Generate new UUID and persist
            _cached = Guid.NewGuid().ToString();
            File.WriteAllText(path, _cached);
            return _cached;
        }
        catch (Exception e)
        {
            Debug.LogError($"UserIdManager error: {e.Message}");
            // Fallback: still return a GUID in-memory so app keeps working
            _cached = Guid.NewGuid().ToString();
            return _cached;
        }
    }
}