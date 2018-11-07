using System;

using UnityEngine;

namespace BleSock
{

#if UNITY_ANDROID && !UNITY_EDITOR

    internal static class AndroidUtils
    {
        // Methods

        public static bool IsBluetoothEnabled
        {
            get
            {
                try
                {
                    using (var utils = new AndroidJavaClass(NAME_PREFIX + "Utils"))
                    {
                        return utils.CallStatic<bool>("isBluetoothEnabled");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                return false;
            }
        }

        public static bool IsPeripheralAvailable
        {
            get
            {
                try
                {
                    using (var utils = new AndroidJavaClass(NAME_PREFIX + "Utils"))
                    {
                        return utils.CallStatic<bool>("isPeripheralAvailable");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                return false;
            }
        }

        // Internal

        private const string NAME_PREFIX = "xflag.plugins.bleSock.";
    }

#endif

}
