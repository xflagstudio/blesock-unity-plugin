package xflag.plugins.bleSock;

public interface PeripheralCallback {

    public void onBluetoothRequire();
    public void onReady();
    public void onFail();

    public void onConnect(int connectionId);
    public void onDisconnect(int connectionId);

    public void onReceiveDirect(Buffer message, int connectionId);
    public void onReceive(Buffer message, int playerId);
}
