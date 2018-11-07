#import <CoreBluetooth/CoreBluetooth.h>
#import "Unity-Swift.h"

#ifdef __cplusplus
extern "C" {
#endif

    typedef void (* _Nonnull CommonCallback)(void const * _Nonnull);
    typedef void (* _Nonnull DiscoverCallback)(void const * _Nonnull, int8_t const * _Nonnull, int32_t);
    typedef void (* _Nonnull ReceiveCallback)(void const * _Nonnull, int8_t const * _Nonnull, int32_t, int32_t);
    
    CentralImpl* _blesock_central_create() {
        
        CentralImpl* instance = [CentralImpl new];
        CFRetain((CFTypeRef)instance);
        
        return instance;
    }
    
    BOOL _blesock_central_is_bluetooth_enabled(CentralImpl* instance) {
        
        return [instance isBluetoothEnabled];
    }

    BOOL _blesock_central_initialize(CentralImpl* instance,
                                     void* owner,
                                     const char* serviceUUID,
                                     const char* uploadUUID,
                                     const char* downloadUUID,
                                     CommonCallback onBluetoothRequire,
                                     CommonCallback onReady,
                                     CommonCallback onFail,
                                     DiscoverCallback onDiscover,
                                     CommonCallback onConnect,
                                     CommonCallback onDisconnect,
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
                         onDiscover: onDiscover
                          onConnect: onConnect
                       onDisconnect: onDisconnect
                          onReceive: onReceive];
    }

    BOOL _blesock_central_start_scan(CentralImpl* instance) {
        
        return [instance startScan];
    }

    void _blesock_central_stop_scan(CentralImpl* instance) {
        
        [instance stopScan];
    }

    BOOL _blesock_central_connect(CentralImpl* instance, int32_t deviceId) {
        
        return [instance connect: deviceId];
    }

    void _blesock_central_accept(CentralImpl* instance) {
        
        [instance accept];
    }

    void _blesock_central_disconnect(CentralImpl* instance) {
        
        [instance disconnect];
    }

    BOOL _blesock_central_send(CentralImpl* instance,
                               Byte* message,
                               int32_t messageSize,
                               int32_t receiver) {

        NSData *buffer = [NSData dataWithBytesNoCopy: message
                                              length: messageSize
                                        freeWhenDone: NO];
        
        return [instance send: buffer receiver: receiver];
    }

    void _blesock_central_cleanup(CentralImpl* instance) {
        
        [instance cleanup];
    }
    
    void _blesock_central_release(CentralImpl* instance) {
        
        CFRelease((CFTypeRef)instance);
    }
    
#ifdef __cplusplus
}
#endif
