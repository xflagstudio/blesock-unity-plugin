using System;
using System.Text;

namespace BleSock
{
    public class Buffer
    {
        // Properties

        public byte[] RawData
        {
            get
            {
                return mRawData;
            }
        }

        public int Capacity
        {
            get
            {
                return mRawData.Length;
            }
        }

        public int Size
        {
            get
            {
                return mSize;
            }
        }

        public int Position
        {
            get
            {
                return mPosition;
            }
        }

        // Constructor

        public Buffer(int capacity = 1024)
        {
            if (capacity <= 0)
            {
                throw new Exception("Invalid capacity");
            }

            mRawData = new byte[capacity];
            mSize = 0;
            mPosition = 0;
        }

        public Buffer(byte[] bytes, int size)
        {
            if (bytes == null)
            {
                throw new Exception("Invalid bytes");
            }

            if (size > bytes.Length)
            {
                throw new Exception("Invalid size");
            }

            mRawData = bytes;
            mSize = size;
            mPosition = 0;
        }

        // Methods

        public void Clear()
        {
            mSize = 0;
            mPosition = 0;
        }

        public void Seek(int position)
        {
            if ((position < 0) || (position > mSize))
            {
                throw new Exception("Invalid position");
            }

            mPosition = position;
        }

        public byte[] GetBytes()
        {
            var bytes = new byte[mSize];
            Array.Copy(mRawData, bytes, mSize);

            return bytes;
        }


        public void Write(Int32 value)
        {
            Write((byte)value);
            Write((byte)(value >> 8));
            Write((byte)(value >> 16));
            Write((byte)(value >> 24));
        }

        public void Write(UInt32 value)
        {
            Write((byte)value);
            Write((byte)(value >> 8));
            Write((byte)(value >> 16));
            Write((byte)(value >> 24));
        }

        public void Write(Int16 value)
        {
            Write((byte)value);
            Write((byte)(value >> 8));
        }

        public void Write(UInt16 value)
        {
            Write((byte)value);
            Write((byte)(value >> 8));
        }

        public void Write(char value)
        {
            Write((byte)value);
        }

        public void Write(byte value)
        {
            PrepareWrite(1);

            mRawData[mPosition++] = value;
            mSize = Math.Max(mPosition, mSize);
        }

        public void Write(bool value)
        {
            Write((byte)(value ? 1 : 0));
        }

        public void Write(string value, bool large = false)
        {
            if (value == null)
            {
                throw new Exception("Value is null");
            }

            int size = Encoding.UTF8.GetByteCount(value);

            if (large)
            {
                if (size > UInt16.MaxValue)
                {
                    throw new Exception("Value too long");
                }

                Write((UInt16)size);
            }
            else
            {
                if (size > byte.MaxValue)
                {
                    throw new Exception("Value too long");
                }

                Write((byte)size);
            }

            PrepareWrite(size);

            Encoding.UTF8.GetBytes(value, 0, value.Length, mRawData, mPosition);

            mPosition += size;
            mSize = Math.Max(mPosition, mSize);
        }

        public void WriteBuffer(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new Exception("Buffer is null");
            }

            if (offset < 0)
            {
                throw new Exception("Invalid offset");
            }

            if (count < 0)
            {
                throw new Exception("Invalid count");
            }

            if (offset + count > buffer.Length)
            {
                throw new Exception("Buffer over flow");
            }

            PrepareWrite(count);

            Array.Copy(buffer, offset, mRawData, mPosition, count);

            mPosition += count;
            mSize = Math.Max(mPosition, mSize);
        }


        public int ReadInt32()
        {
            PrepareRead(sizeof(Int32));

            int result = BitConverter.ToInt32(mRawData, mPosition);
            mPosition += sizeof(Int32);

            return result;
        }

        public uint ReadUInt32()
        {
            PrepareRead(sizeof(UInt32));

            uint result = BitConverter.ToUInt32(mRawData, mPosition);
            mPosition += sizeof(UInt32);

            return result;
        }

        public short ReadInt16()
        {
            PrepareRead(sizeof(Int16));

            short result = BitConverter.ToInt16(mRawData, mPosition);
            mPosition += sizeof(Int16);

            return result;
        }

        public ushort ReadUInt16()
        {
            PrepareRead(sizeof(UInt16));

            ushort result = BitConverter.ToUInt16(mRawData, mPosition);
            mPosition += sizeof(UInt16);

            return result;
        }

        public char ReadChar()
        {
            PrepareRead(sizeof(char));

            char result = (char)mRawData[mPosition];
            mPosition += sizeof(char);

            return result;
        }

        public byte ReadByte()
        {
            PrepareRead(sizeof(byte));

            byte result = mRawData[mPosition];
            mPosition += sizeof(byte);

            return result;
        }

        public bool ReadBoolean()
        {
            return (ReadByte() > 0);
        }

        public string ReadString(bool large = false)
        {
            int size = large ? ReadUInt16() : ReadByte();
            PrepareRead(size);

            var result = Encoding.UTF8.GetString(mRawData, mPosition, size);
            mPosition += size;

            return result;
        }

        public void ReadBuffer(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new Exception("Buffer is null");
            }

            if (offset < 0)
            {
                throw new Exception("Invalid offset");
            }

            if (count < 0)
            {
                throw new Exception("Invalid count");
            }

            if (offset + count > buffer.Length)
            {
                throw new Exception("Buffer over flow");
            }

            PrepareRead(count);

            Array.Copy(mRawData, mPosition, buffer, offset, count);

            mPosition += count;
        }

        // Internal

        private byte[] mRawData;
        private int mSize;
        private int mPosition;

        private void PrepareWrite(int size)
        {
            if (mPosition + size > mRawData.Length)
            {
                int newSize = mRawData.Length;
                while (newSize < mPosition + size)
                {
                    newSize *= 2;
                }

                Array.Resize(ref mRawData, newSize);
            }
        }

        private void PrepareRead(int size)
        {
            if (mPosition + size > mSize)
            {
                throw new Exception("Read error");
            }
        }
    }
}
