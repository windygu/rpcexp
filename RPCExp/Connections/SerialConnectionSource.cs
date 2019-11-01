﻿using ModbusBasic.IO;
using RPCExp.Common;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace RPCExp.Connections
{
    public class SerialConnectionSource : ConnectionSourceAbstract
    {
        public override string ClassName => "Serial";

        public string Port { get; set; } = "COM1";

        public int Baud { get; set; } = 9600;

        public int Data { get; set; } = 8;

        public RJCP.IO.Ports.Parity Parity { get; set; } = RJCP.IO.Ports.Parity.None;

        public RJCP.IO.Ports.StopBits StopBits { get; set; } = RJCP.IO.Ports.StopBits.One;

        protected override IStreamResource TryOpen()
        {
            var sps = new RJCP.IO.Ports.SerialPortStream(Port, Baud, Data, Parity, StopBits);
            sps.Open();
            return new SerialPortStreamAdapter(sps);
        }
    }
}
