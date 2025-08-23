using System;
using System.IO;
using UnityEngine;

public static class ParentEmailManager
{
    private const string FileName = "parent_email.txt";
    private static bool? _cached;

    public static bool IsEmailSubmitted()
    {
        if (_cached.HasValue)
            return _cached.Value;

        string path = Path.Combine(Application.persistentDataPath, FileName);

        try
        {
            _cached = File.Exists(path);
            return _cached.Value;
        }
        catch (Exception e)
        {
            Debug.LogError($"ParentEmailManager error: {e.Message}");
            _cached = false;
            return false;
        }
    }

    public static void MarkEmailSubmitted()
    {
        string path = Path.Combine(Application.persistentDataPath, FileName);
        
        try
        {
            File.WriteAllText(path, "submitted");
            _cached = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"ParentEmailManager error: {e.Message}");
        }
    }
}