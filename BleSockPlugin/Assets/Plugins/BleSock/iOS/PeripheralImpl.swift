import CoreBluetooth

public class PeripheralImpl: NSObject, CBPeripheralManagerDelegate {
    
    public typealias CommonCallback = @convention(c) (UnsafeRawPointer) -> Void
    public typealias ConnectionCallback = @convention(c) (UnsafeRawPointer, Int32) -> Void
    public typealias ReceiveCallback = @convention(c) (UnsafeRawPointer, UnsafePointer<Int8>, Int32, Int32) -> Void

    private enum Status: Int {
        
        case invalid    = 0
        case initialize = 1
        case ready      = 2
        case advertise  = 3
    }
    
    
    private let DISPATCH_QUEUE = DispatchQueue(label: "BleSock.DispatchQueue")
    private let MESSAGE_SIZE_MAX = 4096
    private let BUFFER_SIZE = 8192
    private let ACCEPTANCE_TIMEOUT = 19.0
    
    
    private var mOwner: UnsafeRawPointer!
    private var mServiceUUID: CBUUID!
    private var mUploadUUID: CBUUID!
    private var mDownloadUUID: CBUUID!
    
    private var mOnBluetoothRequire: CommonCallback!
    private var mOnReady: CommonCallback!
    private var mOnFail: CommonCallback!
    private var mOnConnect: ConnectionCallback!
    private var mOnDisconnect: ConnectionCallback!
    private var mOnReceiveDirect: ReceiveCallback!
    private var mOnReceive: ReceiveCallback!

    private var mStatus: Status = .invalid
    private var mManager: CBPeripheralManager!
    private var mUploadCharacteristic: CBMutableCharacteristic!
    private var mDownloadCharacteristic: CBMutableCharacteristic!
    private var mBluetoothEnabled = false

    private enum WriteState {
        
        case none
        case notifyPending
        case writing
    }
    
    private class CentralContext {
        
        let central: CBCentral
        var connectionId: Int32 = 0
        var acceptanceTimer: DispatchSourceTimer!
        var receiveBuffer = Data()
        var receiveMessageSize = -1
        var receiveMessageAddress: Int32 = -1
        var sendBuffer = Data()
        var writeState = WriteState.none
        var playerId: Int32 = 0
        
        init(central: CBCentral) {
            
            self.central = central
        }
    }
    
    private var mSubscribedCentrals: [CentralContext] = []
    private var mNextConnectionId: Int32 = 1
    
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
                                 onConnect: @escaping ConnectionCallback,
                                 onDisconnect: @escaping ConnectionCallback,
                                 onReceiveDirect: @escaping ReceiveCallback,
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
            mOnConnect = onConnect
            mOnDisconnect = onDisconnect
            mOnReceiveDirect = onReceiveDirect
            mOnReceive = onReceive

            mStatus = .initialize
            
            print("initialize")
            let options = [CBPeripheralManagerOptionShowPowerAlertKey: 1]
            mManager = CBPeripheralManager(delegate: self, queue: DISPATCH_QUEUE, options: options)
            
            return true
        })
    }
    
    // Advertising
    
    @objc public func startAdvertising(_ deviceName: String) -> Bool {
        
        return DISPATCH_QUEUE.sync(execute: {
            
            if mStatus != .ready {
                print("invalid status: \(mStatus)")
                return false
            }
            
            print("startAdvertising")
            let advertisementData = [CBAdvertisementDataServiceUUIDsKey: [mServiceUUID],
                                     CBAdvertisementDataLocalNameKey: deviceName] as [String : Any]
            mManager.startAdvertising(advertisementData)
            
            if !mBluetoothEnabled {
                print("bluetooth required..")
                mOnBluetoothRequire(mOwner)
            }
            
            return true
        })
    }
    
    @objc public func stopAdvertising() {
        
        DISPATCH_QUEUE.sync {
            
            if mStatus != .advertise {
                print("invalid status: \(mStatus)")
                return
            }
            
            mStatus = .ready
            mManager.stopAdvertising()
        }
    }
    
    @objc public func accept(_ connectionId: Int32, playerId: Int32) -> Bool {
        
        return DISPATCH_QUEUE.sync(execute: {
            
            let ctx = mSubscribedCentrals.filter({$0.connectionId == connectionId}).first
            if ctx == nil {
                print("invalid connectionId: \(connectionId)")
                return false
            }
            
            let context = ctx!
            if context.playerId != 0 {
                print("already accepted")
            }
            
            context.playerId = playerId
            
            if context.acceptanceTimer != nil {
                context.acceptanceTimer.cancel()
                context.acceptanceTimer = nil
            }

            mManager.setDesiredConnectionLatency(.low, for: context.central)
            
            return true
        })
    }

    @objc public func invalidate(_ connectionId: Int32) {
        
        DISPATCH_QUEUE.sync {

            let ctx = mSubscribedCentrals.filter({$0.connectionId == connectionId}).first
            if ctx == nil {
                
                print("invalid connectionId: \(connectionId)")
                return
            }

            removeCentral(ctx!)
        }
    }
    
    // Communication
    
    @objc public func sendDirect(_ message: Data, connectionId: Int32) -> Bool {
        
        return DISPATCH_QUEUE.sync(execute: {
            
            if (mStatus != .ready) && (mStatus != .advertise) {
                print("invalid status: \(mStatus)")
                return false
            }
            
            if message.count > MESSAGE_SIZE_MAX {
                print("message size too large")
                return false
            }
            
            for context in mSubscribedCentrals {
                
                if (context.connectionId != 0) && (context.connectionId == connectionId) {
                    
                    if !sendInternal(context: context, message: message, from: 0) {
                        
                        removeCentral(context)
                        return false
                    }

                    return true
                }
            }
            
            print("invalid connectionId: \(connectionId)")
            return false
        })
    }

    @objc public func send(_ message: Data, receiver: Int32) -> Bool {
        
        return DISPATCH_QUEUE.sync(execute: {
            
            if (mStatus != .ready) && (mStatus != .advertise) {
                print("invalid status: \(mStatus)")
                return false
            }

            if message.count > MESSAGE_SIZE_MAX {
                print("message size too large")
                return false
            }

            var index = 0
            while index < mSubscribedCentrals.count {
                
                let context = mSubscribedCentrals[index]
                if (context.playerId & receiver) != 0 {
                    
                    if !sendInternal(context: context, message: message, from: 1) {
                        
                        removeCentral(context)
                    }
                    else {
                        
                        index += 1
                    }
                }
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
            mOnConnect = nil
            mOnDisconnect = nil
            mOnReceiveDirect = nil
            mOnReceive = nil
            
            mStatus = .invalid
            mBluetoothEnabled = false
            
            for context in mSubscribedCentrals {
                
                if context.acceptanceTimer != nil {
                    context.acceptanceTimer.cancel()
                    context.acceptanceTimer = nil
                }
            }
            
            mSubscribedCentrals.removeAll()
            
            mUploadCharacteristic = nil
            mDownloadCharacteristic = nil
            mManager = nil
        }
    }

    // Internal

    private func addService() {
        
        mUploadCharacteristic = CBMutableCharacteristic.init(
            type: mUploadUUID, properties: [.write], value: nil, permissions: [.writeable])
        
        mDownloadCharacteristic = CBMutableCharacteristic.init(
            type: mDownloadUUID, properties: [.read, .indicate], value: nil, permissions: [.readable])

        let service = CBMutableService(type: mServiceUUID, primary: true)
        service.characteristics = [mUploadCharacteristic, mDownloadCharacteristic]
        
        mManager.add(service)
    }

    private func removeCentral(_ context: CentralContext) {
        
        let index = mSubscribedCentrals.index(where: {$0 === context})
        if index == nil {
            print("not subscribed")
            return
        }
        
        if context.acceptanceTimer != nil {
            context.acceptanceTimer.cancel()
            context.acceptanceTimer = nil
        }
        
        mSubscribedCentrals.remove(at: index!)
        
        mOnDisconnect(mOwner, context.connectionId)
    }
    
    private func sendInternal(context: CentralContext, message: Data, from: Int32) -> Bool {
        
        var size = UInt16(message.count)
        let sizeData = Data(bytes: &size, count: MemoryLayout.size(ofValue: size)) // リトルエンディアン
        context.sendBuffer.append(sizeData)
        
        var address = UInt16(from & 0xffff)
        let addressData = Data(bytes: &address, count: MemoryLayout.size(ofValue: size)) // リトルエンディアン
        context.sendBuffer.append(addressData)
        
        context.sendBuffer.append(message)
        
        if context.sendBuffer.count > BUFFER_SIZE {
            
            print("send buffer over flow")
            return false
        }

        if context.writeState == .none {
            
            // send notification
            
            let size = min(context.sendBuffer.count, context.central.maximumUpdateValueLength - 1)
            var value = context.sendBuffer.subdata(in: 0..<size)
            value.append(1)
            
            if mManager.updateValue(value, for: mDownloadCharacteristic, onSubscribedCentrals: [context.central]) {
                
                context.sendBuffer.removeSubrange(0..<size)
                context.writeState = .writing

                print("updateValue: \(value.count) bytes remain \(context.sendBuffer.count) bytes \(context.central.identifier)")
            }
            else {
                
                print("pending updateValue.. \(context.central.identifier)")
                context.writeState = .notifyPending
            }
        }
        
        return true
    }

    private func processReceiveBuffer(_ context: CentralContext, value: Data) {

        print("received: \(value.count) bytes remain \(context.receiveBuffer.count) bytes \(context.central.identifier)")
        
        context.receiveBuffer.append(value)
        
        if context.receiveBuffer.count > BUFFER_SIZE {
            
            print("receive buffer over flow")
            removeCentral(context)
            return
        }

        while true {
            
            if context.receiveMessageSize == -1 {
                
                if context.receiveBuffer.count < MemoryLayout<UInt16>.size {
                    break
                }
                
                let sizeData = context.receiveBuffer.subdata(in: 0..<MemoryLayout<UInt16>.size)
                context.receiveBuffer.removeSubrange(0..<MemoryLayout<UInt16>.size)
                
                let size: UInt16 = sizeData.withUnsafeBytes{$0.pointee}
                context.receiveMessageSize = Int(size)
                
                if context.receiveMessageSize > MESSAGE_SIZE_MAX {
                    
                    print("invalid message size")
                    removeCentral(context)
                    return
                }
            }
            
            if context.receiveMessageAddress == -1 {
                
                if context.receiveBuffer.count < MemoryLayout<UInt16>.size {
                    break
                }
                
                let addressData = context.receiveBuffer.subdata(in: 0..<MemoryLayout<UInt16>.size)
                context.receiveBuffer.removeSubrange(0..<MemoryLayout<UInt16>.size)
                
                let address: UInt16 = addressData.withUnsafeBytes{$0.pointee}
                context.receiveMessageAddress = Int32(address)
            }
            
            if context.receiveBuffer.count < context.receiveMessageSize {
                break
            }
            
            let message = context.receiveBuffer.subdata(in: 0..<context.receiveMessageSize)
            context.receiveBuffer.removeSubrange(0..<context.receiveMessageSize)

            let to = context.receiveMessageAddress
            context.receiveMessageSize = -1
            context.receiveMessageAddress = -1
            
            if context.playerId != 0 {
                
                var index = 0
                while index < mSubscribedCentrals.count {
                    
                    let ctx = mSubscribedCentrals[index]
                    if (ctx.playerId & to) != 0 {
                        
                        if !sendInternal(context: ctx, message: message, from: context.playerId) {
                            
                            removeCentral(ctx)
                        }
                        else {
                            
                            index += 1
                        }
                    }
                }

                if (to & 1) != 0 {
                    
                    message.withUnsafeBytes { (pointer: UnsafePointer<Int8>) in
                        
                        mOnReceive(mOwner, pointer, Int32(message.count), context.playerId)
                    }
                }
            }
            
            if to == 0 {
                
                message.withUnsafeBytes { (pointer: UnsafePointer<Int8>) in
                    
                    mOnReceiveDirect(mOwner, pointer, Int32(message.count), context.connectionId)
                }
            }
        }
    }
    
    private func handleError() {
        
        mOnFail(mOwner)
    }

    // CBPeripheralManagerDelegate
    
    public func peripheralManagerDidUpdateState(_ peripheral: CBPeripheralManager) {
        
        switch peripheral.state {
            
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
                print("bluetooth required..")
                mOnBluetoothRequire(mOwner)
            }
            break
            
        case .poweredOn:
            print("didUpdateState: poweredOn")
            mBluetoothEnabled = true

            if mStatus == .initialize {
                addService()
            }
            break
            
        default:
            print("didUpdateState: unknown")
        }
    }
    
    /*
    public func peripheralManager(_ peripheral: CBPeripheralManager,
                                  willRestoreState dict: [String : Any]) {

        print("willRestoreState: \(dict)")
    }
    */
 
    public func peripheralManager(_ peripheral: CBPeripheralManager,
                                  didAdd service: CBService,
                                  error: Error?) {

        print("didAddService: \(service)")
        
        if mStatus != .initialize {
            print("invalid status: \(mStatus)")
            return
        }

        if error != nil {
            print("error: \(error!)")
            handleError()
            return
        }
        
        print("ready")
        mStatus = .ready
        
        mOnReady(mOwner)
    }
    
    
    public func peripheralManagerDidStartAdvertising(_ peripheral: CBPeripheralManager,
                                                     error: Error?) {

        print("didStartAdvertising")
        
        if mStatus != .ready {
            print("invalid status: \(mStatus)")
            return
        }

        if error != nil {
            print("error: \(error!)")
            stopAdvertising()
            handleError()
            return
        }

        print("advertising..")
        mStatus = .advertise
    }

    
    public func peripheralManager(_ peripheral: CBPeripheralManager,
                                  central: CBCentral,
                                  didSubscribeTo characteristic: CBCharacteristic) {

        print("didSubscribe central: \(central) characteristic: \(characteristic)")
        
        if mStatus != .advertise {
            print("invalid status: \(mStatus)")
            return
        }
        
        if characteristic != mDownloadCharacteristic {
            print("invalid characteristic")
            return
        }
        
        if !mSubscribedCentrals.filter({$0.central == central}).isEmpty {
            print("already subscribed")
            return
        }
        
        let context = CentralContext(central: central)
        context.acceptanceTimer = DispatchSource.makeTimerSource(flags: DispatchSource.TimerFlags(), queue: DISPATCH_QUEUE)
        context.acceptanceTimer.schedule(deadline: DispatchTime.now() + ACCEPTANCE_TIMEOUT)
        context.acceptanceTimer.setEventHandler(handler: {
            
            print("connection timeout")
            self.removeCentral(context)
        })
        context.acceptanceTimer.resume()
        
        mSubscribedCentrals.append(context)
        
        print("subscribed address: \(central.identifier) connectionId: \(context.connectionId)")
    }
    
    public func peripheralManager(_ peripheral: CBPeripheralManager,
                                  central: CBCentral,
                                  didUnsubscribeFrom characteristic: CBCharacteristic) {

        print("didUnsubscribe central: \(central) characteristic: \(characteristic)")

        if characteristic != mDownloadCharacteristic {
            print("invalid characteristic")
        }

        let ctx = mSubscribedCentrals.filter({$0.central == central}).first
        if ctx != nil {
            
            removeCentral(ctx!)
        }
    }
    
    public func peripheralManagerIsReady(toUpdateSubscribers peripheral: CBPeripheralManager) {
        
        print("readyToUpdateSubscribers") // Notifyできる状態となった
        
        for context in mSubscribedCentrals {
            
            if context.writeState == .notifyPending {
                
                // send notification
                
                let size = min(context.sendBuffer.count, context.central.maximumUpdateValueLength - 1)
                var value = context.sendBuffer.subdata(in: 0..<size)
                value.append((context.sendBuffer.count == size) ? 0 : 1)
                
                if mManager.updateValue(value, for: mDownloadCharacteristic, onSubscribedCentrals: [context.central]) {
                    
                    context.sendBuffer.removeSubrange(0..<size)
                    context.writeState = context.sendBuffer.isEmpty ? .none : .writing
                    print("updateValue: \(value.count) bytes remain \(context.sendBuffer.count) bytes \(context.central.identifier)")
                }
                else { // Pend again..
                    
                    break
                }
            }
        }
    }
    
    
    public func peripheralManager(_ peripheral: CBPeripheralManager,
                                  didReceiveRead request: CBATTRequest) {

        print("didReceiveRead") // print("didReceiveRead request: \(request)")

        if (mStatus != .ready) && (mStatus != .advertise) {
            print("invalid status: \(mStatus)")
            mManager.respond(to: request, withResult: .requestNotSupported)
            return
        }

        let ctx = mSubscribedCentrals.filter({$0.central == request.central}).first
        if ctx == nil {
            print("not subscribed")
            mManager.respond(to: request, withResult: .insufficientAuthentication)
            return
        }
        
        let context = ctx!
        if request.characteristic != mDownloadCharacteristic {
            print("invalid characteristic")
            mManager.respond(to: request, withResult: .attributeNotFound)
            removeCentral(context)
            return
        }
        
        if request.offset != 0 {
            print("invalid offset")
            mManager.respond(to: request, withResult: .invalidPdu)
            removeCentral(context)
            return
        }

        if !context.sendBuffer.isEmpty {
            
            let size = min(context.sendBuffer.count, context.central.maximumUpdateValueLength - 1)
            var value = context.sendBuffer.subdata(in: 0..<size)
            
            if context.sendBuffer.isEmpty {
                
                value.append(0)
                context.writeState = .none
            }
            else {
                
                value.append(1)
                context.writeState = .writing
            }
            
            request.value = value
            context.sendBuffer.removeSubrange(0..<size)
            print("respond: \(value.count) bytes remain \(context.sendBuffer.count) bytes \(request.central.identifier)")
        }
        else {

            context.writeState = .none
            
            request.value = nil
            print("respond: nil \(request.central.identifier)")
        }

        mManager.respond(to: request, withResult: .success)
    }
    
    public func peripheralManager(_ peripheral: CBPeripheralManager,
                                  didReceiveWrite requests: [CBATTRequest]) {

        print("didReceiveWrite")

        for request in requests {
            
            // print("request: \(request)")

            if (mStatus != .ready) && (mStatus != .advertise) {
                print("invalid status: \(mStatus)")
                mManager.respond(to: request, withResult: .requestNotSupported)
                continue
            }
            
            let ctx = mSubscribedCentrals.filter({$0.central == request.central}).first
            if ctx == nil {
                print("not subscribed")
                mManager.respond(to: request, withResult: .insufficientAuthentication)
                continue
            }

            let context = ctx!            
            if request.characteristic != mUploadCharacteristic {
                print("invalid characteristic")
                mManager.respond(to: request, withResult: .attributeNotFound)
                removeCentral(context)
                continue
            }
            
            if request.offset != 0 {
                print("invalid offset")
                mManager.respond(to: request, withResult: .invalidPdu)
                removeCentral(context)
                continue
            }
            
            mManager.respond(to: request, withResult: .success)

            if context.connectionId == 0 {
                
                context.connectionId = mNextConnectionId
                mNextConnectionId = mNextConnectionId &+ 1
                
                mOnConnect(mOwner, context.connectionId)
            }
            else if request.value != nil {
                
                processReceiveBuffer(context, value: request.value!)
            }
        }
    }
}
