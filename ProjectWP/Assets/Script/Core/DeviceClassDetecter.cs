using UnityEngine;
using Application = UnityEngine.Device.Application;
using Screen = UnityEngine.Device.Screen;
using SystemInfo = UnityEngine.Device.SystemInfo;

public enum DeviceClass { PC, Tablet, Phone }

public static class DeviceClassDetecter
{
   public static DeviceClass Detect()
    {
        if(!Application.isMobilePlatform)return DeviceClass.PC;
        if(SystemInfo.deviceModel.StartsWith("ipad"))return DeviceClass.Tablet;

        // 端末の大きさを測れない場合は0で返る為、その場合はスマホ扱いにする
        if(Screen.dpi <= 0f) return DeviceClass.Phone;

        float w = Screen.width / Screen.dpi;
        float h = Screen.height / Screen.dpi;
        float diagonalInches = Mathf.Sqrt(w * w + h * h);
        return diagonalInches >= 7f ? DeviceClass.Tablet : DeviceClass.Phone;
    }
}
