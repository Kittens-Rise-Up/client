using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Common.Networking.Packet;
using Common.Networking.IO;
using Common.Networking.Message;

namespace KRU.Networking 
{
    public class RPacketPurchaseItem : IReadable
    {
        public uint itemId;

        public void Read(PacketReader reader)
        {
            itemId = reader.ReadUInt16();

            reader.Dispose();
        }
    }
}
