﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ModbusBasic.Extensions;
using ModbusBasic.Utility;

namespace ModbusBasic.IO
{
    /// <summary>
    ///     Refined Abstraction - http://en.wikipedia.org/wiki/Bridge_Pattern
    /// </summary>
    internal class ModbusRtuTransport : ModbusSerialTransport, IModbusRtuTransport
    {
        public const int RequestFrameStartLength = 7;

        public const int ResponseFrameStartLength = 4;

        internal ModbusRtuTransport(IStreamResource streamResource, IModbusFactory modbusFactory)
            : base(streamResource, modbusFactory)
        {
            if (modbusFactory == null) throw new ArgumentNullException(nameof(modbusFactory));
            Debug.Assert(streamResource != null, "Argument streamResource cannot be null.");
        }

        internal int RequestBytesToRead(byte[] frameStart)
        {
            byte functionCode = frameStart[1];

            IModbusFunctionService service = ModbusFactory.GetFunctionServiceOrThrow(functionCode);
                
            return service.GetRtuRequestBytesToRead(frameStart);
        }

        internal int ResponseBytesToRead(byte[] frameStart)
        {
            byte functionCode = frameStart[1];

            if (functionCode > Modbus.ExceptionOffset)
            {
                return 1;
            }

            IModbusFunctionService service = ModbusFactory.GetFunctionServiceOrThrow(functionCode);

            return service.GetRtuResponseBytesToRead(frameStart);
        }

        public virtual byte[] Read(int count)
        {
            byte[] frameBytes = new byte[count];
            int numBytesRead = 0;

            while ((numBytesRead != count) && (StreamResource.IsOpen))
            {
                numBytesRead += StreamResource.Read(frameBytes, numBytesRead, count - numBytesRead);
                if (numBytesRead == 0)
                {
                    if (StreamResource is TcpClientAdapter)
                    {
                        throw new IOException("There is no connection on TCP.");
                    }
                }

            }

            return frameBytes;
        }

        public override byte[] BuildMessageFrame(IModbusMessage message)
        {
            var messageFrame = message.MessageFrame;
            var crc = ModbusUtility.CalculateCrc(messageFrame);
            var messageBody = new MemoryStream(messageFrame.Length + crc.Length);

            messageBody.Write(messageFrame, 0, messageFrame.Length);
            messageBody.Write(crc, 0, crc.Length);

            return messageBody.ToArray();
        }

        public override bool ChecksumsMatch(IModbusMessage message, byte[] messageFrame)
        {
            ushort messageCrc = BitConverter.ToUInt16(messageFrame, messageFrame.Length - 2);
            ushort calculatedCrc = BitConverter.ToUInt16(ModbusUtility.CalculateCrc(message.MessageFrame), 0);

            return messageCrc == calculatedCrc;
        }

        public override IModbusMessage ReadResponse<T>()
        {
            byte[] frame = ReadResponse();

            //Logger.LogFrameRx(frame);

            return CreateResponse<T>(frame);
        }

        private byte[] ReadResponse()
        {
            byte[] frameStart = Read(ResponseFrameStartLength);
            byte[] frameEnd = Read(ResponseBytesToRead(frameStart));
            byte[] frame = frameStart.Concat(frameEnd).ToArray();

            return frame;
        }

        public override void IgnoreResponse()
        {
            byte[] frame = ReadResponse();

            //Logger.LogFrameIgnoreRx(frame);
        }

        public override byte[] ReadRequest()
        {
            byte[] frameStart = Read(RequestFrameStartLength);
            byte[] frameEnd = Read(RequestBytesToRead(frameStart));
            byte[] frame = frameStart.Concat(frameEnd).ToArray();

            //Logger.LogFrameRx(frame);

            return frame;
        }
    }
}
