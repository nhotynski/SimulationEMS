using System;
using System.Collections.Generic;
using IndustrialConveyor.Simulation.Movement;
using IndustrialConveyor.Simulation.Track;
using UnityEngine;

namespace IndustrialConveyor.Simulation.Communication
{
    /// <summary>
    /// Fixed-size little-endian UDP telegram (version 2) with the positions of up to 100 trolleys and the state of up to
    /// 16 switches (1692 bytes).
    /// Header (28 B): magic "CNVP" u32, version u16, activeRecords u16, sequence u32, simTimeMs u32, networkLengthMm u32,
    /// recordSize u16, maxRecords u16, activeSwitches u16, maxSwitches u16.
    /// Record (16 B, 100 slots, unused slots zeroed): id u16, typeCode u8, driveState u8, edge u16, reserved u16,
    /// positionMm u32 (distance from the start of that edge), speedMmPerS i32.
    /// Switch (4 B, 16 slots after the records): id u16 (digits of the switch name, e.g. S40 = 40), state u8
    /// (selected outgoing edge: 0 = Q0 branch, 1 = Q1 straight), flags u8 (bit0 locked by a trolley, bit1 change pending).
    /// Edge numbers are the 0-based index of the track edge; a simple loop has one edge (0).
    /// </summary>
    public static class PositionTelegram
    {
        public const int MaximumRecords = 100;
        public const int MaximumSwitches = 16;
        public const int HeaderSize = 28;
        public const int RecordSize = 16;
        public const int SwitchSize = 4;
        public const int SwitchOffset = HeaderSize + MaximumRecords * RecordSize;
        public const int TotalSize = SwitchOffset + MaximumSwitches * SwitchSize;
        public const uint Magic = 0x50564E43;
        public const ushort Version = 2;
        private const int SequenceOffset = 8;

        /// <summary>Fills the buffer (at least <see cref="TotalSize"/> bytes) from the current state; sequence is left 0.</summary>
        public static void Write(byte[] buffer, uint simulationTimeMs, uint networkLengthMm,
            IReadOnlyList<TrolleyData> trolleys, IReadOnlyList<TrackNode> switches = null)
        {
            Array.Clear(buffer, 0, TotalSize);
            var count = Math.Min(trolleys.Count, MaximumRecords);
            var switchCount = switches == null ? 0 : Math.Min(switches.Count, MaximumSwitches);
            PutUInt32(buffer, 0, Magic);
            PutUInt16(buffer, 4, Version);
            PutUInt16(buffer, 6, (ushort)count);
            PutUInt32(buffer, 12, simulationTimeMs);
            PutUInt32(buffer, 16, networkLengthMm);
            PutUInt16(buffer, 20, RecordSize);
            PutUInt16(buffer, 22, MaximumRecords);
            PutUInt16(buffer, 24, (ushort)switchCount);
            PutUInt16(buffer, 26, MaximumSwitches);

            for (var index = 0; index < count; index++)
            {
                var trolley = trolleys[index];
                WriteRecord(buffer, index, ParseId(trolley.TrolleyId, index), TypeCode(trolley),
                    (byte)trolley.DriveState,
                    (ushort)trolley.Edge.Index,
                    (uint)Mathf.Max(0, Mathf.RoundToInt(trolley.SimulationPositionMeters * 1000f)),
                    Mathf.RoundToInt(trolley.ActualSpeedMetersPerSecond * 1000f));
            }

            for (var index = 0; index < switchCount; index++)
            {
                var node = switches[index];
                var flags = (byte)((node.IsLocked ? 1 : 0) | (node.RequestedOutIndex >= 0 ? 2 : 0));
                WriteSwitch(buffer, index, ParseId(node.Name, index), (byte)node.SelectedOutIndex, flags);
            }
        }

        /// <summary>Stamps the packet sequence number just before sending.</summary>
        public static void WriteSequence(byte[] buffer, uint sequence)
        {
            PutUInt32(buffer, SequenceOffset, sequence);
        }

        /// <summary>Writes one trolley record into slot <paramref name="slot"/>.</summary>
        public static void WriteRecord(byte[] buffer, int slot, ushort id, byte typeCode, byte driveState,
            ushort edge, uint positionMillimeters, int speedMillimetersPerSecond)
        {
            var offset = HeaderSize + slot * RecordSize;
            PutUInt16(buffer, offset, id);
            buffer[offset + 2] = typeCode;
            buffer[offset + 3] = driveState;
            PutUInt16(buffer, offset + 4, edge);
            PutUInt32(buffer, offset + 8, positionMillimeters);
            PutUInt32(buffer, offset + 12, unchecked((uint)speedMillimetersPerSecond));
        }

        /// <summary>Writes one switch entry into slot <paramref name="slot"/>.</summary>
        public static void WriteSwitch(byte[] buffer, int slot, ushort id, byte state, byte flags)
        {
            var offset = SwitchOffset + slot * SwitchSize;
            PutUInt16(buffer, offset, id);
            buffer[offset + 2] = state;
            buffer[offset + 3] = flags;
        }

        private static ushort ParseId(string name, int index)
        {
            var value = 0;
            var hasDigits = false;
            foreach (var character in name)
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
