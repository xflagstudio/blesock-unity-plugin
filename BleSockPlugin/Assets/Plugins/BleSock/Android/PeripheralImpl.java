package xflag.plugins.bleSock;

import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattServer;
import android.bluetooth.BluetoothGattServerCallback;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothProfile;
import android.bluetooth.le.AdvertiseCallback;
import android.bluetooth.le.AdvertiseData;
import android.bluetooth.le.AdvertiseSettings;
import android.bluetooth.le.BluetoothLeAdvertiser;
import android.content.Context;
import android.content.Intent;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelUuid;
import android.os.Bundle;
import android.view.KeyEvent;
import android.view.View;
import android.view.inputmethod.InputMethodManager;
import android.widget.EditText;
import android.widget.TextView;

import com.unity3d.player.UnityPlayer;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.security.SecureRandom;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.LinkedList;
import java.util.Timer;
import java.util.TimerTask;
import java.util.UUID;

public final class PeripheralImpl {

    private static final String NOTIFICATION_DESCRIPTOR_UUID = "00002902-0000-1000-8000-00805F9B34FB";
    private static final int UPDATE_INTERVAL = 1000;
    private static final int MESSAGE_SIZE_MAX = 4096;
    private static final int BUFFER_SIZE = 8192;
    private static final int ACCEPTANCE_TIMEOUT = 19000;

    private enum Status {

        Invalid,
        Initialize,
        Ready,
        Advertise;
    }

    private Object mLockObject = new Object();
    private Status mStatus = Status.Invalid;

    // Initialization

    private UUID mServiceUUID = null;
    private UUID mUploadUUID = null;
    private UUID mDownloadUUID = null;
    private PeripheralCallback mPeripheralCallback = null;

    private Timer mInitializationTimer = null;
    private BluetoothGattServer mGattServer = null;
    private BluetoothGattDescriptor mNotificationDescriptor = null;
    private BluetoothGattCharacteristic mDownloadCharacteristic = null;
    private BluetoothGattCharacteristic mUploadCharacteristic = null;
    private BluetoothGattService mCommunicationService = null;

    private class CentralContext {

        public final BluetoothDevice device;
        public boolean subscribed = false;
        public int connectionId = 0;
        public Timer acceptanceTimer = null;
        public int maximumWriteLength = 20;
        public String secondaryAddress = null;
        public ByteBuffer receiveBuffer = ByteBuffer.allocate(BUFFER_SIZE).order(ByteOrder.LITTLE_ENDIAN);
        public int receiveMessageSize = -1;
        public int receiveMessageAddress = -1;
        public ByteBuffer sendBuffer = ByteBuffer.allocate(BUFFER_SIZE).order(ByteOrder.LITTLE_ENDIAN);
        public boolean valueWriting = false;
        public int playerId = 0;

        public CentralContext(BluetoothDevice device) {

            this.device = device;
        }
    }

    private ArrayList<CentralContext> mConnectedCentrals = new ArrayList<>();
    private int mNextConnectionId = 1;

    private BluetoothGattServerCallback mGattCallback = new BluetoothGattServerCallback() {

        @Override
        public void onConnectionStateChange(BluetoothDevice device, int status, int newState) {

            Utils.info("onConnectionStateChange device: %s status: %d newState: %d",
                    device.toString(), status, newState);

            synchronized (mLockObject) {

                CentralContext context = null;
                for (CentralContext ctx : mConnectedCentrals) {

                    if (ctx.device.getAddress().equalsIgnoreCase(device.getAddress())) {

                        context = ctx;
                        break;
                    }
                }

                if (newState == BluetoothProfile.STATE_CONNECTED) {

                    if (mStatus != Status.Advertise) {
                        Utils.error("invalid status: %s", mStatus.toString());
                        return;
                    }

                    if (context != null) {
                        Utils.error("already connected");
                        return;
                    }

                    Utils.info("central connected: %s", device.getAddress());
                    mConnectedCentrals.add(new CentralContext(device));
                }
                else if (newState == BluetoothProfile.STATE_DISCONNECTED) {

                    if ((mStatus != Status.Ready) && (mStatus != Status.Advertise)) {
                        Utils.error("invalid status: %s", mStatus.toString());
                        return;
                    }

                    if (context == null) {
                        Utils.error("invalid device");
                        return;
                    }

                    int connectionId = context.connectionId;

                    Utils.info("central disconnected: %s", device.getAddress());
                    mConnectedCentrals.remove(context);
                    unsubscribed(context);

                    if ((mNotifyingConnectionId != 0) && (connectionId == mNotifyingConnectionId)) {

                        mNotifyingConnectionId = 0;
                        processNotificationQueue();
                    }
                }
                else {

                    Utils.error("invalid newState");
                }
            }
        }

        @Override
        public void onServiceAdded(int status, BluetoothGattService service) {

            Utils.info("onServiceAdded status: %d service: %s", status, service.toString());

            synchronized (mLockObject) {

                if (mStatus != Status.Initialize) {
                    Utils.error("invalid status: %s", mStatus.toString());
                    return;
                }

                if (status != BluetoothGatt.GATT_SUCCESS) {
                    Utils.error("failed");
                    onFail();
                    return;
                }

                Utils.info("ready");
                mStatus = Status.Ready;

                mPeripheralCallback.onReady();
            }
        }

        @Override
        public void onCharacteristicReadRequest(
                BluetoothDevice device,
                int requestId,
                int offset,
                BluetoothGattCharacteristic characteristic) {

            Utils.info("onCharacteristicReadRequest device: %s requestId: %d offset: %d characteristic: %s",
                    device.toString(), requestId, offset, characteristic.toString());

            synchronized (mLockObject) {

                if (characteristic != mDownloadCharacteristic) {
                    Utils.error("invalid characteristic");
                    mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_FAILURE, 0, null);
                    return;
                }

                if ((mStatus != Status.Ready) && (mStatus != Status.Advertise)) {
                    Utils.error("invalid status: %s", mStatus.toString());
                    mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_FAILURE, 0, null);
                    return;
                }

                CentralContext context = null;
                for (CentralContext ctx : mConnectedCentrals) {

                    if (ctx.device.getAddress().equalsIgnoreCase(device.getAddress())) {

                        context = ctx;
                        break;
                    }
                }

                if ((context == null) || !context.subscribed) {
                    Utils.error("invalid device");
                    mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_FAILURE, 0, null);
                    return;
                }

                if (offset != 0) {
                    Utils.error("invalid parameter");
                    mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_FAILURE, 0, null);
                    unsubscribed(context);
                    return;
                }

                byte[] value = null;

                if (context.sendBuffer.position() > 0) {

                    value = processSendBuffer(context);
                    Utils.info("sendResponse: %d bytes remain %d bytes %s ",
                            value.length, context.sendBuffer.position(), device.getAddress());
                }
                else {

                    Utils.info("sendResponse: null");
                    context.valueWriting = false;
                }

                if (!mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, 0, value)) {
                    Utils.error("failed");
                    unsubscribed(context);
                }
            }
        }

        @Override
        public void onCharacteristicWriteRequest(
                BluetoothDevice device,
                int requestId,
                BluetoothGattCharacteristic characteristic,
                boolean preparedWrite,
                boolean responseNeeded,
                int offset,
                byte[] value) {

            Utils.info("onCharacteristicWriteRequest device: %s requestId: %d characteristic: %s preparedWrite: %b responseNeeded: %b, offset: %d, value: %d bytes",
                    device.toString(), requestId, characteristic.toString(), preparedWrite, responseNeeded, offset, value.length);

            synchronized (mLockObject) {

                if (characteristic != mUploadCharacteristic) {
                    Utils.error("invalid characteristic");
                    mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_FAILURE, 0, value);
                    return;
                }

                if ((mStatus != Status.Ready) && (mStatus != Status.Advertise)) {
                    Utils.error("invalid status: %s", mStatus.toString());
                    mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_FAILURE, 0, value);
                    return;
                }

                CentralContext context = null;
                for (CentralContext ctx : mConnectedCentrals) {

                    if (ctx.device.getAddress().equalsIgnoreCase(device.getAddress())) {

                        context = ctx;
                        break;
                    }
                }

                if ((context == null) || !context.subscribed) {
                    Utils.error("invalid device");
                    mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_FAILURE, 0, value);
                    return;
                }

                if (preparedWrite || !responseNeeded || (offset != 0) || (value == null)) {
                    Utils.error("invalid parameter");
                    mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_FAILURE, 0, value);
                    unsubscribed(context);
                    return;
                }

                // Utils.info("sendResponse ack");
                if (!mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, 0, value)) {
                    Utils.error("failed");
                    unsubscribed(context);
                    return;
                }

                if (context.connectionId == 0) { // Negotiation complete

                    final int connectionId = mNextConnectionId++;
                    context.connectionId = connectionId;

                    mPeripheralCallback.onConnect(connectionId);
                }
                else {

                    processReceiveBuffer(context, value);
                }
            }
        }

        @Override
        public void onDescriptorReadRequest(
                BluetoothDevice device,
                int requestId,
                int offset,
                BluetoothGattDescriptor descriptor) {

            Utils.info("onDescriptorReadRequest device: %s requestId: %d offset: %d descriptor: %s",
                    device.toString(), requestId, offset, descriptor.toString());

            Utils.error("invalid operation");
        }

        @Override
        public void onDescriptorWriteRequest(
                BluetoothDevice device,
                int requestId,
                BluetoothGattDescriptor descriptor,
                boolean preparedWrite,
                boolean responseNeeded,
                int offset,
                byte[] value) {

            Utils.info("onDescriptorWriteRequest device: %s requestId: %d descriptor: %s preparedWrite: %b responseNeeded: %b, offset: %d, value: %d bytes",
                    device.toString(), requestId, descriptor.toString(), preparedWrite, responseNeeded, offset, value.length);

            synchronized (mLockObject) {

                if (descriptor != mNotificationDescriptor) {
                    Utils.error("invalid descriptor");
                    mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_FAILURE, 0, value);
                    return;
                }

                CentralContext context = null;
                for (CentralContext ctx : mConnectedCentrals) {

                    if (ctx.device.getAddress().equalsIgnoreCase(device.getAddress())) {
                        context = ctx;
                        break;
                    }
                }

                if (context == null) {
                    Utils.error("invalid device");
                    mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_FAILURE, 0, value);
                    return;
                }

                if (Arrays.equals(value, BluetoothGattDescriptor.ENABLE_INDICATION_VALUE)) { // Subscribe

                    if (mStatus != Status.Advertise) {
                        Utils.error("invalid status: %s", mStatus.toString());
                        mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_FAILURE, 0, value);
                        return;
                    }

                    // Utils.info("sendResponse ack");
                    if (!mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, 0, value)) {
                        Utils.error("failed");
                        return;
                    }

                    subscribed(context);
                }
                else if (Arrays.equals(value, BluetoothGattDescriptor.DISABLE_NOTIFICATION_VALUE)) { // Unsubscribe

                    if (!context.subscribed) {
                        Utils.error("not subscribed");
                        return;
                    }

                    // Utils.info("sendResponse ack");
                    if (!mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, 0, value)) {
                        Utils.error("failed");
                    }

                    unsubscribed(context);
                }
                else {

                    Utils.error("invalid value");
                    mGattServer.sendResponse(device, requestId, BluetoothGatt.GATT_FAILURE, 0, value);
                    unsubscribed(context);
                    return;
                }
            }
        }

        @Override
        public void onExecuteWrite(BluetoothDevice device, int requestId, boolean execute) {

            Utils.info("onExecuteWrite device: %s requestId: %d execute: %b",
                    device.toString(), requestId, execute);
        }

        @Override
        public void onNotificationSent(BluetoothDevice device, int status) {

            Utils.info("onNotificationSent device: %s status: %d", device.toString(), status);

            synchronized (mLockObject) {

                mNotifyingConnectionId = 0;
                processNotificationQueue();
            }
        }

        @Override
        public void onMtuChanged(BluetoothDevice device, int mtu) {

            Utils.info("onMtuChanged device: %s mtu: %d", device.toString(), mtu);

            synchronized (mLockObject) {

                for (CentralContext context : mConnectedCentrals) {

                    if (context.device.getAddress().equalsIgnoreCase(device.getAddress())) {

                        context.maximumWriteLength = mtu - 3;
                        context.secondaryAddress = device.getAddress();
                        return;
                    }
                }

                // HACK: iPhoneのセントラルのアドレスが一致しなくなる場合への対応
                for (CentralContext context : mConnectedCentrals) {

                    if (context.secondaryAddress == null) {

                        Utils.error("using workaround: %s as %s", context.device.getAddress(), device.getAddress());
                        context.maximumWriteLength = mtu - 3;
                        context.secondaryAddress = device.getAddress();
                        return;
                    }
                }

                Utils.error("could not apply mtu");
            }
        }

        @Override
        public void onPhyUpdate(BluetoothDevice device, int txPhy, int rxPhy, int status) {

            Utils.info("onPhyUpdate device: %s txPhy: %d rxPhy: %d status: %d",
                    device.toString(), txPhy, rxPhy, status);
        }

        @Override
        public void onPhyRead(BluetoothDevice device, int txPhy, int rxPhy, int status) {

            Utils.info("onPhyRead device: %s txPhy: %d rxPhy: %d status: %d",
                    device.toString(), txPhy, rxPhy, status);
        }
    };

    public boolean initialize(String serviceUUID, String uploadUUID, String downloadUUID, PeripheralCallback callback) {

        synchronized (mLockObject) {

            if (mStatus != Status.Invalid) {
                Utils.error("invalid status: %s", mStatus.toString());
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

            mStatus = Status.Initialize;
            mPeripheralCallback = callback;

            TimerTask timerTask = new TimerTask() {

                @Override
                public void run() {

                    synchronized (mLockObject) {

                        if (mStatus != Status.Initialize) {
                            Utils.error("invalid status: %s", mStatus.toString());
                            return;
                        }

                        if (Utils.isBluetoothEnabled()) {

                            mInitializationTimer.cancel();
                            mInitializationTimer = null;

                            if (!initializeInternal()) {
                                onFail();
                            }
                        }
                    }
                }
            };

            mInitializationTimer = new Timer();
            mInitializationTimer.schedule(timerTask, 0, UPDATE_INTERVAL);

            if (!Utils.isBluetoothEnabled()) {
                Utils.info("bluetooth required..");
                mPeripheralCallback.onBluetoothRequire();
            }
        }

        return true;
    }

    private boolean initializeInternal() {

        BluetoothAdapter adapter = BluetoothAdapter.getDefaultAdapter();
        if (adapter == null) {
            Utils.error("bluetooth is not available on this device");
            return false;
        }

        if (!adapter.isMultipleAdvertisementSupported()) {
            Utils.error("multiple advertisement is not available on this device");
            return false;
        }

        Context context = UnityPlayer.currentActivity.getApplicationContext();

        BluetoothManager manager = (BluetoothManager)context.getSystemService(Context.BLUETOOTH_SERVICE);
        mGattServer = manager.openGattServer(context, mGattCallback);
        if (mGattServer == null) {
            Utils.error("failed to open GATT server");
            return false;
        }

        mNotificationDescriptor = new BluetoothGattDescriptor(
                UUID.fromString(NOTIFICATION_DESCRIPTOR_UUID),
                BluetoothGattDescriptor.PERMISSION_WRITE);

        mDownloadCharacteristic = new BluetoothGattCharacteristic(
                mDownloadUUID,
                BluetoothGattCharacteristic.PROPERTY_READ | BluetoothGattCharacteristic.PROPERTY_INDICATE,
                BluetoothGattCharacteristic.PERMISSION_READ);
        mDownloadCharacteristic.addDescriptor(mNotificationDescriptor);

        mUploadCharacteristic = new BluetoothGattCharacteristic(
                mUploadUUID,
                BluetoothGattCharacteristic.PROPERTY_WRITE,
                BluetoothGattCharacteristic.PERMISSION_WRITE);

        mCommunicationService = new BluetoothGattService(mServiceUUID, BluetoothGattService.SERVICE_TYPE_PRIMARY);
        mCommunicationService.addCharacteristic(mDownloadCharacteristic);
        mCommunicationService.addCharacteristic(mUploadCharacteristic);

        Utils.info("addService");
        if (!mGattServer.addService(mCommunicationService)) {
            Utils.error("failed");
            return false;
        }

        return true;
    }

    private void subscribed(final CentralContext context) {

        if (context.subscribed) {

            Utils.info("already subscribed");
            return;
        }

        context.subscribed = true;

        TimerTask timerTask = new TimerTask() {

            @Override
            public void run() {

                synchronized (mLockObject) {

                    Utils.error("connection timeout");
                    unsubscribed(context);
                }
            }
        };

        context.acceptanceTimer = new Timer();
        context.acceptanceTimer.schedule(timerTask, ACCEPTANCE_TIMEOUT);

        Utils.info("central subscribed");
    }

    private void unsubscribed(CentralContext context) {

        if (!context.subscribed) {
            return;
        }

        context.subscribed = false;

        final int connectionId = context.connectionId;
        context.connectionId = 0;

        if (context.acceptanceTimer != null) {
            context.acceptanceTimer.cancel();
            context.acceptanceTimer = null;
        }

        context.receiveBuffer.clear();
        context.receiveMessageSize = -1;
        context.receiveMessageAddress = -1;
        context.sendBuffer.clear();
        context.valueWriting = false;
        context.playerId = 0;

        if (mNotificationQueue.contains(connectionId)) {
            mNotificationQueue.remove((Integer)connectionId);
        }

        Utils.info("central unsubscribed: %s", context.device.getAddress());

        if (connectionId != 0) {

            mPeripheralCallback.onDisconnect(connectionId);
        }
    }

    private void onFail() {

        mPeripheralCallback.onFail();
    }

    // Advertising

    private String mDeviceName = null;
    private Timer mAdvertiseTimer = null;
    private BluetoothLeAdvertiser mAdvertiser = null;
    private String mOriginalAdapterName = null;

    private AdvertiseCallback mAdvertiseCallback = new AdvertiseCallback() {
        @Override
        public void onStartSuccess(AdvertiseSettings settingsInEffect) {

            Utils.info("onStartSuccess settingsInEffect: %s", settingsInEffect.toString());

            restoreAdapterName();
        }

        @Override
        public void onStartFailure(int errorCode) {

            Utils.error("onStartFailure errorCode: %d", errorCode);

            restoreAdapterName();
            stopAdvertising();
            onFail();
        }
    };

    public boolean startAdvertising(String deviceName) {

        synchronized (mLockObject) {

            if (mStatus == Status.Advertise) {
                Utils.error("already advertising");
                return false;
            }

            if (mStatus != Status.Ready) {
                Utils.error("invalid status: %s", mStatus.toString());
                return false;
            }

            if (deviceName == null) {
                Utils.error("deviceName is null");
                return false;
            }

            mStatus = Status.Advertise;
            mDeviceName = deviceName;

            TimerTask timerTask = new TimerTask() {

                @Override
                public void run() {

                    synchronized (mLockObject) {

                        if (mStatus != Status.Advertise) {
                            Utils.error("invalid status: %s", mStatus.toString());
                            return;
                        }

                        if (Utils.isBluetoothEnabled()) {

                            if (mAdvertiser == null) {
                                startAdvertisingInternal();
                            }
                        }
                        else if (mAdvertiser != null) {
                            stopAdvertisingInternal();
                        }
                    }
                }
            };

            mAdvertiseTimer = new Timer();
            mAdvertiseTimer.schedule(timerTask, 0, UPDATE_INTERVAL);

            if (!Utils.isBluetoothEnabled()) {

                Utils.info("bluetooth required..");
                mPeripheralCallback.onBluetoothRequire();
            }
        }

        return true;
    }

    private boolean startAdvertisingInternal(){

        if (mAdvertiser != null) {
            Utils.error("advertiser is not null");
            return false;
        }

        BluetoothAdapter adapter = BluetoothAdapter.getDefaultAdapter();
        if (adapter == null) {
            Utils.error("bluetooth is not available on this device");
            return false;
        }

        // アダプタ名を保存しておいて一時的に名前を変える
        // ほんとうはscanResponseにaddManufacturerDataして名前を流し込みたいところだけど、それをやるとcentralからconnectできなくなる（beaconとみなされちゃう？）
        mOriginalAdapterName = adapter.getName();
        Utils.info("original adapter name: %s", mOriginalAdapterName);

        if (!adapter.setName(mDeviceName)) {
            Utils.error("failed to change adapter name: %s", mDeviceName);
            return false;
        }

        mAdvertiser = adapter.getBluetoothLeAdvertiser();
        if (mAdvertiser == null) {
            Utils.error("failed to get bluetoothLe advertiser");
            return false;
        }

        Utils.info("startAdvertising");
        AdvertiseSettings settings = new AdvertiseSettings.Builder().
                setAdvertiseMode(AdvertiseSettings.ADVERTISE_MODE_BALANCED).
                setTxPowerLevel(AdvertiseSettings.ADVERTISE_TX_POWER_MEDIUM).
                setConnectable(true).build();
        ParcelUuid uuid = new ParcelUuid(mServiceUUID);
        AdvertiseData data = new AdvertiseData.Builder().addServiceUuid(uuid).build();
        AdvertiseData scanResponse = new AdvertiseData.Builder().setIncludeDeviceName(true).build();
        mAdvertiser.startAdvertising(settings, data, scanResponse, mAdvertiseCallback);

        return true;
    }

    public void stopAdvertising() {

        synchronized (mLockObject) {

            if (mStatus != Status.Advertise) {
                Utils.error("invalid status: %s", mStatus.toString());
                return;
            }

            mStatus = Status.Ready;

            if (mAdvertiser != null) {
                stopAdvertisingInternal();
            }

            mAdvertiseTimer.cancel();
            mAdvertiseTimer = null;
        }
    }

    private void stopAdvertisingInternal() {

        if (mAdvertiser == null) {
            Utils.error("advertiser is null");
            return;
        }

        Utils.info("stopAdvertising");
        mAdvertiser.stopAdvertising(mAdvertiseCallback);
        mAdvertiser = null;
    }

    private void restoreAdapterName() {

        if (mOriginalAdapterName == null) {
            return;
        }

        BluetoothAdapter adapter = BluetoothAdapter.getDefaultAdapter();
        if (adapter != null) {

            if (adapter.setName(mOriginalAdapterName)) {
                Utils.info("adapter name restored: %s", mOriginalAdapterName);
            }
            else {
                Utils.error("failed to restore adapter name");
            }
        }
        else {
            Utils.error("failed to get adapter");
        }
    }

    public boolean accept(int connectionId, int playerId) {

        synchronized (mLockObject) {

            for (CentralContext context : mConnectedCentrals) {

                if (context.connectionId == connectionId) {

                    if (context.playerId != 0) {
                        Utils.info("already accepted");
                    }

                    context.playerId = playerId;

                    if (context.acceptanceTimer != null) {
                        context.acceptanceTimer.cancel();
                        context.acceptanceTimer = null;
                    }

                    return true;
                }
            }
        }

        Utils.error("invalid connectionId: %d", connectionId);
        return false;
    }

    public void invalidate(int connectionId) {

        synchronized (mLockObject) {

            for (CentralContext context : mConnectedCentrals) {

                if (context.connectionId == connectionId) {

                    unsubscribed(context);
                    return;
                }
            }
        }

        Utils.error("invalid connectionId: %d", connectionId);
    }

    // Communication

    private int mNotifyingConnectionId = 0;
    private LinkedList<Integer> mNotificationQueue = new LinkedList<>();

    public boolean sendDirect(byte[] message, int messageSize, int connectionId) {

        synchronized (mLockObject) {

            if ((mStatus != Status.Ready) && (mStatus != Status.Advertise)) {
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

            for (CentralContext context : mConnectedCentrals) {

                if ((context.connectionId != 0) && (context.connectionId == connectionId)) {

                    return sendInternal(context, message, messageSize, 0);
                }
            }
        }

        Utils.error("invalid connectionId: %d", connectionId);
        return false;
    }

    public boolean send(byte[] message, int messageSize, int receiver) {

        synchronized (mLockObject) {

            if ((mStatus != Status.Ready) && (mStatus != Status.Advertise)) {
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

            for (CentralContext context : mConnectedCentrals) {

                if ((context.playerId & receiver) != 0) {

                    sendInternal(context, message, messageSize, 1);
                }
            }
        }

        return true;
    }

    private boolean sendInternal(CentralContext context, byte[] message, int messageSize, int address) {

        try
        {
            context.sendBuffer.putChar((char)messageSize);
            context.sendBuffer.putChar((char)(address & 0xffff));
            context.sendBuffer.put(message, 0, messageSize);
        }
        catch (Exception e)
        {
            Utils.error(e.toString());
            unsubscribed(context);
            return false;
        }

        if (!context.valueWriting) {

            context.valueWriting = true;

            if (mNotifyingConnectionId == 0) {

                sendNotification(context);
            }
            else {

                mNotificationQueue.add(context.connectionId);
            }
        }

        return true;
    }

    private byte[] processSendBuffer(CentralContext context) {

        context.sendBuffer.flip();

        int size = Math.min(context.sendBuffer.limit(), context.maximumWriteLength - 1);
        byte[] value = new byte[size + 1];

        context.sendBuffer.get(value, 0, size);
        context.sendBuffer.compact();

        if (context.sendBuffer.position() > 0) {

            value[size] = 1;
            context.valueWriting = true;
        }
        else {

            value[size] = 0;
            context.valueWriting = false;
        }

        return value;
    }

    private boolean sendNotification(CentralContext context) {

        byte[] value = processSendBuffer(context);

        mDownloadCharacteristic.setValue(value);

        Utils.info("notifyCharacteristicChanged: %d bytes remain %d bytes %s",
                value.length, context.sendBuffer.position(), context.device.getAddress());

        if (!mGattServer.notifyCharacteristicChanged(context.device, mDownloadCharacteristic, true)) {
            Utils.error("failed");
            unsubscribed(context);
            return false;
        }

        mNotifyingConnectionId = context.connectionId;

        return true;
    }

    private void processNotificationQueue() {

        while (!mNotificationQueue.isEmpty()) {

            int connectionId = mNotificationQueue.remove();

            CentralContext context = null;
            for (CentralContext ctx : mConnectedCentrals) {

                if (ctx.connectionId == connectionId) {

                    context = ctx;
                    break;
                }
            }

            if (context != null) {

                if (!context.valueWriting) {
                    continue;
                }

                if (sendNotification(context)) {
                    return;
                }
            }
            else {

                Utils.error("context not found: %d", connectionId);
            }
        }
    }

    private void processReceiveBuffer(CentralContext context, byte[] value) {

        Utils.info("received: %d bytes remain %d bytes %s",
                value.length, context.receiveBuffer.position(), context.device.getAddress());
        try
        {
            context.receiveBuffer.put(value);
        }
        catch (Exception e)
        {
            Utils.error(e.toString());
            unsubscribed(context);
            return;
        }

        while (true) {

            if (context.receiveMessageSize == -1) {

                if (context.receiveBuffer.position() < 2) {
                    break;
                }

                context.receiveBuffer.flip();
                context.receiveMessageSize = context.receiveBuffer.getChar();
                context.receiveBuffer.compact();

                if (context.receiveMessageSize > MESSAGE_SIZE_MAX) {
                    Utils.error("invalid message size: %d", context.receiveMessageSize);
                    unsubscribed(context);
                    return;
                }
            }

            if (context.receiveMessageAddress == -1) {

                if (context.receiveBuffer.position() < 2) {
                    break;
                }

                context.receiveBuffer.flip();
                context.receiveMessageAddress = context.receiveBuffer.getChar();
                context.receiveBuffer.compact();
            }

            if (context.receiveBuffer.position() < context.receiveMessageSize) {
                break;
            }

            final byte[] message = new byte[context.receiveMessageSize];
            if (context.receiveMessageSize > 0) {

                context.receiveBuffer.flip();
                context.receiveBuffer.get(message);
                context.receiveBuffer.compact();
            }

            int to = context.receiveMessageAddress;
            context.receiveMessageSize = -1;
            context.receiveMessageAddress = -1;

            if (context.playerId != 0) {

                for (CentralContext ctx : mConnectedCentrals) {

                    if ((ctx.playerId & to) != 0) {

                        sendInternal(ctx, message, message.length, context.playerId);
                    }
                }

                if ((to & 1) != 0) {

                    mPeripheralCallback.onReceive(new Buffer(message), context.playerId);
                }
            }

            if (to == 0) {

                mPeripheralCallback.onReceiveDirect(new Buffer(message), context.connectionId);
            }
        }
    }

    // Cleanup

    public void cleanup() {

        synchronized (mLockObject) {

            if (mStatus == Status.Advertise) {
                stopAdvertising();
            }

            mStatus = Status.Invalid;

            if (mGattServer != null) {
                mGattServer.clearServices();
                mGattServer.close();
                mGattServer = null;
            }

            if (mInitializationTimer != null) {
                mInitializationTimer.cancel();
                mInitializationTimer = null;
            }

            mNotificationDescriptor = null;
            mDownloadCharacteristic = null;
            mUploadCharacteristic = null;
            mCommunicationService = null;

            for (CentralContext ctx : mConnectedCentrals) {

                if (ctx.acceptanceTimer != null) {
                    ctx.acceptanceTimer.cancel();
                    ctx.acceptanceTimer = null;
                }
            }

            mConnectedCentrals.clear();

            mNotifyingConnectionId = 0;
            mNotificationQueue.clear();

            mServiceUUID = null;
            mUploadUUID = null;
            mDownloadUUID = null;
            mPeripheralCallback = null;
        }
    }
}
