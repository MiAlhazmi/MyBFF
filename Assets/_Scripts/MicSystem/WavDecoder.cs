using System;
using System.Text;
using UnityEngine;

public static class WavDecoder
{
    public static AudioClip DecodeToClip(byte[] wavBytes, string clipName = "WebhookReply")
    {
        if (wavBytes == null || wavBytes.Length < 44)
        {
            Debug.LogError("[WavDecoder] Invalid WAV: too small.");
            return null;
        }

        // RIFF header
        if (Encoding.ASCII.GetString(wavBytes, 0, 4) != "RIFF" ||
            Encoding.ASCII.GetString(wavBytes, 8, 4) != "WAVE")
        {
            Debug.LogError("[WavDecoder] Not a RIFF/WAVE file.");
            return null;
        }

        int pos = 12; // start after "RIFF....WAVE"
        int fmtChunkSize = 0;
        int sampleRate = 0;
        short channels = 0;
        short bitsPerSample = 0;
        int dataOffset = -1;
        int dataSize = -1;

        // Parse chunks
        while (pos + 8 <= wavBytes.Length)
        {
            string chunkId = Encoding.ASCII.GetString(wavBytes, pos, 4);
            int chunkSize = ReadInt32LE(wavBytes, pos + 4);
            pos += 8;

            if (chunkId == "fmt ")
            {
                fmtChunkSize = chunkSize;
                short audioFormat   = ReadInt16LE(wavBytes, pos + 0);
                channels            = ReadInt16LE(wavBytes, pos + 2);
                sampleRate          = ReadInt32LE(wavBytes, pos + 4);
                // int byteRate     = ReadInt32LE(wavBytes, pos + 8);
                // short blockAlign = ReadInt16LE(wavBytes, pos + 12);
                bitsPerSample       = ReadInt16LE(wavBytes, pos + 14);

                if (audioFormat != 1)
                {
                    Debug.LogError("[WavDecoder] Only PCM (format 1) supported.");
                    return null;
                }
                if (bitsPerSample != 16)
                {
                    Debug.LogError("[WavDecoder] Only 16‑bit PCM supported.");
                    return null;
                }
            }
            else if (chunkId == "data")
            {
                dataOffset = pos;
                dataSize = chunkSize;
            }

            pos += chunkSize;
            if (pos > wavBytes.Length) break;
        }

        if (dataOffset < 0 || dataSize <= 0 || channels <= 0 || sampleRate <= 0)
        {
            Debug.LogError("[WavDecoder] Missing required chunks/fields.");
            return null;
        }

        int sampleCount = dataSize / 2;                // 2 bytes per 16‑bit sample
        int frames = sampleCount / channels;
        if (frames <= 0)
        {
            Debug.LogError("[WavDecoder] No audio frames.");
            return null;
        }

        // Convert PCM16 LE -> float [-1..1], interleaved
        float[] samples = new float[sampleCount];
        int si = 0;
        for (int i = 0; i < dataSize; i += 2)
        {
            short s = (short)(wavBytes[dataOffset + i] | (wavBytes[dataOffset + i + 1] << 8));
            samples[si++] = s / 32768f;
        }

        var clip = AudioClip.Create(clipName, frames, channels, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    static short ReadInt16LE(byte[] b, int o) =>
        (short)(b[o] | (b[o + 1] << 8));

    static int ReadInt32LE(byte[] b, int o) =>
        (b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
}
