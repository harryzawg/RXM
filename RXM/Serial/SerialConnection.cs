using RJCP.IO.Ports;
using System;
using System.Collections.Generic;
using System.Threading;

namespace RXM.Serial
{
    public class SerialConnection : IDisposable
    {
        private readonly SerialPortStream _serial;
        private readonly List<byte> _buffer = new();
        private bool IsRunning;
        public event EventHandler<byte[]>? DataReceived;

        public SerialConnection(string port = "COM3", int baud = 19200)
        {
            _serial = new SerialPortStream(port, baud)
            {
                Parity = Parity.None,
                StopBits = StopBits.One,
                DataBits = 8,
                ReadTimeout = 10,
                WriteTimeout = 100
            };
        }

        public void Open()
        {
            if (_serial.IsOpen) return;
            _serial.Open();
            IsRunning = true;
            new Thread(ReadLoop) { IsBackground = true }.Start();
            Console.WriteLine($"[SC] Opened port {_serial.PortName} @ {_serial.BaudRate}");
        }

        public void Close()
        {
            IsRunning = false;
            if (_serial.IsOpen) _serial.Close();
            Console.WriteLine("[SC] COM port closed");
        }

        public void Write(byte[] data)
        {
            if (_serial.IsOpen)
                _serial.Write(data, 0, data.Length);
        }

        private void ReadLoop()
        {
            while (IsRunning && _serial.IsOpen)
            {
                try
                {
                    int b = _serial.ReadByte();
                    if (b < 0) continue;

                    _buffer.Add((byte)b);

                    if (_buffer.Count > 0 && _buffer[^1] == 0xF7)
                    {
                        int lastF0 = _buffer.LastIndexOf(0xF0);
                        if (lastF0 >= 0)
                        {
                            var packet = _buffer.GetRange(lastF0, _buffer.Count - lastF0).ToArray();
                            DataReceived?.Invoke(this, packet);
                           // Console.WriteLine($"Packet {BitConverter.ToString(packet)}");
                            _buffer.Clear();
                        }
                        else
                        {
                            _buffer.Clear();
                        }
                    }
                }
                catch (TimeoutException) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SC] Error: {ex.Message}");
                    Thread.Sleep(10);
                }
            }
        }

        public void Dispose()
        {
            IsRunning = false;
            if (_serial.IsOpen) _serial.Close();
            _serial.Dispose();
        }
    }
}
