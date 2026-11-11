using System;
using System.Collections.Generic;
using IndustrialConveyor.Simulation.Movement;
using UnityEngine;

namespace IndustrialConveyor.Simulation.Communication
{
    /// <summary>
    /// Fixed-size little-endian UDP telegram with the current positions of up to 100 trolleys (1224 bytes).
    /// Header (24 B): magic "CNVP" u32, version u16, activeRecords u16, sequence u32, simTimeMs u32,
    /// trackLengthMm u32, recordSize u16, maxRecords u16.
    /// Record (12 B, 100 slots, unused slots zeroed): id u16, typeCode u8, driveState u8, positionMm u32, speedMmPerS i32.
    /// </summary>
    public static class PositionTelegram
    {
        public const int MaximumRecords = 100;
        public const int HeaderSize = 24;
        public const int RecordSize = 12;
        public const int TotalSize = HeaderSize + MaximumRecords * RecordSize;
        public const uint Magic = 0x50564E43;
        public const ushort Version = 1;
        private const int SequenceOffset = 8;

        /// <summary>Fills the buffer (at least <see cref="TotalSize"/> bytes) from the current trolley state; sequence is left 0.</summary>
        public static void Write(byte[] buffer, uint simulationTimeMs, uint trackLengthMm, IReadOnlyList<TrolleyData> trolleys)
        {
            Array.Clear(buffer, 0, TotalSize);
            var count = Math.Min(trolleys.Count, MaximumRecords);
            PutUInt32(buffer, 0, Magic);
            PutUInt16(buffer, 4, Version);
            PutUInt16(buffer, 6, (ushort)count);
            PutUInt32(buffer, 12, simulationTimeMs);
            PutUInt32(buffer, 16, trackLengthMm);
            PutUInt16(buffer, 20, RecordSize);
            PutUInt16(buffer, 22, MaximumRecords);

            for (var index = 0; index < count; index++)
            {
                var trolley = trolleys[index];
                WriteRecord(buffer, index, ParseId(trolley.TrolleyId, index), TypeCode(trolley),
                    (byte)trolley.DriveState,
                    (uint)Mathf.Max(0, Mathf.RoundToInt(trolley.SimulationPositionMeters * 1000f)),
                    Mathf.RoundToInt(trolley.ActualSpeedMetersPerSecond * 1000f));
            }
        }

        /// <summary>Stamps the packet sequence number just before sending.</summary>
        public static void WriteSequence(byte[] buffer, uint sequence)
        {
            PutUInt32(buffer, SequenceOffset, sequence);
        }

        /// <summary>Writes one record into slot <paramref name="slot"/>.</summary>
        public static void WriteRecord(byte[] buffer, int slot, ushort id, byte typeCode, byte driveState,
            uint positionMillimeters, int speedMillimetersPerSecond)
        {
            var offset = HeaderSize + slot * RecordSize;
            PutUInt16(buffer, offset, id);
            buffer[offset + 2] = typeCode;
            buffer[offset + 3] = driveState;
            PutUInt32(buffer, offset + 4, positionMillimeters);
            PutUInt32(buffer, offset + 8, unchecked((uint)speedMillimetersPerSecond));
        }

        private static ushort ParseId(string trolleyId, int index)
        {
            var value = 0;
            var hasDigits = false;
            foreach (var character in trolleyId)
            {
                if (character < '0' || character > '9')
                    continue;
                hasDigits = true;
                value = Math.Min(ushort.MaxValue, value * 10 + (character - '0'));
            }
            return (ushort)(hasDigits ? value : index + 1);
        }

        // Body length in decimeters (30/50/70 for the 3/5/7 m types).
        private static byte TypeCode(TrolleyData trolley)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(trolley.TrolleyType.BodyLengthMeters * 10f), 0, 255);
        }

        private static void PutUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static void PutUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }
}
