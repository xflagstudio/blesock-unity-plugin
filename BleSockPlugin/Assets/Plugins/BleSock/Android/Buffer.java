package xflag.plugins.bleSock;

public final class Buffer {

    private byte[] mBytes;

    public byte[] getBytes() {

        return mBytes;
    }

    public Buffer(byte[] bytes) {

        mBytes = bytes;
    }
}
