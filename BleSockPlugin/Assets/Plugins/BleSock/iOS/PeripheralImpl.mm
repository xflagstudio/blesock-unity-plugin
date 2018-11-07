#import <CoreBluetooth/CoreBluetooth.h>
#import "Unity-Swift.h"

#ifdef __cplusplus
extern "C" {
#endif
    
    typedef void (* _Nonnull CommonCallback)(void const * _Nonnull);
    typedef void (* _Nonnull ConnectionCallback)(void const * _Nonnull, int32_t);
    typedef void (* _Nonnull ReceiveCallback)(void const * _Nonnull, int8_t const * _Nonnull, int32_t, int32_t);
    
    PeripheralImpl* _blesock_peripheral_create() {
        
        PeripheralImpl* instance = [PeripheralImpl new];
        CFRetain((CFTypeRef)instance);
        
        return instance;
    }
    
    BOOL _blesock_peripheral_is_bluetooth_enabled(PeripheralImpl* instance) {
        
        return [instance isBluetoothEnabled];
    }
    
    BOOL _blesock_peripheral_initialize(PeripheralImpl* instance,
                                        void* owner,
                                        const char* serviceUUID,
                                        const char* uploadUUID,
                                        const char* downloadUUID,
                                        CommonCallback onBluetoothRequire,
                                        CommonCallback onReady,
                                        CommonCallback onFail,
                                        ConnectionCallback onConnect,
                                        ConnectionCallback onDisconnect,
                                        ReceiveCallback onReceiveDirect,
                                        ReceiveCallback onReceive) {

        NSString* serviceUUIDStr = [NSString stringWithUTF8String: serviceUUID];
        NSString* uploadUUIDStr = [NSString stringWithUTF8String: uploadUUID];
        NSString* downloadUUIDStr = [NSString stringWithUTF8String: downloadUUID];

        return [instance initialize: owner
                        serviceUUID: serviceUUIDStr
                         uploadUUID: uploadUUIDStr
                       downloadUUID: downloadUUIDStr
                 onBluetoothRequire: onBluetoothRequire
                            onReady: onReady
                             onFail: onFail
                          onConnect: onConnect
                       onDisconnect: onDisconnect
                    onReceiveDirect: onReceiveDirect
                          onReceive: onReceive];
    }
    
    BOOL _blesock_peripheral_start_advertising(PeripheralImpl* instance, const char* deviceName) {
        
        NSString* deviceNameStr = [NSString stringWithUTF8String: deviceName];
        
        return [instance startAdvertising: deviceNameStr];
    }
    
    void _blesock_peripheral_stop_advertising(PeripheralImpl* instance) {
        
        [instance stopAdvertising];
    }
    
    BOOL _blesock_peripheral_accept(PeripheralImpl* instance, int32_t connectionId, int32_t playerId) {
        
        return [instance accept: connectionId playerId: playerId];
    }

    void _blesock_peripheral_invalidate(PeripheralImpl* instance, int32_t connectionId) {
        
        [instance invalidate: connectionId];
    }
    
    BOOL _blesock_peripheral_send_direct(PeripheralImpl* instance,
                                  Byte* message,
                                  int32_t messageSize,
                                  int32_t connectionId) {
        
        NSData *buffer = [NSData dataWithBytesNoCopy: message
                                              length: messageSize
                                        freeWhenDone: NO];
        
        return [instance sendDirect: buffer connectionId: connectionId];
    }

    BOOL _blesock_peripheral_send(PeripheralImpl* instance,
                                  Byte* message,
                                  int32_t messageSize,
                                  int32_t receiver) {
        
        NSData *buffer = [NSData dataWithBytesNoCopy: message
                                              length: messageSize
                                        freeWhenDone: NO];
        
        return [instance send: buffer receiver: receiver];
    }
    
    void _blesock_peripheral_cleanup(PeripheralImpl* instance) {
        
        [instance cleanup];
    }
    
    void _blesock_peripheral_release(PeripheralImpl* instance) {
        
        CFRelease((CFTypeRef)instance);
    }
    
#ifdef __cplusplus
}
#endif
