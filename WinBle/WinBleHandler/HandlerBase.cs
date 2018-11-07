using System;
using System.IO;
using System.Diagnostics;
using System.Threading;

using UnityEngine;

namespace BleSock.Windows
{
    public abstract class HandlerBase : IDisposable
    {
        // Methods

        public virtual void Cleanup()
        {
            ShutdownProcess();
        }

        public virtual void Dispose()
        {
            Cleanup();
        }

        // Internal

        protected Process mProcess;

        protected void LaunchProcess(string executionFilePath, string arguments)
        {
            var startInfo = new ProcessStartInfo()
            {
                FileName = Path.Combine(Application.dataPath, executionFilePath),
                Arguments = arguments,
                UseShellExecute = false,
            };

            mProcess = Process.Start(startInfo);
            mProcess.EnableRaisingEvents = true;
            mProcess.Exited += (sender, args) =>
            {
                OnProcessExited();
            };
        }

        protected void ShutdownProcess()
        {
            if (mProcess != null)
            {
                if (!mProcess.HasExited)
                {
                    mProcess.Kill();
                }

                mProcess.Dispose();
                mProcess = null;
            }
        }

        protected virtual void OnProcessExited()
        {
            mProcess.Dispose();
            mProcess = null;
        }
    }
}
