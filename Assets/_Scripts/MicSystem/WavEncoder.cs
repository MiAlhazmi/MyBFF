using UnityEngine;
using System;
using System.Text;

public static class WavEncoder
{
    // Returns complete WAV file bytes; also returns a suggested filename.
    public static byte[] EncodeToWAV(AudioClip clip, out string filename)
    {
        if (clip == null) throw new ArgumentNullException(nameof(clip));

        int totalSamples = clip.samples * clip.channels;
        float[] floatData = new float[totalSamples];
        clip.GetData(floatData, 0);

        // Convert float [-1,1] -> PCM16 LE bytes
        byte[] pcm16 = new byte[totalSamples * 2];
        int p = 0;
        for (int i = 0; i < totalSamples; i++)
        {
            float f = Mathf.Clamp(floatData[i], -1f, 1f);
            short s = (short)Mathf.RoundToInt(f * 32767f);
            pcm16[p++] = (byte)(s & 0xFF);
            pcm16[p++] = (byte)((s >> 8) & 0xFF);
        }

        int sampleRate = clip.frequency;
        short channels = (short)clip.channels;
        int subchunk2Size = pcm16.Length;                // data length in bytes
        int byteRate = sampleRate * channels * 2;        // 16-bit
        int blockAlign = channels * 2;
        int chunkSize = 36 + subchunk2Size;

        byte[] wav = new byte[44 + subchunk2Size];

        // RIFF header
        WriteASCII(wav, 0, "RIFF");
        WriteInt32LE(wav, 4, chunkSize);
        WriteASCII(wav, 8, "WAVE");

        // fmt subchunk
        WriteASCII(wav, 12, "fmt ");
        WriteInt32LE(wav, 16, 16);               // PCM header size
        WriteInt16LE(wav, 20, 1);                // PCM format
        WriteInt16LE(wav, 22, channels);         // NumChannels
        WriteInt32LE(wav, 24, sampleRate);       // SampleRate
        WriteInt32LE(wav, 28, byteRate);         // ByteRate
        WriteInt16LE(wav, 32, (short)blockAlign);// BlockAlign
        WriteInt16LE(wav, 34, 16);               // BitsPerSample

        // data subchunk
        WriteASCII(wav, 36, "data");
        WriteInt32LE(wav, 40, subchunk2Size);
        Buffer.BlockCopy(pcm16, 0, wav, 44, subchunk2Size);

        filename = $"recording_{DateTime.UtcNow:yyyyMMdd_HHmmss}.wav";
        return wav;
    }

    static void WriteASCII(byte[] buffer, int offset, string text)
    {
        Encoding.ASCII.GetBytes(text, 0, text.Length, buffer, offset);
    }

    static void WriteInt16LE(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    static void WriteInt32LE(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
