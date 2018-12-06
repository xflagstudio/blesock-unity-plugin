using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

using UnityEngine;

namespace BleSock
{
    public class MessageBuffer
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

        public bool IsReallocAllowed
        {
            get
            {
                return mReallocAllowed;
            }
        }

        // Constructors

        public MessageBuffer(int capacity = 1024, bool reallocAllowed = true)
        {
            Debug.Assert(capacity > 0);

            mRawData = new byte[capacity];
            mSize = 0;
            mPosition = 0;
            mReallocAllowed = reallocAllowed;
        }

        // Methods

        public void Clear()
        {
            mSize = 0;
            mPosition = 0;
        }

        public void Seek(int position)
        {
            Debug.Assert(position >= 0);
            Debug.Assert(position < mSize);

            mPosition = position;
        }

        public byte[] GetBytes()
        {
            var bytes = new byte[mSize];
            Array.Copy(mRawData, bytes, mSize);

            return bytes;
        }

        public ArraySegment<byte> GetArraySegment()
        {
            return new ArraySegment<byte>(mRawData, 0, mSize);
        }

        public void Write(int value)
        {
            Write((byte)value);
            Write((byte)(value >> 8));
            Write((byte)(value >> 16));
            Write((byte)(value >> 24));
        }

        public void Write(uint value)
        {
            Write((byte)value);
            Write((byte)(value >> 8));
            Write((byte)(value >> 16));
            Write((byte)(value >> 24));
        }

        public void Write(short value)
        {
            Write((byte)value);
            Write((byte)(value >> 8));
        }

        public void Write(ushort value)
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

        public void Write(float value)
        {
            mTypeConvert.single = value;

            Write(mTypeConvert.int32);
        }

        public void WriteString(string str, bool large = false)
        {
            Debug.Assert(str != null);

            int size = Encoding.UTF8.GetByteCount(str);

            if (large)
            {
                Debug.Assert(size <= UInt16.MaxValue);

                Write((UInt16)size);
            }
            else
            {
                Debug.Assert(size <= byte.MaxValue);

                Write((byte)size);
            }

            PrepareWrite(size);

            Encoding.UTF8.GetBytes(str, 0, str.Length, mRawData, mPosition);

            mPosition += size;
            mSize = Math.Max(mPosition, mSize);
        }

        public void WriteBytes(byte[] bytes, int offset, int count)
        {
            Debug.Assert(bytes != null);
            Debug.Assert(offset >= 0);
            Debug.Assert(count >= 0);
            Debug.Assert(offset + count <= bytes.Length);

            PrepareWrite(count);

            Array.Copy(bytes, offset, mRawData, mPosition, count);

            mPosition += count;
            mSize = Math.Max(mPosition, mSize);
        }

        public void WriteStream(Stream stream, int length)
        {
            Debug.Assert(stream != null);
            Debug.Assert(length >= 0);
            Debug.Assert(length <= stream.Length - stream.Position);

            PrepareWrite(length);

            int size = stream.Read(mRawData, mPosition, length);
            Debug.Assert(size == length);

            mPosition += length;
            mSize = Math.Max(mPosition, mSize);
        }


        public int ReadInt32()
        {
            PrepareRead(sizeof(int));

            int result = BitConverter.ToInt32(mRawData, mPosition);
            mPosition += sizeof(int);

            return result;
        }

        public uint ReadUInt32()
        {
            PrepareRead(sizeof(uint));

            uint result = BitConverter.ToUInt32(mRawData, mPosition);
            mPosition += sizeof(uint);

            return result;
        }

        public short ReadInt16()
        {
            PrepareRead(sizeof(short));

            short result = BitConverter.ToInt16(mRawData, mPosition);
            mPosition += sizeof(short);

            return result;
        }

        public ushort ReadUInt16()
        {
            PrepareRead(sizeof(ushort));

            ushort result = BitConverter.ToUInt16(mRawData, mPosition);
            mPosition += sizeof(ushort);

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

        public float ReadSingle()
        {
            PrepareRead(sizeof(float));

            float result = BitConverter.ToSingle(mRawData, mPosition);
            mPosition += sizeof(float);

            return result;
        }

        public string ReadString(bool large = false)
        {
            int size = large ? ReadUInt16() : ReadByte();
            PrepareRead(size);

            var result = Encoding.UTF8.GetString(mRawData, mPosition, size);
            mPosition += size;

            return result;
        }

        public void ReadBytes(byte[] bytes, int offset, int count)
        {
            Debug.Assert(bytes != null);
            Debug.Assert(offset >= 0);
            Debug.Assert(count >= 0);
            Debug.Assert(offset + count <= bytes.Length);

            PrepareRead(count);

            Array.Copy(mRawData, mPosition, bytes, offset, count);

            mPosition += count;
        }

        // Internal

        [StructLayout(LayoutKind.Explicit)]
        private struct TypeConvert
        {
            [FieldOffset(0)]
            public int int32;

            [FieldOffset(0)]
            public uint uint32;

            [FieldOffset(0)]
            public short int16;

            [FieldOffset(0)]
            public ushort ushort16;

            [FieldOffset(0)]
            public float single;

            [FieldOffset(0)]
            public byte byte0;

            [FieldOffset(1)]
            public byte byte1;

            [FieldOffset(2)]
            public byte byte2;

            [FieldOffset(3)]
            public byte byte3;
        };

        private byte[] mRawData;
        private int mSize;
        private int mPosition;
        private bool mReallocAllowed;
        private TypeConvert mTypeConvert = new TypeConvert();

        private void PrepareWrite(int size)
        {
            if (mPosition + size > mRawData.Length)
            {
                Debug.Assert(mReallocAllowed);

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
            Debug.Assert(mPosition + size <= mSize);
        }
    }
}
