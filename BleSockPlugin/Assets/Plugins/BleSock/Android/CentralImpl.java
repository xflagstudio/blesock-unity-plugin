package xflag.plugins.bleSock;

import android.Manifest;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.BluetoothProfile;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanFilter;
import android.bluetooth.le.ScanResult;
import android.bluetooth.le.ScanSettings;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelUuid;
import android.os.Bundle;
import android.view.KeyEvent;
import android.view.View;
import android.view.inputmethod.InputMethodManager;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.Spinner;
import android.widget.TextView;

import com.unity3d.player.UnityPlayer;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.LinkedList;
import java.util.Timer;
import java.util.TimerTask;
import java.util.UUID;

public final class CentralImpl {

    private static final String NOTIFICATION_DESCRIPTOR_UUID = "00002902-0000-1000-8000-00805F9B34FB";
    private static final int UPDATE_INTERVAL = 1000;
    private static final int REQUEST_MTU_SIZE = 512;
    private static final int MESSAGE_SIZE_MAX = 4096;
    private static final int BUFFER_SIZE = 8192;
    private static final int ACCEPTANCE_TIMEOUT = 20000;

    private enum Status {
    	
        Invalid,
        // Initialize,
        Ready,
        Scan,
        Connect,
        Discover,
        Online,
        Disconnect;
    }

    private Object mLockObject = new Object();
    private Status mStatus = Status.Invalid;

    // Initialization

    private UUID mServiceUUID = null;
    private UUID mUploadUUID = null;
    private UUID mDownloadUUID = null;
    private CentralCallback mCentralCallback = null;


    public boolean initialize(String serviceUUID, String uploadUUID, String downloadUUID, CentralCallback callback) {

        synchronized (mLockObject) {

            if (mStatus != Status.Invalid) {
                Utils.error("invalid status: %s", mStatus.toString());
                return false;
            }

            BluetoothAdapter adapter = BluetoothAdapter.getDefaultAdapter();
            if (adapter == null) {
                Utils.error("bluetooth is not available on this device");
                return false;
            }

            try {

                mServiceUUID = UUID.fromString(serviceUUID);
                mUploadUUID = UUID.fromString(uploadUUID);
                mDownloadUUID = UUID.fromString(downloadUUID);
            }
            catch (Exception e) {

                Utils.error(e.toString());
                cleanup();
                return false;
            }

            mCentralCallback = callback;

            Utils.info("ready");
            mStatus = Status.Ready;

            mCentralCallback.onReady();
        }

        return true;
    }

    // Scan peripherals

    private BluetoothLeScanner mScanner = null;
    private ScanCallback mScanCallback = new ScanCallback() {

        @Override
        public void onScanResult(int callbackType, ScanResult result) {

            Utils.info("onScanResult callbackType: %d result:%s", callbackType, result.toString());

            synchronized (mLockObject) {

                if (mStatus != Status.Scan) {
                    Utils.error("invalid status: %s", mStatus.toString());
                    return;
                }

                if (callbackType != ScanSettings.CALLBACK_TYPE_ALL_MATCHES) {
                    Utils.error("invalid callbackType");
                    return;
                }

                String deviceName = result.getScanRecord().getDeviceName();
                if (deviceName == null) {
                    Utils.error("device name is null");
                    return;
                }

                String address = result.getDevice().getAddress();

                for (PeripheralContext context : mDiscoveredPeripherals) {
                    if (context.device.getAddress().equalsIgnoreCase(address)) {
                        return;
                    }
                }

                final PeripheralContext context = new PeripheralContext(result.getDevice(), mNextPeripheralId++, deviceName);
                mDiscoveredPeripherals.add(context);

                Utils.info("peripheral discovered: %s", result.getDevice().toString());

                mCentralCallback.onDiscover(context.name, context.id);
            }
        }

        @Override
        public void onScanFailed(int errorCode) {

            Utils.error("onScanFailed: %d", errorCode);

            stopScan();

            mCentralCallback.onFail();
        }
    };

    private class PeripheralContext {
        public final BluetoothDevice device;
        public final int id;
        public final String name;

        public PeripheralContext(BluetoothDevice device, int id, String name) {
            this.device = device;
            this.id = id;
            this.name = name;
        }
    }

    private Timer mScanTimer = null;
    private ArrayList<PeripheralContext> mDiscoveredPeripherals = new ArrayList<>();
    private int mNextPeripheralId = 1;

    public boolean startScan() {

        synchronized (mLockObject) {

            if (mStatus == Status.Scan) {
                Utils.error("already scanning");
                return true;
            }

            if (mStatus != Status.Ready) {
                Utils.error("invalid status: %d", mStatus.toString());
                return false;
            }

            mStatus = Status.Scan;
            mDiscoveredPeripherals.clear();

            TimerTask timerTask = new TimerTask() {

                @Override
                public void run() {

                    synchronized (mLockObject) {

                        if (mStatus != Status.Scan) {
                            Utils.error("invalid status: %s", mStatus.toString());
                            return;
                        }

                        if (Utils.isBluetoothEnabled()) {
                            if (mScanner == null) {
                                startScanInternal();
                            }
                        }
                        else if (mScanner != null) {
                            stopScanInternal();
                        }
                    }
                }
            };

            mScanTimer = new Timer();
            mScanTimer.schedule(timerTask, 0, UPDATE_INTERVAL);

            /*
            if (Build.VERSION.SDK_INT >= 23) {
                if (checkSelfPermission(Manifest.permission.ACCESS_COARSE_LOCATION) != PackageManager.PERMISSION_GRANTED) {
                    requestPermissions(new String[] {Manifest.permission.ACCESS_COARSE_LOCATION}, PERMISSION_REQUEST_CODE);
                    return true;
                }
            }
            */

            if (!Utils.isBluetoothEnabled()) {
                Utils.info("bluetooth required..");
                mCentralCallback.onBluetoothRequire();
            }
        }

        return true;
    }

    private boolean startScanInternal() {
        if (mScanner != null) {
            Utils.error("scanner is not null");
            return false;
        }

        BluetoothAdapter adapter = BluetoothAdapter.getDefaultAdapter();
        if (adapter == null) {
            Utils.error("bluetooth is not available on this device");
            return false;
        }

        mScanner = adapter.getBluetoothLeScanner();
        if (mScanner == null) {
            Utils.error("failed to get scanner");
            return false;
        }

        Utils.info("startScan");
        ParcelUuid uuid = new ParcelUuid(mServiceUUID);
        ScanFilter filter = new ScanFilter.Builder().setServiceUuid(uuid).build();
        ArrayList<ScanFilter> filters = new ArrayList<ScanFilter>(Arrays.asList(filter));
        ScanSettings settings = new ScanSettings.Builder().setScanMode(ScanSettings.SCAN_MODE_BALANCED).build();
        mScanner.startScan(filters, settings, mScanCallback);

        return true;
    }

    public void stopScan() {

        synchronized (mLockObject) {

            if (mStatus != Status.Scan) {
                Utils.error("invalid status: %s", mStatus.toString());
                return;
            }

            mStatus = Status.Ready;

            if (mScanner != null) {
                stopScanInternal();
            }

            mScanTimer.cancel();
            mScanTimer = null;
        }
    }

    private void stopScanInternal() {
        if (mScanner == null) {
            Utils.error("scanner is null");
            return;
        }

        Utils.info("stopScan");

        try {

            mScanner.stopScan(mScanCallback);
        }
        catch (Exception e) {

            Utils.error(e.toString());
        }

        mScanner = null;
    }

    // Connection

    private BluetoothGatt mGatt = null;
    private Timer mAcceptanceTimer = null;
    private int mMaximumWriteLength = 20;

    private Timer mDiscoverTimer = null;
    private BluetoothGattCharacteristic mUploadCharacteristic = null;
    private BluetoothGattCharacteristic mDownloadCharacteristic = null;
    private BluetoothGattDescriptor mNotificationDescriptor = null;

    private BluetoothGattCallback mGattCallback = new BluetoothGattCallback() {

        @Override
        public void onPhyUpdate(BluetoothGatt gatt, int txPhy, int rxPhy, int status) {

            Utils.info("onPhyUpdate gatt: %s txPhy: %d rxPhy: %d status: %d", gatt.toString(), txPhy, rxPhy, status);
        }

        @Override
        public void onPhyRead(BluetoothGatt gatt, int txPhy, int rxPhy, int status) {

            Utils.info("onPhyRead gatt: %s txPhy: %d rxPhy: %d status: %d", gatt.toString(), txPhy, rxPhy, status);
        }

        @Override
        public void onConnectionStateChange(BluetoothGatt gatt, int status, int newState) {

            Utils.info("onConnectionStateChange gatt: %s status: %d, newState: %d", gatt.toString(), status, newState);

            synchronized (mLockObject) {

                if (gatt != mGatt) {
                    Utils.error("invalid gatt");
                    return;
                }

                if (newState == BluetoothProfile.STATE_CONNECTED) {

                    Utils.info("connected to peripheral");

                    if (mStatus != Status.Connect) {
                        Utils.error("invalid status: %s", mStatus.toString());
                        handleError();
                        return;
                    }

                    Utils.info("requestMtu: %d", REQUEST_MTU_SIZE);
                    if (!mGatt.requestMtu(REQUEST_MTU_SIZE)) {
                        Utils.error("failed");
                        handleError();
                        return;
                    }

                    startDiscoverServices();
                }
                else if (newState == BluetoothProfile.STATE_DISCONNECTED) {

                    Utils.info("disconnected from peripheral");

                    if ((mStatus != Status.Connect) && (mStatus != Status.Discover) && (mStatus != Status.Online) && (mStatus != mStatus.Disconnect)) {

                        Utils.error("invalid status: %s", mStatus.toString());
                        return;
                    }

                    if ((mStatus == Status.Connect) && (status == 133)) // Busy
                    {
                        BluetoothDevice bluetoothDevice = mGatt.getDevice();
                        Context context = UnityPlayer.currentActivity.getApplicationContext();

                        Utils.info("connectGatt: %s", bluetoothDevice.getAddress());

                        mGatt = bluetoothDevice.connectGatt(context, false, mGattCallback);
                        if (mGatt != null) {
                            return;
                        }

                        Utils.error("failed");
                    }

                    final boolean disconnected = (mStatus == Status.Online) || (mStatus == Status.Disconnect);

                    mStatus = Status.Ready;

                    cleanupConnection();

                    if (disconnected) {
                        mCentralCallback.onDisconnect();
                    }
                    else {
                        mCentralCallback.onFail();
                    }
                }
                else  {

                    Utils.error("invalid newState");
                    handleError();
                }
            }
        }

        @Override
        public void onServicesDiscovered(BluetoothGatt gatt, int status) {

            Utils.info("onServicesDiscovered gatt: %s status: %d", gatt.toString(), status);

            synchronized (mLockObject) {

                if (gatt != mGatt) {
                    Utils.error("invalid gatt");
                    return;
                }

                if (mStatus != Status.Discover) {
                    Utils.error("invalid status: %s", mStatus.toString());
                    handleError();
                    return;
                }

                if (status != BluetoothGatt.GATT_SUCCESS) {
                    Utils.error("failed");
                    handleError();
                    return;
                }

                Utils.info("services discovered");
                mDiscoverTimer.cancel();
                mDiscoverTimer = null;

                // Communication characteristic

                BluetoothGattService service = mGatt.getService(mServiceUUID);
                if (service == null) {
                    Utils.error("communication service not found");
                    handleError();
                    return;
                }

                mUploadCharacteristic = service.getCharacteristic(mUploadUUID);
                if (mUploadCharacteristic == null) {
                    Utils.error("upload characteristic not found");
                    handleError();
                    return;
                }

                mDownloadCharacteristic = service.getCharacteristic(mDownloadUUID);
                if (mDownloadCharacteristic == null) {
                    Utils.error("download characteristic not found");
                    handleError();
                    return;
                }

                // Enable notification

                if (!mGatt.setCharacteristicNotification(mDownloadCharacteristic, true)) {
                    Utils.error("set characteristic notification failed");
                    handleError();
                    return;
                }

                mNotificationDescriptor = mDownloadCharacteristic.getDescriptor(UUID.fromString(NOTIFICATION_DESCRIPTOR_UUID));
                if (mNotificationDescriptor == null) {
                    Utils.error("notification descriptor not found");
                    handleError();
                    return;
                }

                mNotificationDescriptor.setValue(BluetoothGattDescriptor.ENABLE_INDICATION_VALUE);

                Utils.info("writeDescriptor: ENABLE_INDICATION_VALUE");
                if (!mGatt.writeDescriptor(mNotificationDescriptor)) {
                    Utils.error("failed");
                    handleError();
                    return;
                }

                // Etc

                Utils.info("requestConnectionPriority");
                if (!mGatt.requestConnectionPriority(BluetoothGatt.CONNECTION_PRIORITY_HIGH)) {
                    Utils.error("failed");
                    handleError();
                    return;
                }
            }
        }

        @Override
        public void onCharacteristicRead(
                BluetoothGatt gatt,
                BluetoothGattCharacteristic characteristic,
                int status) {

            Utils.info("onCharacteristicRead gatt: %s characteristic: %s status: %d",
                    gatt.toString(), characteristic.toString(), status); // readCharacteristicのレスポンスが帰ってきた

            synchronized (mLockObject) {

                if (gatt != mGatt) {
                    Utils.error("invalid gatt");
                    return;
                }

                if (characteristic != mDownloadCharacteristic) {
                    Utils.error("invalid characteristic");
                    return;
                }

                if (mStatus != Status.Online) {
                    Utils.error("invalid status: %s", mStatus.toString());
                    handleError();
                    return;
                }

                if (status != BluetoothGatt.GATT_SUCCESS) {
                    Utils.error("failed");
                    handleError();
                    return;
                }

                mReadWriteLock = false;

                byte[] value = characteristic.getValue();
                if ((value != null) && (value.length > 1)) {

                    if (processReceiveBuffer(value)) {

                        if (!mOperations.contains(Operation.Read)) {

                            mOperations.add(Operation.Read);
                        }
                    }
                }

                processOperation();
            }
        }

        @Override
        public void onCharacteristicWrite(
                BluetoothGatt gatt,
                BluetoothGattCharacteristic characteristic,
                int status) {

            Utils.info("onCharacteristicWrite gatt: %s characteristic: %s status: %d",
                    gatt.toString(), characteristic.toString(), status); // writeCharacteristicのレスポンスが帰ってきた

            synchronized (mLockObject) {

                if (gatt != mGatt) {
                    Utils.error("invalid gatt");
                    return;
                }

                if (characteristic != mUploadCharacteristic) {
                    Utils.error("invalid characteristic");
                    return;
                }

                if (mStatus != Status.Online) {
                    Utils.error("invalid status: %s", mStatus.toString());
                    handleError();
                    return;
                }

                if (status != BluetoothGatt.GATT_SUCCESS) {
                    Utils.error("failed");
                    handleError();
                    return;
                }

                mReadWriteLock = false;

                processOperation();
            }
        }

        @Override
        public void onCharacteristicChanged(
                BluetoothGatt gatt,
                BluetoothGattCharacteristic characteristic) {

            Utils.info("onCharacteristicChanged gatt: %s characteristic: %s",
                    gatt.toString(), characteristic.toString()); // Notificationが送られてきた

            synchronized (mLockObject) {

                if (gatt != mGatt) {
                    Utils.error("invalid gatt");
                    return;
                }

                if (characteristic != mDownloadCharacteristic) {
                    Utils.error("invalid characteristic");
                    return;
                }

                if (mStatus != Status.Online) {
                    Utils.error("invalid status: %s", mStatus.toString());
                    handleError();
                    return;
                }

                byte[] value = characteristic.getValue();
                if ((value == null) || (value.length == 0)) {
                    Utils.info("invalid value");
                    return;
                }

                if (processReceiveBuffer(value)) {

                    if (!mReadWriteLock) {

                        Utils.info("readCharacteristic");
                        if (!mGatt.readCharacteristic(mDownloadCharacteristic)) {
                            Utils.error("failed");
                            handleError();
                            return;
                        }

                        mReadWriteLock = true;
                    }
                    else if (!mOperations.contains(Operation.Read)) {

                        mOperations.add(Operation.Read);
                    }
                }
            }
        }

        private boolean processReceiveBuffer(byte[] value) {

            Utils.info("received: %d bytes remain %d bytes", value.length - 1, mReceiveBuffer.position());

            try
            {
                mReceiveBuffer.put(value, 0, value.length - 1);
            }
            catch (Exception e)
            {
                Utils.error(e.toString());
                handleError();
                return false;
            }

            boolean willContinue = (value[value.length - 1] != 0);

            while (true) {

                if (mReceiveMessageSize == -1) {

                    if (mReceiveBuffer.position() < 2) {
                        break;
                    }

                    mReceiveBuffer.flip();
                    mReceiveMessageSize = mReceiveBuffer.getChar();
                    mReceiveBuffer.compact();
                }

                if (mReceiveMessageAddress == -1) {

                    if (mReceiveBuffer.position() < 2) {
                        break;
                    }

                    mReceiveBuffer.flip();
                    mReceiveMessageAddress = mReceiveBuffer.getChar();
                    mReceiveBuffer.compact();
                }

                if (mReceiveBuffer.position() < mReceiveMessageSize) {
                    break;
                }

                final byte[] message = new byte[mReceiveMessageSize];
                if (mReceiveMessageSize > 0) {

                    mReceiveBuffer.flip();
                    mReceiveBuffer.get(message);
                    mReceiveBuffer.compact();
                }

                final int from = mReceiveMessageAddress;

                mReceiveMessageSize = -1;
                mReceiveMessageAddress = -1;

                mCentralCallback.onReceive(new Buffer(message), from);
            }

            return willContinue;
        }

        @Override
        public void onDescriptorRead(
                BluetoothGatt gatt,
                BluetoothGattDescriptor descriptor,
                int status) {

            Utils.info("onDescriptorRead gatt: %s descriptor: %s status: %d",
                    gatt.toString(), descriptor.toString(), status);

            Utils.error("invalid operation");
        }

        @Override
        public void onDescriptorWrite(
                BluetoothGatt gatt,
                BluetoothGattDescriptor descriptor,
                int status) {

            Utils.info("onDescriptorWrite gatt: %s descriptor: %s status: %d",
                    gatt.toString(), descriptor.toString(), status);

            synchronized (mLockObject) {

                if (gatt != mGatt) {
                    Utils.error("invalid gatt");
                    return;
                }

                if (descriptor != mNotificationDescriptor) {
                    Utils.error("invalid descriptor");
                    return;
                }

                if (mStatus != Status.Discover) {
                    Utils.error("invalid status: %s", mStatus.toString());
                    handleError();
                    return;
                }

                if (status != BluetoothGatt.GATT_SUCCESS) {
                    Utils.error("failed");
                    handleError();
                    return;
                }

                // write dummy response

                mUploadCharacteristic.setValue(new byte[0]);

                Utils.info("writeCharacteristic: void");
                if (!mGatt.writeCharacteristic(mUploadCharacteristic)) {
                    Utils.error("failed");
                    handleError();
                    return;
                }

                mReadWriteLock = true;

                Utils.info("online");
                mStatus = Status.Online;

                mCentralCallback.onConnect();
            }
        }

        @Override
        public void onReliableWriteCompleted(BluetoothGatt gatt, int status) {

            Utils.info("onReliableWriteCompleted gatt: %s status: %d", gatt.toString(), status);
        }

        @Override
        public void onReadRemoteRssi(BluetoothGatt gatt, int rssi, int status) {

            Utils.info("onReadRemoteRssi gatt: %s rssi: %d status: %d", gatt.toString(), rssi, status);
        }

        @Override
        public void onMtuChanged(BluetoothGatt gatt, int mtu, int status) {

            Utils.info("onMtuChanged gatt: %s mtu: %d status: %d", gatt.toString(), mtu, status);

            synchronized (mLockObject) {

                if (gatt != mGatt) {
                    Utils.error("invalid gatt");
                    return;
                }

                if (status == BluetoothGatt.GATT_SUCCESS) {
                    mMaximumWriteLength = mtu - 3;
                }
                else {
                    Utils.error("failed");
                }
            }
        }
    };

    private void startDiscoverServices() {

        mStatus = Status.Discover;

        TimerTask timerTask = new TimerTask() {

            private int mCount = 1;

            @Override
            public void run() {

                synchronized (mLockObject) {

                    if (mStatus != Status.Discover) {
                        Utils.error("invalid status: %s", mStatus.toString());
                        handleError();
                        return;
                    }

                    Utils.info("discoverServices [%d]", mCount++);
                    if (!mGatt.discoverServices()) {
                        Utils.error("failed");
                        handleError();
                    }
                }
            }
        };

        mDiscoverTimer = new Timer();
        mDiscoverTimer.schedule(timerTask, UPDATE_INTERVAL, UPDATE_INTERVAL);
    }

    private void handleError() {

        synchronized (mLockObject) {

            if (mStatus == Status.Connect) {

                mStatus = Status.Ready;

                cleanupConnection();

                mCentralCallback.onFail();
            }
            else if ((mStatus == Status.Discover) || (mStatus == Status.Online)) {

                Utils.info("disconnect");
                mGatt.disconnect();

                if (mAcceptanceTimer != null) {
                    mAcceptanceTimer.cancel();
                    mAcceptanceTimer = null;
                }
            }
        }
    }

    private void cleanupConnection() {

        if (mGatt != null) {
            Utils.info("close");
            mGatt.close();
            mGatt = null;
        }

        if (mAcceptanceTimer != null) {
            mAcceptanceTimer.cancel();
            mAcceptanceTimer = null;
        }

        if (mDiscoverTimer != null) {
            mDiscoverTimer.cancel();
            mDiscoverTimer = null;
        }

        mUploadCharacteristic = null;
        mDownloadCharacteristic = null;
        mNotificationDescriptor = null;

        mSendBuffer.clear();
        mReceiveBuffer.clear();
        mReceiveMessageSize = -1;
        mReceiveMessageAddress = -1;
        mReadWriteLock = false;
        mOperations.clear();
    }

    public boolean connect(int peripheralId) {

        synchronized (mLockObject) {

            if ((mStatus != Status.Ready) && (mStatus != Status.Scan)) {
                Utils.error("invalid status: %s", mStatus.toString());
                return false;
            }

            BluetoothDevice bluetoothDevice = null;
            for (PeripheralContext context : mDiscoveredPeripherals) {
                if (context.id == peripheralId) {
                    bluetoothDevice = context.device;
                    break;
                }
            }

            if (bluetoothDevice == null) {
                Utils.error("invalid peripheralId: %d", peripheralId);
                return false;
            }

            if (mStatus == Status.Scan) {
                stopScan();
            }

            Context context = UnityPlayer.currentActivity.getApplicationContext();

            Utils.info("connectGatt: %s", bluetoothDevice.getAddress());
            mGatt = bluetoothDevice.connectGatt(context, false, mGattCallback);
            if (mGatt == null) {
                Utils.error("failed");
                return false;
            }

            TimerTask timerTask = new TimerTask() {

                @Override
                public void run() {

                    synchronized (mLockObject) {

                        Utils.error("connection timeout");
                        handleError();
                    }
                }
            };

            mAcceptanceTimer = new Timer();
            mAcceptanceTimer.schedule(timerTask, ACCEPTANCE_TIMEOUT);

            mStatus = Status.Connect;
        }

        return true;
    }

    public void accept() {

        synchronized (mLockObject) {

            if (mStatus != Status.Online) {
                Utils.error("invalid status: %s", mStatus.toString());
                return;
            }

            if (mAcceptanceTimer != null) {
                mAcceptanceTimer.cancel();
                mAcceptanceTimer = null;
            }
        }
    }

    public void disconnect() {

        synchronized (mLockObject) {

            if ((mStatus == Status.Connect) || (mStatus == Status.Discover)) {

                mStatus = Status.Ready;

                cleanupConnection();
            }
            else if (mStatus == Status.Online) {

                mStatus = Status.Disconnect;

                Utils.info("disconnect");
                mGatt.disconnect();

                if (mAcceptanceTimer != null) {
                    mAcceptanceTimer.cancel();
                    mAcceptanceTimer = null;
                }
            }
            else {

                Utils.error("invalid status: %s", mStatus.toString());
            }
        }
    }

    // Communication

    private ByteBuffer mSendBuffer = ByteBuffer.allocate(BUFFER_SIZE).order(ByteOrder.LITTLE_ENDIAN);
    private ByteBuffer mReceiveBuffer = ByteBuffer.allocate(BUFFER_SIZE).order(ByteOrder.LITTLE_ENDIAN);
    private int mReceiveMessageSize = -1;
    private int mReceiveMessageAddress = -1;

    private boolean mReadWriteLock = false;

    private enum Operation
    {
        Read,
        Write,
    }

    private LinkedList<Operation> mOperations = new LinkedList<>();


    public boolean send(byte[] message, int messageSize, int to) {

        synchronized (mLockObject) {

            if (mStatus != Status.Online) {
                Utils.error("invalid status: %s", mStatus.toString());
                return false;
            }

            if (message == null) {
                Utils.error("message is null");
                return false;
            }

            if ((messageSize < 0) || (messageSize > message.length)) {
                Utils.error("invalid message size");
                return false;
            }

            if (messageSize > MESSAGE_SIZE_MAX) {
                Utils.error("message size too large");
                return false;
            }

            try
            {
                mSendBuffer.putChar((char)messageSize);
                mSendBuffer.putChar((char)to);
                mSendBuffer.put(message, 0, messageSize);
            }
            catch (Exception e)
            {
                Utils.error(e.toString());
                handleError();
                return false;
            }

            if (!mReadWriteLock) {

                processSendBuffer();
            }
            else if (!mOperations.contains(Operation.Write)) {

                mOperations.add(Operation.Write);
            }
        }

        return true;
    }

    private void processSendBuffer() {

        if (mSendBuffer.position() == 0) {
            return;
        }

        mSendBuffer.flip();

        int size = Math.min(mSendBuffer.limit(), mMaximumWriteLength);
        byte[] value = new byte[size];

        mSendBuffer.get(value);
        mSendBuffer.compact();

        mUploadCharacteristic.setValue(value);

        Utils.info("writeCharacteristic: %d bytes remain %d bytes", size, mSendBuffer.position());
        if (!mGatt.writeCharacteristic(mUploadCharacteristic)) {
            Utils.error("failed");
            handleError();
            return;
        }

        mReadWriteLock = true;
    }

    private void processOperation() {

        if (mOperations.isEmpty()) {
            return;
        }

        if (mOperations.remove() == Operation.Read) {

            Utils.info("readCharacteristic");
            if (!mGatt.readCharacteristic(mDownloadCharacteristic)) {
                Utils.error("failed");
                handleError();
                return;
            }

            mReadWriteLock = true;
        }
        else {

            processSendBuffer();
        }
    }

    // Cleanup

    public void cleanup() {

        synchronized (mLockObject) {

            if (mStatus == Status.Scan) {
                stopScan();
            }

            mStatus = Status.Invalid;

            cleanupConnection();

            mServiceUUID = null;
            mUploadUUID = null;
            mDownloadUUID = null;
            mCentralCallback = null;
        }
    }
}
