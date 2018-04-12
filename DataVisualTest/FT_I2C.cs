using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using FTD2XX_NET;

namespace CINALib
{
    /// <summary>
    ///  http://www.ftdichip.com/Support/Documents/AppNotes/AN_108_Command_Processor_for_MPSSE_and_MCU_Host_Bus_Emulation_Modes.pdf
    ///  http://www.ftdichip.com/Support/Documents/AppNotes/AN_113_FTDI_Hi_Speed_USB_To_I2C_Example.pdf
    ///  http://www.ftdichip.com/Support/Documents/AppNotes/AN_255_USB%20to%20I2C%20Example%20using%20the%20FT232H%20and%20FT201X%20devices.pdf
    ///  http://www.ftdichip.com/Support/Documents/AppNotes/AN_113_FTDI_Hi_Speed_USB_To_I2C_Example.pdf
    /// </summary>

    public class FTI2C : IDisposable
    {
        const byte MSB_FALLING_EDGE_CLOCK_BYTE_IN = 0x20;
        const byte MSB_FALLING_EDGE_CLOCK_BYTE_OUT = 0x11;
        const byte MSB_FALLING_EDGE_CLOCK_BIT_OUT = 0x13;
        const byte MSB_RISING_EDGE_CLOCK_BIT_IN = 0x22;

        const byte SET_DATA_BITS_ADBUS = 0x80; // Low byte
        const byte SET_DATA_BITS_ACBUS = 0x82; // High byte

        const byte TURN_ON_LOOPBACK = 0x84;
        const byte TURN_OFF_LOOPBACK = 0x85;
        const byte SET_TCK_SK_CLOCK_DIVISOR = 0x86;
        const byte DISABLE_CLOCK_DIVISOR = 0x8A;

        const byte SET_IO_TO_ONLY_DRIVE_ON_0 = 0x95;

        const int USB_TRANSFERE_SIZE = 65536;

        FTDI _ftdi;
        public FTDI Controller { get { return _ftdi; } set { _ftdi = value; } }

        public enum BUS { ACBUS = 0x82, ADBUS = 0x80 }; // {HI (7-0), LO (7-0)} bus libes

        public enum RW { READ = 1, WRITE = 0 };

        public void Init(int index)
        {
            _ftdi = new FTDI();
            FTDI.FT_STATUS status = _ftdi.OpenByIndex((uint)index);
            if (status != FTDI.FT_STATUS.FT_OK)
                throw new FTI2CException("Problem opening FTDI device");

            status = _ftdi.ResetDevice();
            if (status != FTDI.FT_STATUS.FT_OK)
                throw new FTI2CException("Problem resetting FTDI device");

            // Purge USB receive buffer first by reading out all old data from FT2232H receive buffer
            status = _ftdi.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
            if (status != FTDI.FT_STATUS.FT_OK)
                throw new FTI2CException("Problem purging FTDI device");

            //Set USB request transfer size
            status = _ftdi.InTransferSize(USB_TRANSFERE_SIZE);
            if (status != FTDI.FT_STATUS.FT_OK)
                throw new FTI2CException("Problem setting USB transfer size");

            //Disable event and error characters
            status = _ftdi.SetCharacters(0, false, 0, false);
            if (status != FTDI.FT_STATUS.FT_OK)
                throw new FTI2CException("Problem disabling event and error characters");

            //Sets the read and write timeouts in milliseconds for the FT2232H
            status = _ftdi.SetTimeouts(5000, 5000);
            if (status != FTDI.FT_STATUS.FT_OK)
                throw new FTI2CException("Problem setting timeouts");

            //Set the latency timer
            status = _ftdi.SetLatency(16);
            if (status != FTDI.FT_STATUS.FT_OK)
                throw new FTI2CException("Problem setting latency timer");

            //Reset controller
            status = _ftdi.SetBitMode(0x0, FTDI.FT_BIT_MODES.FT_BIT_MODE_RESET);
            if (status != FTDI.FT_STATUS.FT_OK)
                throw new FTI2CException("Problem resetting controller");

            // Set IO to only drive on 0
            List<byte> buffer = new List<byte>();
            buffer.Add(SET_IO_TO_ONLY_DRIVE_ON_0);
            buffer.Add(0xFF);
            buffer.Add(0xFF);
            rawWrite(buffer.ToArray()); // Send Command

            // enable MPSEE mode
            status = _ftdi.SetBitMode(0x0, FTDI.FT_BIT_MODES.FT_BIT_MODE_MPSSE);
            if (status != FTDI.FT_STATUS.FT_OK)
                throw new FTI2CException("Problem enabling MPSEE mode");

            /// Configure the MPSSE settings
            buffer = new List<byte>();
            buffer.Add(DISABLE_CLOCK_DIVISOR); // Disables the clk divide by 5 to allow for a 60MHz master clock.
            buffer.Add(0x97);//  Ensure adaptive clocking is off
            buffer.Add(0x8C);// Enables 3 phase data clocking. data VALID on both edges.
            rawWrite(buffer.ToArray()); // Send Command

            /* Low Byte (ADBUS)
                ADBUS0 TCK/SK ---> SCL
                ADBUS1 TDI/DO -+-> SDA
                ADBUS2 TDO/DI -+
                ADBUS3 TMS/CS
                ADBUSS GPIOL0
                ADBUS5 GPIOL1
                ADBUS6 GPIOl2
                ADBUS7 GPIOL3
            */
            //Command to set ADBUS state and direction
            //Set SDA high, SCL high
            //Set SDA, SCL as output with bit 1, other pins as input with bit 0
            buffer.AddRange(formSetDataBits(BUS.ADBUS, 0x03, 0x03));
            rawWrite(buffer.ToArray()); // Send Command

            // Set clock divisor
            buffer = new List<byte>();
            buffer.Add(SET_TCK_SK_CLOCK_DIVISOR);
            //TCK/SK period = 12MHz*5 / ( (1 + (0xValueH << 8) | 0xValueL) ) * 2)
            //double frequency = (60E6) / ( (1+ ((0x00 << 8) | 0xCB)) * 2/3 );
            UInt16 dwClockDivisor = 0x00CB; //(0x012B/3) =~ 100 KHz
            buffer.Add((byte)(dwClockDivisor & 0xFF));// low byte
            buffer.Add((byte)((dwClockDivisor >> 8) & 0xFF));// high byte
            rawWrite(buffer.ToArray());// sent of commands

            // Turn off Loopback
            buffer = new List<byte>();
            //buffer.Add(TURN_OFF_LOOPBACK);//Command to turn off loop back of TDI/TDO connection
            rawWrite(buffer.ToArray());// sent of commands

        }

        public Int16 ReadRegister(byte slave_addr)
        {
            _ftdi.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);

            List<byte> buffer = new List<byte>();

            // Frame 1 - Slave addr
            buffer.AddRange(formStart());// Start
            buffer.AddRange(formSlaveAddrFrame(slave_addr, FTI2C.RW.READ)); // Slave addr + R/W

            // SDA tristate, SCL low
            buffer.AddRange(formSetDataBits(BUS.ADBUS, 0x02, 0x01));

            // Read Byte
            buffer.Add(0x20);
            buffer.Add(0x00);
            buffer.Add(0x00);

            // Send ACK
            buffer.AddRange(formSetDataBits(BUS.ADBUS, 0x00, 0x03));

            buffer.Add(0x12);
            buffer.Add(0x00);
            buffer.Add(0x00);

            // SDA tristate, SCL low
            buffer.AddRange(formSetDataBits(BUS.ADBUS, 0x02, 0x01));

            // Read 8 bits
            buffer.Add(0x20);
            buffer.Add(0x00);
            buffer.Add(0x00);

            buffer.Add(0x12);
            buffer.Add(0x00);
            buffer.Add(0x00);

            buffer.AddRange(formStop());

            rawWrite(buffer.ToArray());// sent of commands

            uint c = 0;
            c = waifForRxDataCount(3);
            byte[] readdata = rawRead(c);

            byte ack = (byte)(readdata[0] & 0x01);
            if (ack != 0)
                throw new FTI2CException("No ACK");

            Int16 value = (Int16)((readdata[1] << 8) | (readdata[2]));

            return value;
        }

        public void WriteRegister(byte slave_addr, byte register_pointer, UInt16 value)
        {
            _ftdi.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);

            List<byte> buffer = new List<byte>();

            // Frame 1 - Slave addr
            buffer.AddRange(formStart());// Start
            buffer.AddRange(formSlaveAddrFrame(slave_addr, FTI2C.RW.WRITE)); // Slave addr + R/W

            // Frame 2 - Resgister address
            buffer.AddRange(formDataFrame(register_pointer));

            // Farme 3 - Data MSB
            byte msb = (byte)(value >> 8);
            buffer.AddRange(formDataFrame(msb));

            // Farme 4 - Data LSB
            byte lsb = (byte)(value & 0xFF);
            buffer.AddRange(formDataFrame(lsb));

            buffer.AddRange(formStop());// Stop

            rawWrite(buffer.ToArray()); // Send command

            // Check ACKs
            uint read_count = waifForRxDataCount(4, 100);
            byte[] readdata = rawRead(read_count);
            byte ack = (byte)(readdata[read_count - 1] & 0x0F);
            if (ack != 0)
                throw new FTI2CException("No ACK");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="count"></param>
        /// <param name="max_read_count"></param>
        /// <returns>Rx bytes available</returns>
        uint waifForRxDataCount(uint count, uint max_read_count = 100)
        {
            FTDI.FT_STATUS status;
            uint read_count = 0;
            uint inCount = 0;
            while (true)
            {
                status = _ftdi.GetRxBytesAvailable(ref inCount);
                if (status != FTDI.FT_STATUS.FT_OK)
                    throw new FTI2CException("Unable to get Rx bytes available");
                if (inCount >= count)
                    break;
                if (read_count++ > max_read_count)
                    throw new FTI2CException("Timeout reading Rx bytes available");
                Thread.Sleep(1);
            }

            return inCount;

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="slave_addr">The slave address is determine by A0 and A1 pins, so only last 4 bits</param>
        /// <param name="R_W">true = read, false = write command</param> 
        /// <param name="register_pointer">register address</param>
        public void RegisterPointerSet(byte slave_addr, RW R_W, byte register_pointer)
        {
            List<byte> buffer = new List<byte>();

            // Start - Slave addr
            buffer.AddRange(formStart()); // Start
            buffer.AddRange(formSlaveAddrFrame(slave_addr, R_W)); // Slave addr + R/W

            // Register address
            buffer.AddRange(formDataFrame(register_pointer));

            // Stop
            buffer.AddRange(formStop()); // Stop

            // Send command
            rawWrite(buffer.ToArray());


            uint count = waifForRxDataCount(2);
            byte[] readdata = rawRead(count);
            // readdata should be all 0 for all acks

        }

        /// <summary>
        /// Forms an I2C data frame
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        byte[] formDataFrame(byte data)
        {
            List<byte> buffer = new List<byte>();
            buffer.Add(MSB_FALLING_EDGE_CLOCK_BYTE_OUT);// clock data byte on clock edge MSB first
            buffer.Add(0x00);// data length of 0x0000 means 1 byte
            buffer.Add(0x00);
            buffer.Add(data); // data

            // SDA tristate, SCL low
            buffer.AddRange(formSetDataBits(BUS.ADBUS, 0x02, 0x01));

            // Clock for ACK
            buffer.Add(MSB_RISING_EDGE_CLOCK_BIT_IN);// Command to clock in bits MSB first on rising edge
            buffer.Add(0x00);// length of 0x00 means scan 1 bit
            buffer.Add(0x87);//Send answer back immediate command. This will make the chip flush its buffer back to the PC

            // SDA High, SCL low
            buffer.AddRange(formSetDataBits(BUS.ADBUS, 0x02, 0x03));

            return buffer.ToArray();
        }

        byte[] formSlaveAddrFrame(byte slave_addr, RW R_W)
        {
            List<byte> buffer = new List<byte>();

            buffer.Add(MSB_FALLING_EDGE_CLOCK_BYTE_OUT);// clock data byte on clock edge MSB first
            buffer.Add(0x00);// data length of 0x0000 means 1 byte
            buffer.Add(0x00);

            // Shift by 1 bit and add Read/Write bit at end
            byte data = (byte)((slave_addr << 1) | (Convert.ToByte(R_W)&0x1));
            data = (byte)(data | 0x80);
            buffer.Add(data);

            // SDA tristate, SCL low
            buffer.AddRange(formSetDataBits(BUS.ADBUS, 0x02, 0x01));

            // Clock for ACK
            buffer.Add(MSB_RISING_EDGE_CLOCK_BIT_IN);// Command to clock in bits MSB first on rising edge
            buffer.Add(0x00);// length of 0x00 means scan 1 bit
            buffer.Add(0x87);//Send answer back immediate command

            //// Reset pin state
            buffer.AddRange(formSetDataBits(BUS.ADBUS, 0x02, 0x03));

            return buffer.ToArray();
        }

        /// <summary>
        /// Gets the Start condition on the I2C Lines
        /// </summary>
        byte[] formStart()
        {
            List<byte> buffer = new List<byte>();

            // Repeat commands to ensure the minimum period of the start hold time ie 600ns is achieved
            for (int i = 0; i < 4; i++)
            {
                // SDA high, SCL high
                buffer.AddRange(formSetDataBits(BUS.ADBUS, 0x03, 0x03));
            }
            for (int i = 0; i < 4; i++)
            {
                buffer.AddRange(formSetDataBits(BUS.ADBUS, 0x01, 0x03));
            }

            buffer.AddRange(formSetDataBits(BUS.ADBUS, 0x00, 0x03));

            return buffer.ToArray();
        }

        /// <summary>
        /// Gets the Stop condition on the I2C Lines
        /// </summary>
        /// <returns></returns>
        byte[] formStop()
        {
            List<byte> buffer = new List<byte>();

            for (int i = 0; i < 4; i++)
            {
                buffer.AddRange(formSetDataBits(BUS.ADBUS, 0x00, 0x03));
            }

            for (int i = 0; i < 4; i++)
            {
                buffer.AddRange(formSetDataBits(BUS.ADBUS, 0x01, 0x03));
            }

            for (int i = 0; i < 4; i++)
            {
                buffer.AddRange(formSetDataBits(BUS.ADBUS, 0x03, 0x03));
            }

            return buffer.ToArray();
        }


        /// <summary>
        /// This will setup the direction of the BUS and force a value on the bits that are set as output. 
        /// A 1 in the Direction byte will make that bit an output, 0 an input
        /// </summary>
        /// <param name="bus"></param>
        /// <param name="value"></param>
        /// <param name="direction"></param>
        byte[] formSetDataBits(BUS bus = BUS.ADBUS, byte value = 0x00, byte direction = 0x00)
        {

            /*
            byte opcode = 0x80; // Low byte ADBUS
            if(bus == BUS.ACBUS) // High byte ACBUS
            {
                opcode = 0x82;
            }*/

            List<byte> buffer = new List<byte>();
            byte opcode = (byte)bus;
            buffer.Add(opcode);
            buffer.Add(value);
            buffer.Add(direction);

            return buffer.ToArray();

        }

        uint rawWrite(byte[] buffer)
        {
            if (buffer.Length > USB_TRANSFERE_SIZE)
                throw new FTI2CException("buffer size too large");

            uint outputSent = 0;
            FTDI.FT_STATUS status = _ftdi.Write(buffer, buffer.Length, ref outputSent);
            if (status != FTDI.FT_STATUS.FT_OK || outputSent != buffer.Length)
                throw new FTI2CException("Problem writing data");
            return outputSent;
        }

        byte[] rawRead(uint count)
        {

            byte[] inputBuffer = new byte[count];
            uint inputRead = 0;
            FTDI.FT_STATUS status = _ftdi.Read(inputBuffer, count, ref inputRead);
            if (status != FTDI.FT_STATUS.FT_OK)
                throw new FTI2CException("Problem reading data");
            if (inputRead != count)
                throw new FTI2CException("Problem reading data");
            return inputBuffer;
        }

        public void SetPins(BUS bus, byte data, byte direction)
        {
            List<byte> buffer = new List<byte>();
            buffer.AddRange(formSetDataBits(bus, data, direction));
            rawWrite(buffer.ToArray());

        }

        public byte GetPins(BUS bus)
        {
            rawWrite(new byte[] { (byte)(bus+1) });
            return rawRead(1)[0];
        }

        public void Dispose()
        {

            if (_ftdi != null)
                _ftdi.Close();
        }
    }

    class FTI2CException : Exception
    {
        public FTI2CException(string msg) : base(msg)
        {

        }
    }
}

