using System;
using System.IO;
using System.Text;

namespace FishingMod
{
    internal readonly struct FishingWaveData
    {
        internal FishingWaveData(int channels, int sampleRate, float[] samples)
        {
            Channels = channels;
            SampleRate = sampleRate;
            Samples = samples ?? throw new ArgumentNullException(nameof(samples));
        }

        internal int Channels { get; }
        internal int SampleRate { get; }
        internal float[] Samples { get; }
        internal int FrameCount => Samples.Length / Channels;
        internal float DurationSeconds => FrameCount / (float)SampleRate;
    }

    internal static class FishingWaveDecoder
    {
        internal static FishingWaveData Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A WAV path is required.", nameof(path));
            return Decode(File.ReadAllBytes(path));
        }

        internal static FishingWaveData Decode(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length < 44) throw new InvalidDataException("WAV file is too short.");

            using (MemoryStream stream = new MemoryStream(bytes, writable: false))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.ASCII))
            {
                if (ReadFourCc(reader) != "RIFF") throw new InvalidDataException("WAV RIFF header is missing.");
                reader.ReadUInt32();
                if (ReadFourCc(reader) != "WAVE") throw new InvalidDataException("WAV format marker is missing.");

                ushort format = 0;
                ushort channels = 0;
                int sampleRate = 0;
                ushort blockAlign = 0;
                ushort bitsPerSample = 0;
                byte[] sampleBytes = null;

                while (stream.Position + 8 <= stream.Length)
                {
                    string chunk = ReadFourCc(reader);
                    uint chunkSize = reader.ReadUInt32();
                    long chunkEnd = stream.Position + chunkSize;
                    if (chunkEnd < stream.Position || chunkEnd > stream.Length)
                        throw new InvalidDataException("WAV chunk extends past the end of the file.");

                    if (chunk == "fmt ")
                    {
                        if (chunkSize < 16) throw new InvalidDataException("WAV format chunk is incomplete.");
                        format = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        sampleRate = reader.ReadInt32();
                        reader.ReadInt32();
                        blockAlign = reader.ReadUInt16();
                        bitsPerSample = reader.ReadUInt16();
                    }
                    else if (chunk == "data")
                    {
                        if (chunkSize > int.MaxValue) throw new InvalidDataException("WAV sample data is too large.");
                        sampleBytes = reader.ReadBytes((int)chunkSize);
                        if (sampleBytes.Length != (int)chunkSize)
                            throw new EndOfStreamException("WAV sample data ended unexpectedly.");
                    }

                    stream.Position = chunkEnd;
                    if ((chunkSize & 1) != 0 && stream.Position < stream.Length) stream.Position++;
                }

                if (format != 1) throw new InvalidDataException("Only uncompressed PCM WAV files are supported.");
                if (channels < 1 || channels > 2) throw new InvalidDataException("WAV must be mono or stereo.");
                if (sampleRate < 8000 || sampleRate > 192000) throw new InvalidDataException("WAV sample rate is invalid.");
                if (bitsPerSample != 16) throw new InvalidDataException("WAV must use 16-bit samples.");
                if (blockAlign != channels * 2) throw new InvalidDataException("WAV block alignment is invalid.");
                if (sampleBytes == null || sampleBytes.Length == 0) throw new InvalidDataException("WAV contains no samples.");
                if (sampleBytes.Length % blockAlign != 0) throw new InvalidDataException("WAV data is not frame-aligned.");

                float[] samples = new float[sampleBytes.Length / 2];
                for (int i = 0, offset = 0; i < samples.Length; i++, offset += 2)
                {
                    short sample = (short)(sampleBytes[offset] | (sampleBytes[offset + 1] << 8));
                    samples[i] = sample / 32768f;
                }

                return new FishingWaveData(channels, sampleRate, samples);
            }
        }

        private static string ReadFourCc(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length != 4) throw new EndOfStreamException("WAV chunk identifier ended unexpectedly.");
            return Encoding.ASCII.GetString(bytes);
        }
    }
}
