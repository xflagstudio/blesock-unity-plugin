using System.ServiceModel;

namespace BleSock.Windows
{
    [ServiceContract]
    internal interface IWcfPeripheralCallback
    {
        [OperationContract(IsOneWay = true)]
        void OnBluetoothRequire();

        [OperationContract(IsOneWay = true)]
        void OnReady();

        [OperationContract(IsOneWay = true)]
        void OnFail();

        [OperationContract(IsOneWay = true)]
        void OnConnect(int connectionId);

        [OperationContract(IsOneWay = true)]
        void OnDisconnect(int connectionId);

        [OperationContract(IsOneWay = true)]
        void OnReceiveDirect(byte[] message, int connectionId);

        [OperationContract(IsOneWay = true)]
        void OnReceive(byte[] message, int sender);
    }
}
