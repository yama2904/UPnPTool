using UPnPLibrary;
using UPnPLibrary.Description.Device;
using UPnPLibrary.Description.Service;

namespace UPnPTool
{
    /// <summary>
    /// UPnPデバイス情報
    /// </summary>
    public  class UPnPDevice
    {
        /// <summary>
        /// UPnPデバイスアクセス情報
        /// </summary>
        public UPnPDeviceAccess DeviceAccess { get; set; } = null;

        /// <summary>
        /// UPnPデバイス情報
        /// </summary>
        public DeviceDescription DeviceDescription { get; set; } = null;

        /// <summary>
        /// UPnPサービス詳細情報
        /// </summary>
        public ServiceDescription ServiceDescription { get; set; } = null;
    }
}
