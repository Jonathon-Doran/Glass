using System;
using System.Net;
using System.Net.NetworkInformation;
using Glass.Core;
using Glass.Network.Protocol;
using SharpPcap;
using SharpPcap.LibPcap;
using SharpPcapCapture = SharpPcap.PacketCapture;
namespace Glass.Network.Capture;

///////////////////////////////////////////////////////////////////////////////////////////////
// PacketCapture
//
// Thin wrapper around SharpPcap/Npcap for live packet capture.
// Opens a capture device, applies a BPF filter targeting Daybreak Games
// IP ranges, and forwards decoded UDP payloads to a PacketRouter.
//
// SharpPcap fires OnPacketArrival on a background thread.  This class
// parses Ethernet + IPv4 + UDP headers to extract source/dest IP and port,
// then hands the UDP payload directly to the router.
//
// Start() begins capture.  Stop() ends it.  One instance per Glass session.
///////////////////////////////////////////////////////////////////////////////////////////////
public class PacketCapture
{
    private ILiveDevice? _device;
    private PacketRouter _router;
    private bool _capturing;
    private int _frameCount;

    // Ethernet header: 14 bytes (dst MAC 6 + src MAC 6 + ethertype 2)
    private const int EthernetHeaderLength = 14;
    private const ushort EtherTypeIPv4 = 0x0800;

    // IPv4 header minimum: 20 bytes
    private const int IPv4HeaderMinLength = 20;
    private const byte IPv4ProtocolUDP = 17;

    // UDP header: 8 bytes (src port 2 + dst port 2 + length 2 + checksum 2)
    private const int UdpHeaderLength = 8;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PacketCapture (constructor)
    //
    // router:  The packet router that will receive decoded UDP payloads
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PacketCapture(PacketRouter router)
    {
        _router = router;
        _device = null;
        _capturing = false;
        _frameCount = 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Start
    //
    // Opens the specified capture device, applies the provided BPF filter,
    // and begins capture.
    //
    // bpfFilter:    The BPF filter string to apply (e.g.
    //               "udp and (net 69.0.0.0/8 or net 64.0.0.0/8)")
    // deviceIndex:  Index into CaptureDeviceList.Instance.  Pass -1 to
    //               auto-select the first device with an IPv4 address.
    //
    // Returns true if capture started successfully.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool Start(string bpfFilter, int deviceIndex = -1)
    {
        if (_capturing)
        {
            DebugLog.Write("PacketCapture.Start: already capturing");
            return false;
        }

        if (string.IsNullOrEmpty(bpfFilter))
        {
            DebugLog.Write("PacketCapture.Start: no BPF filter provided");
            return false;
        }

        try
        {
            CaptureDeviceList deviceList = CaptureDeviceList.Instance;

            if (deviceList.Count == 0)
            {
                DebugLog.Write("PacketCapture.Start: no capture devices found. "
                    + "Is Npcap installed?");
                return false;
            }

            if (deviceIndex >= 0)
            {
                if (deviceIndex >= deviceList.Count)
                {
                    DebugLog.Write("PacketCapture.Start: device index "
                        + deviceIndex + " out of range (0-"
                        + (deviceList.Count - 1) + ")");
                    return false;
                }
                _device = deviceList[deviceIndex];
            }
            else
            {
                _device = SelectDefaultDevice(deviceList);

                if (_device == null)
                {
                    DebugLog.Write("PacketCapture.Start: no suitable device found");
                    return false;
                }
            }

            DebugLog.Write("PacketCapture.Start: opening device '"
                + _device.Description + "'");

            _device.Open();
            _device.Filter = bpfFilter;
            _device.OnPacketArrival += OnPacketArrival;
            _device.StartCapture();

            _capturing = true;
            _frameCount = 0;

            DebugLog.Write("PacketCapture.Start: capture started, filter='"
                + bpfFilter + "'");

            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write("PacketCapture.Start: failed to start capture: "
                + ex.Message);
            return false;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Stop
    //
    // Stops capture and closes the device.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Stop()
    {
        if (!_capturing)
        {
            return;
        }

        try
        {
            if (_device != null)
            {
                _device.OnPacketArrival -= OnPacketArrival;
                _device.StopCapture();
                _device.Close();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write("PacketCapture.Stop: error during shutdown: "
                + ex.Message);
        }

        _capturing = false;
        _device = null;

        DebugLog.Write("PacketCapture.Stop: capture stopped");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ListDevices
    //
    // Returns a list of available capture devices with their index and
    // description.  Useful for UI device selection.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string[] ListDevices()
    {
        CaptureDeviceList deviceList = CaptureDeviceList.Instance;
        string[] result = new string[deviceList.Count];

        for (int i = 0; i < deviceList.Count; i++)
        {
            result[i] = i + ": " + deviceList[i].Description
                + " (" + deviceList[i].Name + ")";
        }

        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OnPacketArrival
    //
    // Called by SharpPcap on its background capture thread for each packet
    // matching the BPF filter.  Parses Ethernet + IPv4 + UDP headers and
    // forwards the UDP payload to the router.
    //
    // sender:  The capture device
    // e:       The captured packet data
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void OnPacketArrival(object sender, SharpPcapCapture e)
    {
        RawCapture rawCapture = e.GetPacket();

        if (rawCapture == null)
        {
            return;
        }

        byte[] data = rawCapture.Data;
        int length = data.Length;

        // Parse Ethernet header
        if (length < EthernetHeaderLength)
        {
            return;
        }

        ushort etherType = (ushort)((data[12] << 8) | data[13]);

        if (etherType != EtherTypeIPv4)
        {
            return;
        }

        int ipOffset = EthernetHeaderLength;

        // Parse IPv4 header
        if (length < ipOffset + IPv4HeaderMinLength)
        {
            return;
        }

        int ipHeaderLength = (data[ipOffset] & 0x0F) * 4;

        if (ipHeaderLength < IPv4HeaderMinLength)
        {
            return;
        }

        byte protocol = data[ipOffset + 9];

        if (protocol != IPv4ProtocolUDP)
        {
            return;
        }

        string sourceIp = data[ipOffset + 12] + "."
            + data[ipOffset + 13] + "."
            + data[ipOffset + 14] + "."
            + data[ipOffset + 15];

        string destIp = data[ipOffset + 16] + "."
            + data[ipOffset + 17] + "."
            + data[ipOffset + 18] + "."
            + data[ipOffset + 19];

        int udpOffset = ipOffset + ipHeaderLength;

        // Parse UDP header
        if (length < udpOffset + UdpHeaderLength)
        {
            return;
        }

        int sourcePort = (data[udpOffset] << 8) | data[udpOffset + 1];
        int destPort = (data[udpOffset + 2] << 8) | data[udpOffset + 3];

        int udpPayloadOffset = udpOffset + UdpHeaderLength;
        int udpPayloadLength = length - udpPayloadOffset;

        if (udpPayloadLength <= 0)
        {
            return;
        }

        ReadOnlySpan<byte> udpPayload = new ReadOnlySpan<byte>(
            data, udpPayloadOffset, udpPayloadLength);

        _router.RoutePacket(udpPayload, udpPayloadLength,
                            sourceIp, sourcePort, destIp, destPort, _frameCount);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SelectDefaultDevice
    //
    // Selects the first capture device that has an IPv4 address assigned.
    // Skips loopback and devices with no addresses.
    //
    // deviceList:  The list of available capture devices
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private ILiveDevice? SelectDefaultDevice(CaptureDeviceList deviceList)
    {
        for (int i = 0; i < deviceList.Count; i++)
        {
            ILiveDevice device = deviceList[i];

            if (device is LibPcapLiveDevice libPcapDevice)
            {
                foreach (PcapAddress address in libPcapDevice.Addresses)
                {
                    if (address.Addr != null &&
                        address.Addr.ipAddress != null &&
                        address.Addr.ipAddress.AddressFamily ==
                            System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        DebugLog.Write("PacketCapture.SelectDefaultDevice: selected '"
                            + device.Description + "' with IP "
                            + address.Addr.ipAddress);
                        return device;
                    }
                }
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------------
    // Properties
    // ---------------------------------------------------------------------------

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IsCapturing
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool IsCapturing
    {
        get { return _capturing; }
    }
}