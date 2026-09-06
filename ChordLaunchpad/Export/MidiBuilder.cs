using System;
using System.Collections.Generic;
using System.IO;
using ChordLaunchpad.Core;
using ChordLaunchpad.Core.Models;

namespace ChordLaunchpad.Export;

public static class MidiBuilder
{
    private const ushort TicksPerBeat = 480;

    private static void PushUint32(List<byte> bytes, uint value)
    {
        bytes.Add((byte)((value >> 24) & 0xff));
        bytes.Add((byte)((value >> 16) & 0xff));
        bytes.Add((byte)((value >> 8) & 0xff));
        bytes.Add((byte)(value & 0xff));
    }

    private static void PushUint16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte)((value >> 8) & 0xff));
        bytes.Add((byte)(value & 0xff));
    }

    private static byte[] VarLen(int value)
    {
        var buffer = value & 0x7f;
        var bytes = new List<byte>();

        while ((value >>= 7) > 0)
        {
            buffer <<= 8;
            buffer |= (value & 0x7f) | 0x80;
        }

        while (true)
        {
            bytes.Add((byte)(buffer & 0xff));
            if ((buffer & 0x80) != 0)
            {
                buffer >>= 8;
            }
            else
            {
                break;
            }
        }

        return bytes.ToArray();
    }

    private static void PushMetaEvent(List<byte> track, int delta, byte type, byte[] data)
    {
        track.AddRange(VarLen(delta));
        track.Add(0xff);
        track.Add(type);
        track.AddRange(VarLen(data.Length));
        track.AddRange(data);
    }

    private static void PushMidiEvent(List<byte> track, int delta, byte status, byte data1, byte data2)
    {
        track.AddRange(VarLen(delta));
        track.Add(status);
        track.Add(data1);
        track.Add(data2);
    }

    public static byte[] BuildMidiBytes(
        IReadOnlyList<ChordBlock> chords,
        int bpm,
        string timeSignature = "4/4",
        BassAdditionMode bassAddition = BassAdditionMode.None,
        OpenVoicingMode openVoicing = OpenVoicingMode.Closed)
    {
        var bytes = new List<byte>();
        var track = new List<byte>();

        // MThd
        bytes.AddRange([0x4d, 0x54, 0x68, 0x64]);
        PushUint32(bytes, 6);
        PushUint16(bytes, 0); // Format 0 (single track)
        PushUint16(bytes, 1); // 1 track
        PushUint16(bytes, TicksPerBeat);

        // Tempo meta event
        var microsecondsPerBeat = (int)Math.Round(60_000_000.0 / bpm);
        PushMetaEvent(track, 0, 0x51, [
            (byte)((microsecondsPerBeat >> 16) & 0xff),
            (byte)((microsecondsPerBeat >> 8) & 0xff),
            (byte)(microsecondsPerBeat & 0xff)
        ]);

        // Time signature meta event
        var num = byte.Parse(timeSignature.Split('/')[0]);
        var den = byte.Parse(timeSignature.Split('/')[1]);
        var denPower = (byte)Math.Log2(den);
        PushMetaEvent(track, 0, 0x58, [num, denPower, 24, 8]);

        foreach (var chord in chords)
        {
            var notes = MusicEngine.MidiNoteNumbers(chord, bassAddition, openVoicing);
            var durationTicks = (int)Math.Round(MusicEngine.ChordDurationBeats(chord.Duration, timeSignature, chord.Dotted) * TicksPerBeat);

            for (var i = 0; i < notes.Count; i++)
            {
                PushMidiEvent(track, 0, 0x90, (byte)notes[i], 92);
            }

            for (var i = 0; i < notes.Count; i++)
            {
                PushMidiEvent(track, i == 0 ? durationTicks : 0, 0x80, (byte)notes[i], 0);
            }
        }

        // End of track meta event
        PushMetaEvent(track, 0, 0x2f, []);

        // MTrk
        bytes.AddRange([0x4d, 0x54, 0x72, 0x6b]);
        PushUint32(bytes, (uint)track.Count);
        bytes.AddRange(track);

        return bytes.ToArray();
    }

    public static string SaveTempMidiFile(
        IReadOnlyList<ChordBlock> chords,
        int bpm,
        string timeSignature = "4/4",
        string fileName = "progression.mid",
        BassAdditionMode bassAddition = BassAdditionMode.None,
        OpenVoicingMode openVoicing = OpenVoicingMode.Closed)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ChordLaunchpad", "DragMidi");
        Directory.CreateDirectory(tempDir);

        try
        {
            var oldFiles = Directory.GetFiles(tempDir, "*.mid");
            var threshold = DateTime.UtcNow.AddHours(-1);
            foreach (var f in oldFiles)
            {
                if (File.GetCreationTimeUtc(f) < threshold)
                {
                    File.Delete(f);
                }
            }
        }
        catch { }

        if (!fileName.EndsWith(".mid", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".mid";
        }

        var fullPath = Path.Combine(tempDir, $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{fileName}");
        var bytes = BuildMidiBytes(chords, bpm, timeSignature, bassAddition, openVoicing);
        File.WriteAllBytes(fullPath, bytes);
        return fullPath;
    }
}
