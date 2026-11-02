
adb shell am force-stop aero.titandynamics.titanplanner

adb shell setprop debug.mono.profile log:sample
rem adb shell setprop debug.mono.profile log:calls
adb shell run-as aero.titandynamics.titanplanner ls -l files/.__override__

adb shell am broadcast -a "mono.android.intent.action.EXTERNAL_STORAGE_DIRECTORY" -n "Mono.Android.DebugRuntime/com.xamarin.mono.android.ExternalStorageDirectory"
adb shell am start -a "android.intent.action.MAIN" -c "android.intent.category.LAUNCHER" -n "aero.titandynamics.titanplanner/crc64d01531a0ef5679fa.MainActivity"

echo press after finished run
pause
adb exec-out "run-as aero.titandynamics.titanplanner cat /data/data/aero.titandynamics.titanplanner/files/.__override__/profile.mlpd" > profile2.mlpd

adb shell setprop debug.mono.profile \"\"

pause
