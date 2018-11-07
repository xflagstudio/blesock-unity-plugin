package xflag.plugins.bleSock;

import android.bluetooth.BluetoothAdapter;
import android.util.Log;

import java.util.Locale;

public final class Utils {

    public static boolean isBluetoothEnabled() {

        BluetoothAdapter adapter = BluetoothAdapter.getDefaultAdapter();
        if (adapter == null) {
            return false;
        }

        return adapter.isEnabled();
    }

    public static boolean isPeripheralAvailable() {

        BluetoothAdapter adapter = BluetoothAdapter.getDefaultAdapter();
        if (adapter == null) {
            return false;
        }

        return adapter.isMultipleAdvertisementSupported();
    }

    public static void info(String format, Object... args) {

        Log.i("BleSock", String.format(Locale.US, format, args));
    }

    public static void error(String format, Object... args) {

        Log.e("BleSock", String.format(Locale.US, format, args));
    }
}
