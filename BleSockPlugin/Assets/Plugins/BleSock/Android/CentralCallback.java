package xflag.plugins.bleSock;

public interface CentralCallback {

    public void onBluetoothRequire();
    public void onReady();
    public void onFail();

    public void onDiscover(String deviceName, int deviceId);
    public void onConnect();
    public void onDisconnect();

    public void onReceive(Buffer message, int from);
}
