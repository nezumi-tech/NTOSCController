using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace NanoTerasuOSCController
{
    public class NanoTerasuOscController
    {
        private readonly UdpClient _udpClient;
        private readonly string _ip;
        private readonly int _port;

        public NanoTerasuOscController(string ip, int port)
        {
            _ip = ip;
            _port = port;
            _udpClient = new UdpClient();
        }

        public void SendInt(string paramName, int value)
        {
            string address = $"/avatar/parameters/{paramName}";
            byte[] oscData = BuildOscIntMessage(address, value);
            SendPacket(address, oscData, value.ToString());
        }

        public void SendBool(string paramName, bool value)
        {
            string address = $"/avatar/parameters/{paramName}";
            byte[] oscData = BuildOscBoolMessage(address, value);
            SendPacket(address, oscData, value.ToString());
        }

        /// <summary>
        /// Trigger型のOSCメッセージを送信します
        /// </summary>
        public void SendTrigger(string paramName)
        {
            string address = $"/avatar/parameters/{paramName}";
            byte[] oscData = BuildOscTriggerMessage(address);
            SendPacket(address, oscData, "Trigger Fired");
        }

        private void SendPacket(string address, byte[] data, string logValue)
        {
            try
            {
                _udpClient.Send(data, data.Length, _ip, _port);
                System.Diagnostics.Debug.WriteLine($"OSC Sent -> {address}: {logValue}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"送信エラー: {ex.Message}");
            }
        }

        private byte[] BuildOscIntMessage(string address, int value)
        {
            var buffer = new List<byte>();

            buffer.AddRange(Encoding.ASCII.GetBytes(address));
            buffer.Add(0);
            while (buffer.Count % 4 != 0) buffer.Add(0);

            buffer.AddRange(Encoding.ASCII.GetBytes(",i"));
            buffer.Add(0);
            while (buffer.Count % 4 != 0) buffer.Add(0);

            byte[] valueBytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(valueBytes);
            }
            buffer.AddRange(valueBytes);

            return buffer.ToArray();
        }

        private byte[] BuildOscBoolMessage(string address, bool value)
        {
            var buffer = new List<byte>();

            buffer.AddRange(Encoding.ASCII.GetBytes(address));
            buffer.Add(0);
            while (buffer.Count % 4 != 0) buffer.Add(0);

            string typeTag = value ? ",T" : ",F";
            buffer.AddRange(Encoding.ASCII.GetBytes(typeTag));
            buffer.Add(0);
            while (buffer.Count % 4 != 0) buffer.Add(0);

            return buffer.ToArray();
        }

        private byte[] BuildOscTriggerMessage(string address)
        {
            var buffer = new List<byte>();

            buffer.AddRange(Encoding.ASCII.GetBytes(address));
            buffer.Add(0);
            while (buffer.Count % 4 != 0) buffer.Add(0);

            // VRChatのTriggerを発火させるための True (,T) タグのみを送信
            buffer.AddRange(Encoding.ASCII.GetBytes(",T"));
            buffer.Add(0);
            while (buffer.Count % 4 != 0) buffer.Add(0);

            return buffer.ToArray();
        }
    }
}