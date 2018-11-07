import CoreBluetooth

public class CentralImpl: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {
    
    public typealias CommonCallback = @convention(c) (UnsafeRawPointer) -> Void
    public typealias DiscoverCallback = @convention(c) (UnsafeRawPointer, UnsafePointer<Int8>, Int32) -> Void
    public typealias ReceiveCallback = @convention(c) (UnsafeRawPointer, UnsafePointer<Int8>, Int32, Int32) -> Void

    private enum Status: Int {
        
        case invalid    = 0
        case initialize = 1
        case ready      = 2
        case scan       = 3
        case connect    = 4
        case discover   = 5
        case online     = 6
        case disconnect = 7
    }
    
    
    private let DISPATCH_QUEUE = DispatchQueue(label: "BleSock.DispatchQueue")
    private let MESSAGE_SIZE_MAX = 4096
    private let BUFFER_SIZE = 8192
    private let ACCEPTANCE_TIMEOUT = 20.0
    
    
    private var mOwner: UnsafeRawPointer!
    private var mServiceUUID: CBUUID!
    private var mUploadUUID: CBUUID!
    private var mDownloadUUID: CBUUID!
    
    private var mOnBluetoothRequire: CommonCallback!
    private var mOnReady: CommonCallback!
    private var mOnFail: CommonCallback!
    private var mOnDiscover: DiscoverCallback!
    private var mOnConnect: CommonCallback!
    private var mOnDisconnect: CommonCallback!
    private var mOnReceive: ReceiveCallback!
    
    private var mStatus: Status = .invalid
    private var mManager: CBCentralManager!
    private var mBluetoothEnabled = false
    
    private class PeripheralContext {
        
        let peripheral: CBPeripheral
        let id: Int32
        let name: String
        
        init (peripheral: CBPeripheral, id: Int32, name: String) {
            self.peripheral = peripheral
            self.id = id
            self.name = name
        }
    }
    
    private var mDiscoveredPeripherals: [PeripheralContext] = []
    private var mNextPeripheralId: Int32 = 1
    
    private var mPeripheral: CBPeripheral!
    private var mAcceptanceTimer: DispatchSourceTimer!
    private var mCommunicationService: CBService!
    private var mUploadCharacteristic: CBCharacteristic!
    private var mDownloadCharacteristic: CBCharacteristic!
    
    private var mSendBuffer = Data()
    private var mWaitForSendResponse = false
    private var mReceiveBuffer = Data()
    private var mReceiveMessageSize = -1
    private var mReceiveMessageAddress: Int32 = -1

    // Properties
    
    @objc public var isBluetoothEnabled: Bool {
        
        get {
            return mBluetoothEnabled
        }
    }
    
    // Initialization

    @objc public func initialize(_ owner: UnsafeRawPointer,
                                 serviceUUID: String,
                                 uploadUUID: String,
                                 downloadUUID: String,
                                 onBluetoothRequire: @escaping CommonCallback,
                                 onReady: @escaping CommonCallback,
                                 onFail: @escaping CommonCallback,
                                 onDiscover: @escaping DiscoverCallback,
                                 onConnect: @escaping CommonCallback,
                                 onDisconnect: @escaping CommonCallback,
                                 onReceive: @escaping ReceiveCallback) -> Bool {

        return DISPATCH_QUEUE.sync(execute: {
            
            if mStatus != .invalid {
                print("invalid status: \(mStatus)")
                return false
            }
            
            mOwner = owner
            mServiceUUID = CBUUID(string: serviceUUID)
            mUploadUUID = CBUUID(string: uploadUUID)
            mDownloadUUID = CBUUID(string: downloadUUID)
            
            mOnBluetoothRequire = onBluetoothRequire
            mOnReady = onReady
            mOnFail = onFail
            mOnDiscover = onDiscover
            mOnConnect = onConnect
            mOnDisconnect = onDisconnect
            mOnReceive = onReceive

            mStatus = .initialize
            
            print("initialize")
            let options = [CBCentralManagerOptionShowPowerAlertKey: 1]
            mManager = CBCentralManager(delegate: self, queue: DISPATCH_QUEUE, options: options)
            
            return true
        })
    }
    
    // Scan peripherals
    
    @objc public func startScan() -> Bool {
        
        return DISPATCH_QUEUE.sync(execute: {
            
            if mStatus == .scan {
                print("already scanning")
                return false
            }
            
            if mStatus != .ready {
                print("invalid status: \(mStatus)")
                return false
            }
            
            mStatus = .scan
            mDiscoveredPeripherals.removeAll()
            
            if mBluetoothEnabled {
                startScanInternal()
            }
            else {
                print("bluetooth required..")
                mOnBluetoothRequire(mOwner)
            }
            
            return true
        })
    }
    
    @objc public func stopScan() {

        DISPATCH_QUEUE.sync {
            stopScanInternal()
        }
    }
    
    // Connection
    
    @objc public func connect(_ peripheralId: Int32) -> Bool {
        
        return DISPATCH_QUEUE.sync(execute: {
            
            if (mStatus != .ready) && (mStatus != .scan) {
                print("invalid status: \(mStatus)")
                return false
            }
            
            let context = mDiscoveredPeripherals.filter({$0.id == peripheralId}).first
            if context == nil {
                print("invalid peripheralId: \(peripheralId)")
                return false
            }
            
            if (mStatus == .scan) {
                stopScanInternal()
            }
            
            mStatus = .connect
            mPeripheral = context?.peripheral
            
            mAcceptanceTimer = DispatchSource.makeTimerSource(flags: DispatchSource.TimerFlags(), queue: DISPATCH_QUEUE)
            mAcceptanceTimer.schedule(deadline: DispatchTime.now() + ACCEPTANCE_TIMEOUT)
            mAcceptanceTimer.setEventHandler(handler: {
                
                print("connection timeout")
                self.handleError();
            })
            mAcceptanceTimer.resume()
            
            print("connect: \(mPeripheral)")
            mManager.connect(mPeripheral, options: nil)
            
            return true
        })
    }

    @objc public func accept() {
        
        DISPATCH_QUEUE.sync {
            
            if mStatus != .online {
                print("invalid status: \(mStatus)")
                return
            }
            
            if mAcceptanceTimer != nil {
                mAcceptanceTimer.cancel()
                mAcceptanceTimer = nil
            }
        }
    }
    
    @objc public func disconnect() {
        
        DISPATCH_QUEUE.sync {
            
            if (mStatus == .connect) || (mStatus == .discover) {
                
                mStatus = .ready
                
                cleanupConnection()
            }
            else if mStatus == .online {

                mStatus = .disconnect
                
                print("cancelPeripheralConnection");
                mManager.cancelPeripheralConnection(mPeripheral) // TODO
                
                if mAcceptanceTimer != nil {
                    mAcceptanceTimer.cancel()
                    mAcceptanceTimer = nil
                }
            }
            else {
                
                print("invalid status: \(mStatus)")
            }
        }
    }
    
    // Communication
    
    @objc public func send(_ message: Data, receiver: Int32) -> Bool {

        return DISPATCH_QUEUE.sync(execute: {
            
            if mStatus != .online {
                print("invalid status: \(mStatus)")
                return false
            }
            
            if message.count > MESSAGE_SIZE_MAX {
                print("message size too large")
                return false
            }
            
            var size = UInt16(message.count)
            let sizeData = Data(bytes: &size, count: MemoryLayout.size(ofValue: size)) // リトルエンディアン
            mSendBuffer.append(sizeData)
            
            var address = UInt16(receiver & 0xffff)
            let addressData = Data(bytes: &address, count: MemoryLayout.size(ofValue: size)) // リトルエンディアン
            mSendBuffer.append(addressData)
            
            mSendBuffer.append(message)

            if mSendBuffer.count > BUFFER_SIZE {
                print("send buffer over flow")
                handleError()
                return false
            }

            if !mWaitForSendResponse {
                processSendBuffer()
            }
            
            return true
        })
    }
    
    // Cleanup
    
    @objc public func cleanup() {

        DISPATCH_QUEUE.sync {
            
            mOwner = nil
            mServiceUUID = nil
            mUploadUUID = nil
            mDownloadUUID = nil
            
            mOnBluetoothRequire = nil
            mOnReady = nil
            mOnFail = nil
            mOnDiscover = nil
            mOnConnect = nil
            mOnDisconnect = nil
            mOnReceive = nil
            
            mStatus = .invalid
            mBluetoothEnabled = false
            mManager = nil
            
            mDiscoveredPeripherals.removeAll()
            
            cleanupConnection()
        }
    }

    // Internal
    
    private func getReady() {
        
        mStatus = .ready
        mOnReady(mOwner)
    }
    
    private func startScanInternal() {
        
        print("scanForPeripherals..")
        mManager.scanForPeripherals(withServices: [mServiceUUID], options: nil)
    }
    
    private func stopScanInternal() {
        
        if mStatus != .scan {
            print("invalid status: \(mStatus)")
            return
        }
        
        mStatus = .ready
        
        if mManager.isScanning {
            mManager.stopScan()
        }
    }

    private func handleError() {

        if mStatus == .connect {
            
            mStatus = .ready
            
            cleanupConnection()

            mOnFail(mOwner)
        }
        else if (mStatus == .discover) || (mStatus == .online) {
            
            print("cancelPeripheralConnection");
            mManager.cancelPeripheralConnection(mPeripheral) // TODO
            
            if mAcceptanceTimer != nil {
                mAcceptanceTimer.cancel()
                mAcceptanceTimer = nil
            }
        }
    }
    
    private func cleanupConnection() {
        
        if mPeripheral != nil {
            mPeripheral.delegate = nil
            mPeripheral = nil
        }
        
        if mAcceptanceTimer != nil {
            mAcceptanceTimer.cancel()
            mAcceptanceTimer = nil
        }

        mCommunicationService = nil
        mUploadCharacteristic = nil
        mDownloadCharacteristic = nil
        
        mSendBuffer.removeAll()
        mWaitForSendResponse = false
        mReceiveBuffer.removeAll()
        mReceiveMessageSize = -1
        mReceiveMessageAddress = -1
    }
    
    private func processSendBuffer() {
        
        // TODO
        let maximumWriteLength: Int
        if #available(iOS 10, *) {
            maximumWriteLength = 185 - 3
        }
        else {
            maximumWriteLength = 135 - 3
        }
        
        let size = min(mSendBuffer.count, maximumWriteLength)
        let value = mSendBuffer.subdata(in: 0..<size)
        mSendBuffer.removeSubrange(0..<size)

        print("writeValue: \(size) bytes remain \(mSendBuffer.count) bytes")
        mPeripheral.writeValue(value, for: mUploadCharacteristic, type: .withResponse)

        mWaitForSendResponse = true
    }
    
    private func disconnected() {
        
        if (mStatus != .discover) && (mStatus != .online) && (mStatus != .disconnect) {
            print("invalid status: \(mStatus)")
            return
        }
        
        let disconnected = (mStatus != .discover)

        mStatus = .ready
        
        cleanupConnection()
        
        if disconnected {
            
            mOnDisconnect(mOwner)
        }
        else {
            
            mOnFail(mOwner)
        }
    }

    // CBCentralManagerDelegate
    
    public func centralManager(_ central: CBCentralManager,
                               didConnect peripheral: CBPeripheral) {
        
        print("didConnect peripheral: \(peripheral)")
        
        if peripheral != mPeripheral {
            print("invalid peripheral")
            return
        }
        
        if mStatus != .connect {
            print("invalid status: \(mStatus)")
            handleError()
            return
        }
        
        mStatus = .discover
        
        print("discover services..")
        peripheral.delegate = self
        peripheral.discoverServices([mServiceUUID])
    }
    
    public func centralManager(_ central: CBCentralManager,
                               didDisconnectPeripheral peripheral: CBPeripheral,
                               error: Error?) {

        print("didDisconnectPeripheral peripheral: \(peripheral)")
        
        if peripheral != mPeripheral {
            print("invalid peripheral")
            return
        }

        if error != nil {
            print("error: \(error!)")
        }
        
        print("disconnected from peripheral")
        disconnected()
    }
    
    public func centralManager(_ central: CBCentralManager,
                               didFailToConnect peripheral: CBPeripheral,
                               error: Error?) {

        print("didFailToConnect peripheral: \(peripheral)")
        
        if peripheral != mPeripheral {
            print("invalid peripheral")
            return
        }

        if error != nil {
            print("error: \(error!)")
        }

        handleError()
    }

    
    public func centralManager(_ central: CBCentralManager,
                               didDiscover peripheral: CBPeripheral,
                               advertisementData: [String : Any],
                               rssi RSSI: NSNumber) {

        print("didDiscover peripheral: \(peripheral) advertisementData: \(advertisementData) rssi: \(RSSI)")
        
        if mStatus != .scan {
            print("invalid status: \(mStatus)")
            return
        }
        
        // 同じperipheralが報告されることがあるので自前で重複を除外
        if !mDiscoveredPeripherals.filter({$0.peripheral.identifier == peripheral.identifier}).isEmpty {
            print("already discovered")
            return
        }
        
        // peripheral.nameに反映されていないことがあるのでadvertisementDataから取り出す
        let deviceName = advertisementData[CBAdvertisementDataLocalNameKey] as? String
        if deviceName == nil {
            print("deviceName is null")
            return
        }
        
        let context = PeripheralContext(peripheral: peripheral, id: mNextPeripheralId, name: deviceName!)
        mDiscoveredPeripherals.append(context)
        
        mNextPeripheralId = mNextPeripheralId &+ 1
        
        mOnDiscover(mOwner, context.name.cString(using: .utf8)!, context.id)
    }

    
    public func centralManagerDidUpdateState(_ central: CBCentralManager) {
        
        switch central.state {
        case .resetting:
            print("didUpdateState: resetting")
            
            // TODO
            
            break
            
        case .unsupported:
            print("didUpdateState: unsupported")
            handleError()
            break
            
        case .unauthorized:
            print("didUpdateState: unauthorized")
            handleError()
            break
            
        case .poweredOff:
            print("didUpdateState: poweredOff")
            mBluetoothEnabled = false
            
            if mStatus == .initialize {
                getReady()
            }
            break
            
        case .poweredOn:
            print("didUpdateState: poweredOn")
            mBluetoothEnabled = true
            
            if mStatus == .initialize {
                getReady()
            }
            else if (mStatus == .scan) && !mManager.isScanning {
                startScanInternal()
            }
            break
            
        default:
            print("didUpdateState: unknown")
        }
    }
    
    /*
    public func centralManager(_ central: CBCentralManager, willRestoreState dict: [String : Any]) {
        
        print("willRestoreState dict: \(dict)")
    }
    */
    
    // CBPeripheralDelegate
    
    public func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        
        print("didDiscoverServices peripheral: \(peripheral)")

        if peripheral != mPeripheral {
            print("invalid peripheral")
            return
        }

        if mStatus != .discover {
            print("invalid status: \(mStatus)")
            handleError()
            return
        }

        if error != nil {
            print("error: \(error!)")
            handleError()
            return
        }

        if (peripheral.services == nil) || peripheral.services!.isEmpty {
            print("no services discovered")
            handleError()
            return
        }
        
        mCommunicationService = peripheral.services!.first(where: {$0.uuid == mServiceUUID})
        if mCommunicationService == nil {
            print("communication service not found")
            handleError()
            return
        }

        print("discover characteristics..")
        peripheral.discoverCharacteristics([mUploadUUID, mDownloadUUID], for: mCommunicationService)
    }
    
    public func peripheral(_ peripheral: CBPeripheral,
                           didDiscoverIncludedServicesFor service: CBService,
                           error: Error?) {

        print("didDiscoverIncludedServicesFor: \(service)")
        
        if error != nil {
            print("error: \(error!)")
        }
    }
    
    
    public func peripheral(_ peripheral: CBPeripheral,
                           didDiscoverCharacteristicsFor service: CBService,
                           error: Error?) {

        print("didDiscoverCharacteristics: \(service.characteristics!.count)")

        if peripheral != mPeripheral {
            print("invalid peripheral")
            return
        }

        if service != mCommunicationService {
            print("invalid service")
            return
        }
        
        if mStatus != .discover {
            print("invalid status: \(mStatus)")
            handleError()
            return
        }
        
        if error != nil {
            print("error: \(error!)")
            handleError()
            return
        }

        if (service.characteristics == nil) || service.characteristics!.isEmpty {
            print("not characteristics discovered")
            handleError()
            return
        }
        
        mUploadCharacteristic = service.characteristics!.first(where: {$0.uuid == mUploadUUID})
        if mUploadCharacteristic == nil {
            print("upload characteristic not found")
            handleError()
            return
        }
        
        mDownloadCharacteristic = service.characteristics!.first(where: {$0.uuid == mDownloadUUID})
        if mDownloadCharacteristic == nil {
            print("download characteristic not found")
            handleError()
            return
        }

        print("setNotifyValue: true")
        peripheral.setNotifyValue(true, for: mDownloadCharacteristic)
    }
    
    public func peripheral(_ peripheral: CBPeripheral,
                           didDiscoverDescriptorsFor characteristic: CBCharacteristic,
                           error: Error?) {

        print("didDiscoverDescriptorsFor: \(characteristic)")

        if error != nil {
            print("error: \(error!)")
        }
    }
    
    
    public func peripheral(_ peripheral: CBPeripheral,
                           didUpdateValueFor characteristic: CBCharacteristic,
                           error: Error?) {

        print("didUpdateValue") // print("didUpdateValueFor: \(characteristic)") // PeripheralからIndicationもしくはreadValueForに対するレスポンスが帰ってきた
        
        if peripheral != mPeripheral {
            print("invalid peripheral")
            return
        }

        if characteristic != mDownloadCharacteristic {
            print("invalid characteristic")
            return
        }

        if error != nil {
            print("error: \(error!)")
            handleError()
            return
        }
        
        if mStatus != .online {
            print("invalid status: \(mStatus)")
            handleError()
            return
        }

        if (characteristic.value == nil) || characteristic.value!.count <= 1 {
            print("received empty")
            return
        }
        
        var value = characteristic.value!
        print("received: \(value.count - 1) bytes remain \(mReceiveBuffer.count) bytes")

        let willContinue = value.popLast()!;
        mReceiveBuffer.append(value)

        if mReceiveBuffer.count > BUFFER_SIZE {
            print("receive buffer over flow")
            handleError()
            return
        }

        if willContinue != 0 {
            
            print("readValue")
            peripheral.readValue(for: characteristic)
        }
        
        while true {
            
            if mReceiveMessageSize == -1 {
                
                if mReceiveBuffer.count < MemoryLayout<UInt16>.size {
                    break
                }
                
                let sizeData = mReceiveBuffer.subdata(in: 0..<MemoryLayout<UInt16>.size)
                mReceiveBuffer.removeSubrange(0..<MemoryLayout<UInt16>.size)
                
                let size: UInt16 = sizeData.withUnsafeBytes{$0.pointee}
                mReceiveMessageSize = Int(size)
            }
            
            if mReceiveMessageAddress == -1 {
                
                if mReceiveBuffer.count < MemoryLayout<UInt16>.size {
                    break
                }
                
                let addressData = mReceiveBuffer.subdata(in: 0..<MemoryLayout<UInt16>.size)
                mReceiveBuffer.removeSubrange(0..<MemoryLayout<UInt16>.size)
                
                let address: UInt16 = addressData.withUnsafeBytes{$0.pointee}
                mReceiveMessageAddress = Int32(address)
            }
            
            if mReceiveBuffer.count < mReceiveMessageSize {
                break
            }

            let message = mReceiveBuffer.subdata(in: 0..<mReceiveMessageSize)
            mReceiveBuffer.removeSubrange(0..<mReceiveMessageSize)

            let from = mReceiveMessageAddress
            mReceiveMessageSize = -1
            mReceiveMessageAddress = -1
            
            message.withUnsafeBytes { (pointer: UnsafePointer<Int8>) in
                
                mOnReceive(mOwner, pointer, Int32(message.count), from)
            }
        }
    }
    
    
    public func peripheral(_ peripheral: CBPeripheral,
                           didWriteValueFor characteristic: CBCharacteristic,
                           error: Error?) {

        print("didWriteValue") // print("didWriteValueFor: \(characteristic)") // PeripheralからwriteValueのレスポンスが帰ってきた

        if peripheral != mPeripheral {
            print("invalid peripheral")
            return
        }

        if characteristic != mUploadCharacteristic {
            print("invalid characteristic")
            return
        }
        
        if error != nil {
            print("error: \(error!)")
            handleError()
            return
        }
        
        if mStatus != .online {
            print("invalid status: \(mStatus)")
            handleError()
            return
        }

        if !mWaitForSendResponse {
            print("not waiting for response")
            return
        }
        
        mWaitForSendResponse = false
        
        if mSendBuffer.count > 0 {
            processSendBuffer()
        }
    }
    
    
    public func peripheral(_ peripheral: CBPeripheral,
                           didUpdateNotificationStateFor characteristic: CBCharacteristic,
                           error: Error?) {

        print("didUpdateNotificationStateFor: \(characteristic)")
        
        if peripheral != mPeripheral {
            print("invalid peripheral")
            return
        }

        if characteristic != mDownloadCharacteristic {
            print("invalid characteristic")
            return
        }
        
        if error != nil {
            print("error: \(error!)")
            handleError()
            return
        }
        
        if characteristic.isNotifying {
            
            if mStatus != .discover {
                print("invalid status: \(mStatus)")
                handleError()
                return
            }

            print("characteristic subscribed")
            mStatus = .online
            
            print("writeValue: nil")
            mPeripheral.writeValue(Data(), for: mUploadCharacteristic, type: .withResponse)
            
            mWaitForSendResponse = true

            mOnConnect(mOwner)
        }
        else {
            
            print("characteristic unsubscribed")
            disconnected()
        }
    }
    
    
    public func peripheral(_ peripheral: CBPeripheral, didReadRSSI RSSI: NSNumber, error: Error?) {
        
        print("didReadRSSI RSSI: \(RSSI)")
        
        if error != nil {
            print("error: \(error!)")
        }
    }
    
    
    public func peripheralDidUpdateName(_ peripheral: CBPeripheral) {
        
        print("peripheralDidUpdateName: \(peripheral.name!)")
    }
    
    public func peripheral(_ peripheral: CBPeripheral, didModifyServices invalidatedServices: [CBService]) {
        
        print("didModifyServices invalidatedServices: \(invalidatedServices)")
        
        
        if !invalidatedServices.isEmpty && invalidatedServices.contains(mCommunicationService) {
            
            print("service invalidated")
            disconnected()
        }
    }
    
    
    public func peripheralIsReady(toSendWriteWithoutResponse peripheral: CBPeripheral) {
        
        print("isReadyToSendWriteWithoutResponse")
    }
}
