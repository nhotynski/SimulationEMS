using System;
using IndustrialConveyor.Simulation.Communication;
using IndustrialConveyor.Simulation.Movement;
using NUnit.Framework;

namespace IndustrialConveyor.Tests
{
    public sealed class PositionTelegramTests
    {
        [Test]
        public void Write_EmptyFleet_ProducesFixedSizeHeaderWithZeroRecords()
        {
            var buffer = new byte[PositionTelegram.TotalSize];
            PositionTelegram.Write(buffer, 1234u, 567000u, Array.Empty<TrolleyData>());

            Assert.AreEqual(1224, PositionTelegram.TotalSize);
            Assert.AreEqual(PositionTelegram.Magic, BitConverter.ToUInt32(buffer, 0));
            Assert.AreEqual((ushort)0, BitConverter.ToUInt16(buffer, 6));
            Assert.AreEqual(1234u, BitConverter.ToUInt32(buffer, 12));
            Assert.AreEqual(567000u, BitConverter.ToUInt32(buffer, 16));
            Assert.AreEqual((ushort)PositionTelegram.RecordSize, BitConverter.ToUInt16(buffer, 20));
            Assert.AreEqual((ushort)PositionTelegram.MaximumRecords, BitConverter.ToUInt16(buffer, 22));
        }

        [Test]
        public void WriteRecord_StoresFieldsAtFixedOffsets()
        {
            var buffer = new byte[PositionTelegram.TotalSize];
            PositionTelegram.WriteRecord(buffer, 2, 7, 50, 1, 123456u, 1500);
            PositionTelegram.WriteSequence(buffer, 42u);

            var offset = PositionTelegram.HeaderSize + 2 * PositionTelegram.RecordSize;
            Assert.AreEqual((ushort)7, BitConverter.ToUInt16(buffer, offset));
            Assert.AreEqual(50, buffer[offset + 2]);
            Assert.AreEqual(1, buffer[offset + 3]);
            Assert.AreEqual(123456u, BitConverter.ToUInt32(buffer, offset + 4));
            Assert.AreEqual(1500, BitConverter.ToInt32(buffer, offset + 8));
            Assert.AreEqual(42u, BitConverter.ToUInt32(buffer, 8));
        }
    }
}
