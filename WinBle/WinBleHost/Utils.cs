using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Windows.Foundation;
using Windows.Storage.Streams;

namespace BleSock.Windows
{
    internal static class Utils
    {
        public static Task<T> AsTask<T>(this IAsyncOperation<T> operation, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }

            cancellationToken.Register(info =>
            {
                ((IAsyncInfo)info).Cancel();
            }, operation);

            return operation.AsTask();
        }

        public static Task<T> AsTask<T>(this IAsyncOperation<T> operation)
        {
            var result = new TaskCompletionSource<T>();

            operation.Completed += (asyncInfo, asyncStatus) =>
            {
                switch (asyncStatus)
                {
                    case AsyncStatus.Completed:
                        result.SetResult(operation.GetResults());
                        break;

                    case AsyncStatus.Canceled:
                        result.SetCanceled();
                        break;

                    case AsyncStatus.Error:
                        result.SetException(operation.ErrorCode);
                        break;
                }
            };

            return result.Task;
        }

        public static IBuffer AsBuffer(this byte[] bytes)
        {
            using (var writer = new DataWriter())
            {
                writer.WriteBytes(bytes);
                return writer.DetachBuffer();
            }
        }

        public static byte[] AsBytes(this IBuffer buffer)
        {
            var bytes = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer))
            {
                reader.ReadBytes(bytes);
            }

            return bytes;
        }

        public static void CompactStream(Stream stream)
        {
            var buff = new byte[stream.Length - stream.Position];
            stream.Read(buff, 0, buff.Length);
            stream.SetLength(0);
            stream.Write(buff, 0, buff.Length);
        }

        public static void Info(string format, params object[] args)
        {
            Console.WriteLine(format, args);
        }

        public static void Error(string format, params object[] args)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(format, args);
            Console.ResetColor();
        }
    }
}
